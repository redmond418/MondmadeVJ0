using System;
using System.Diagnostics;
using System.Threading;
using RtMidi;
using UnityEngine;

namespace MondmadeVJ0
{
    [DisallowMultipleComponent]
    public sealed class MidiClockSender : MonoBehaviour, IRuntimeModule
    {
        [SerializeField, Min(-1)] private int outputPortOrdinal = -1;
        [SerializeField] private bool sendSongPosition = true;
        private readonly object scheduleLock = new object();
        private readonly object sendLock = new object();
        private readonly long[] latenessSamples = new long[131072];
        private static readonly byte[] ClockPulse = { 0xF8 };
        private static readonly byte[] StartMessage = { 0xFA };
        private static readonly byte[] ContinueMessage = { 0xFB };
        private static readonly byte[] StopMessage = { 0xFC };
        private RuntimeServices services;
        private IMusicalTransport transport;
        private MidiOut output;
        private Thread worker;
        private volatile bool stopping;
        private bool scheduleRunning;
        private bool hasEverStarted;
        private long epochTicks;
        private long nextPulseIndex;
        private double pulsePeriodTicks;
        private int scheduleGeneration;
        private double scheduledBpm;
        private double scheduledBeat;
        private double nextReconnectTime;
        private string status = "Not initialized";
        private string workerError;
        private long sentPulses;
        private long maximumLatenessMicroseconds;
        private long latenessSampleCount;

        public string ModuleName => nameof(MidiClockSender);
        public string Status => status;
        public long SentPulses => Interlocked.Read(ref sentPulses);
        public double MaximumLatenessMilliseconds => Interlocked.Read(ref maximumLatenessMicroseconds) / 1000d;
        public double P99LatenessMilliseconds => GetP99LatenessMilliseconds();

        public void Register(RuntimeServices runtimeServices) { services = runtimeServices; runtimeServices.Register(this); }

        public void Initialize(RuntimeServices runtimeServices)
        {
            services = runtimeServices;
            transport = runtimeServices.Get<IMusicalTransport>();
            if (outputPortOrdinal < 0) { status = "Not configured"; return; }
            TryOpenOutput();
            if (output != null) StartWorker();
        }

        public void DrainInputQueue(RuntimeServices runtimeServices, int maxEvents) { }
        public void Tick(RuntimeServices runtimeServices, double deltaTime) { }

        public void FlushFrame(RuntimeServices runtimeServices)
        {
            if (stopping) return;
            if (workerError != null)
            {
                var error = workerError;
                workerError = null;
                status = "Error";
                services?.Report("MIDI_CLOCK_SEND", "MIDI Clock sender worker failed.", exception: new InvalidOperationException(error));
                lock (sendLock)
                {
                    output?.Dispose();
                    output = null;
                }
            }
            if ((output == null || !output.IsOk) && Time.realtimeSinceStartupAsDouble >= nextReconnectTime)
            {
                nextReconnectTime = Time.realtimeSinceStartupAsDouble + 1d;
                TryOpenOutput();
                if (output != null) StartWorker();
            }
            if (output == null) return;

            if (!transport.IsRunning)
            {
                if (scheduleRunning)
                {
                    StopSchedule();
                    Send(StopMessage);
                }
                status = "Stopped";
                return;
            }

            var beat = transport.BeatPosition;
            var bpm = transport.EffectiveBpm;
            if (!(bpm > 0d) || double.IsNaN(bpm) || double.IsInfinity(bpm)) return;
            var nowTicks = Stopwatch.GetTimestamp();
            var changedTempo = Math.Abs(bpm - scheduledBpm) > 1e-6d;
            var expectedBeat = scheduleRunning ? scheduledBeat + (nowTicks - epochTicks) * scheduledBpm / (Stopwatch.Frequency * 60d) : beat;
            var seeked = scheduleRunning && Math.Abs(beat - expectedBeat) > 0.1d;

            if (!scheduleRunning)
            {
                if (sendSongPosition && beat > 0d) SendSongPosition(beat);
                Send(hasEverStarted || beat > 0d ? ContinueMessage : StartMessage);
                hasEverStarted = true;
                SetSchedule(beat, bpm, nowTicks);
            }
            else if (changedTempo || seeked)
            {
                if (seeked && sendSongPosition) SendSongPosition(beat);
                SetSchedule(beat, bpm, nowTicks);
            }
            status = "Connected";
        }

        private void TryOpenOutput()
        {
            MidiOut candidate = null;
            try
            {
                lock (sendLock)
                {
                    output?.Dispose();
                    output = null;
                    candidate = MidiOut.Create();
                    if (outputPortOrdinal < 0 || outputPortOrdinal >= candidate.PortCount)
                    {
                        status = "Waiting for MIDI output port";
                        candidate.Dispose();
                        candidate = null;
                        return;
                    }
                    candidate.OpenPort(outputPortOrdinal, "Mondmade VJ0 MIDI Clock");
                    output = candidate;
                    candidate = null;
                    status = "Connected";
                }
                lock (scheduleLock) { scheduleRunning = false; scheduleGeneration++; }
            }
            catch (Exception exception)
            {
                candidate?.Dispose();
                output = null;
                status = "Disconnected";
                services?.Report("MIDI_CLOCK_OUTPUT", "Could not open MIDI Clock output port.", exception: exception);
            }
        }

        private void StartWorker()
        {
            if (worker != null) return;
            stopping = false;
            worker = new Thread(ClockLoop) { IsBackground = true, Name = "MondmadeVJ0 MIDI Clock" };
            try { worker.Priority = System.Threading.ThreadPriority.AboveNormal; }
            catch { }
            worker.Start();
        }

        private void SetSchedule(double beat, double bpm, long nowTicks)
        {
            var ticksPerBeat = Stopwatch.Frequency * 60d / bpm;
            lock (scheduleLock)
            {
                epochTicks = nowTicks - (long)(beat * ticksPerBeat);
                pulsePeriodTicks = ticksPerBeat / 24d;
                nextPulseIndex = (long)Math.Floor(beat * 24d + 1e-8d) + 1L;
                scheduledBpm = bpm;
                scheduledBeat = beat;
                scheduleRunning = true;
                scheduleGeneration++;
            }
        }

        private void StopSchedule()
        {
            lock (scheduleLock)
            {
                scheduleRunning = false;
                scheduleGeneration++;
            }
        }

        private void ClockLoop()
        {
            while (!stopping)
            {
                long epoch, index;
                double period;
                int generation;
                bool running;
                lock (scheduleLock)
                {
                    running = scheduleRunning;
                    epoch = epochTicks;
                    index = nextPulseIndex;
                    period = pulsePeriodTicks;
                    generation = scheduleGeneration;
                }
                if (!running || period <= 0d) { Thread.Sleep(2); continue; }

                var deadline = epoch + (long)(index * period);
                var remaining = deadline - Stopwatch.GetTimestamp();
                if (remaining > Stopwatch.Frequency / 500d)
                {
                    var milliseconds = (int)(remaining * 1000d / Stopwatch.Frequency) - 1;
                    if (milliseconds > 0) Thread.Sleep(milliseconds);
                    continue;
                }
                if (remaining > 0) { Thread.SpinWait(32); continue; }

                lock (scheduleLock)
                {
                    if (!scheduleRunning || generation != scheduleGeneration || index != nextPulseIndex) continue;
                    nextPulseIndex++;
                }
                var late = Stopwatch.GetTimestamp() - deadline;
                var lateMicroseconds = (long)Math.Max(0d, late * 1_000_000d / Stopwatch.Frequency);
                UpdateMaximum(ref maximumLatenessMicroseconds, lateMicroseconds);
                var sampleIndex = Interlocked.Increment(ref latenessSampleCount) - 1;
                Interlocked.Exchange(ref latenessSamples[(int)(sampleIndex % latenessSamples.Length)], lateMicroseconds);
                try { Send(ClockPulse); Interlocked.Increment(ref sentPulses); }
                catch (Exception exception)
                {
                    workerError = exception.ToString();
                    lock (scheduleLock) { scheduleRunning = false; scheduleGeneration++; }
                    Thread.Sleep(10);
                }
            }
        }

        private double GetP99LatenessMilliseconds()
        {
            var total = Math.Min(Interlocked.Read(ref latenessSampleCount), latenessSamples.Length);
            if (total <= 0) return 0d;
            var samples = new long[(int)total];
            var start = Math.Max(0L, Interlocked.Read(ref latenessSampleCount) - total);
            for (var i = 0; i < samples.Length; i++) samples[i] = Interlocked.Read(ref latenessSamples[(int)((start + i) % latenessSamples.Length)]);
            Array.Sort(samples);
            var index = Math.Min(samples.Length - 1, (int)Math.Ceiling(samples.Length * 0.99d) - 1);
            return samples[index] / 1000d;
        }

        private static void UpdateMaximum(ref long target, long value)
        {
            long current;
            while (value > (current = Interlocked.Read(ref target)) && Interlocked.CompareExchange(ref target, value, current) != current) { }
        }

        private void SendSongPosition(double beat)
        {
            var sixteenths = Mathf.Clamp((int)Math.Floor(Math.Max(0d, beat) * 4d), 0, 16383);
            Send(new[] { (byte)0xF2, (byte)(sixteenths & 0x7F), (byte)((sixteenths >> 7) & 0x7F) });
        }

        private void Send(byte[] message)
        {
            lock (sendLock)
            {
                if (output == null || !output.IsOk) throw new InvalidOperationException("MIDI Clock output is disconnected.");
                output.SendMessage(message);
            }
        }

        public void Shutdown(RuntimeServices runtimeServices)
        {
            stopping = true;
            StopSchedule();
            if (worker != null && worker != Thread.CurrentThread) worker.Join(2000);
            worker = null;
            lock (sendLock)
            {
                output?.Dispose();
                output = null;
            }
            transport = null;
            services = null;
            hasEverStarted = false;
            status = "Stopped";
        }
    }
}
