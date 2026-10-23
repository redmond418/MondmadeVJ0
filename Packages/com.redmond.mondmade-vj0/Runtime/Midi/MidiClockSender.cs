using System;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace MondmadeVJ0
{
    [DisallowMultipleComponent]
    public sealed class MidiClockSender : MonoBehaviour, IRuntimeModule
    {
        private const int ClockQueueLookaheadMilliseconds = 40;
        [SerializeField, Min(-1)] private int outputPortOrdinal = -1;
        [SerializeField] private bool sendSongPosition = true;
        private static readonly byte[] StartMessage = { 0xFA };
        private static readonly byte[] ContinueMessage = { 0xFB };
        private static readonly byte[] StopMessage = { 0xFC };
        private readonly object scheduleLock = new object();
        private readonly MidiClockSendMetrics metrics = new MidiClockSendMetrics();
        private RuntimeServices services;
        private IMusicalTransport transport;
        private MidiOutputLease output;
        private Thread worker;
        private volatile bool stopping;
        private bool scheduleRunning;
        private bool hasEverStarted;
        private long epochTicks;
        private long nextPulseIndex;
        private double pulsePeriodTicks;
        private int scheduleGeneration;
        private long seekResyncCount;
        private long scheduleOutputGeneration;
        private long outputGeneration;
        private long scheduledTimelineRevision;
        private int clockOwner;
        private double scheduledBpm;
        private double nextReconnectTime;
        private string status = "Not initialized";

        public string ModuleName => nameof(MidiClockSender);
        public string Status => status;
        public long SentPulses => metrics.SentPulses;
        public long MissedPulses => metrics.MissedPulses;
        public long SeekResyncCount => Interlocked.Read(ref seekResyncCount);
        public long DroppedOutputMessages => output == null ? 0 : output.DroppedMessages;
        public double MaximumLatenessMilliseconds => metrics.MaximumLatenessMilliseconds;
        public double MaximumDeadlineDriftMilliseconds => metrics.MaximumDeadlineDriftMilliseconds;
        public double P99LatenessMilliseconds => metrics.P99LatenessMilliseconds;
        public string OutputBackend => output?.ClockOutputBackend ?? "Unavailable";
        public double OutputScheduleLeadMilliseconds => output?.ClockScheduleLeadMilliseconds ?? 0d;
        public string OutputBackendDiagnostic => output?.ClockOutputDiagnostic ?? string.Empty;
        public string OutputTimingMetricBasis => OutputBackend == "CoreMIDI timestamped"
            ? "MIDISend entry timestamp vs target deadline minus schedule lead; receiver timing requires external measurement"
            : "SendMessage call vs target deadline";

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
            if (output != null && output.TakeError() is string error)
            {
                status = "Disconnected";
                services?.Report("MIDI_CLOCK_SEND", "Shared MIDI output worker failed.", exception: new InvalidOperationException(error));
            }
            if (output == null && Time.realtimeSinceStartupAsDouble >= nextReconnectTime)
            {
                nextReconnectTime = Time.realtimeSinceStartupAsDouble + 1d;
                TryOpenOutput();
                if (output != null) StartWorker();
            }
            if (output == null) return;

            var currentOutputGeneration = output.ConnectionGeneration;
            if (currentOutputGeneration != outputGeneration)
            {
                outputGeneration = currentOutputGeneration;
                StopSchedule();
            }
            if (!output.IsOk)
            {
                StopSchedule();
                status = output.Status;
                return;
            }

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
            var timelineRevision = transport.TimelineRevision;
            var changedTempo = Math.Abs(bpm - scheduledBpm) > 1e-6d;
            var expectedBeat = scheduleRunning ? GetExpectedBeatAtTime(epochTicks, nowTicks, scheduledBpm) : beat;
            var seekThresholdBeat = GetSeekThresholdBeat(bpm, Time.unscaledDeltaTime);
            var seeked = scheduleRunning && HasTimelineSeeked(
                scheduledTimelineRevision, timelineRevision, beat, expectedBeat, seekThresholdBeat);

            if (!scheduleRunning)
            {
                if (sendSongPosition && beat > 0d) SendSongPosition(beat);
                Send(hasEverStarted || beat > 0d ? ContinueMessage : StartMessage);
                hasEverStarted = true;
                SetSchedule(beat, bpm, nowTicks, timelineRevision);
            }
            else if (changedTempo || seeked)
            {
                if (seeked) Interlocked.Increment(ref seekResyncCount);
                if (seeked && sendSongPosition) SendSongPosition(beat);
                SetSchedule(beat, bpm, nowTicks, timelineRevision);
            }
            status = "Connected";
        }

        private void TryOpenOutput()
        {
            output = MidiOutputDispatcher.Acquire(outputPortOrdinal, ModuleName, out var error);
            if (output == null)
            {
                status = "Waiting for MIDI output port";
                if (!string.IsNullOrEmpty(error))
                    services?.Report("MIDI_CLOCK_OUTPUT", "Could not acquire MIDI Clock output port.", exception: new InvalidOperationException(error));
                return;
            }

            clockOwner = GetInstanceID();
            if (clockOwner == 0) clockOwner = 1;
            if (!output.TryClaimClock(clockOwner))
            {
                output.Dispose();
                output = null;
                status = "Clock output is already claimed";
                services?.Report("MIDI_CLOCK_OUTPUT_CONFLICT", "Only one MIDI Clock sender can own a shared output port.");
                return;
            }
            outputGeneration = output.ConnectionGeneration;
            output.SetClockScheduleGeneration(clockOwner, scheduleGeneration);
            status = output.Status;
        }

        private void StartWorker()
        {
            if (worker != null) return;
            stopping = false;
            worker = new Thread(ClockLoop) { IsBackground = true, Name = "MondmadeVJ0 MIDI Clock" };
            try { worker.Priority = System.Threading.ThreadPriority.Highest; }
            catch { }
            worker.Start();
        }

        private void SetSchedule(double beat, double bpm, long nowTicks, long timelineRevision)
        {
            var ticksPerBeat = Stopwatch.Frequency * 60d / bpm;
            lock (scheduleLock)
            {
                epochTicks = nowTicks - (long)(beat * ticksPerBeat);
                pulsePeriodTicks = ticksPerBeat / 24d;
                nextPulseIndex = (long)Math.Floor(beat * 24d + 1e-8d) + 1L;
                scheduledBpm = bpm;
                scheduledTimelineRevision = timelineRevision;
                scheduleOutputGeneration = output == null ? 0 : output.ConnectionGeneration;
                scheduleRunning = true;
                scheduleGeneration++;
                output?.SetClockScheduleGeneration(clockOwner, scheduleGeneration);
            }
        }

        private void StopSchedule()
        {
            lock (scheduleLock)
            {
                if (!scheduleRunning) return;
                scheduleRunning = false;
                scheduleGeneration++;
                output?.SetClockScheduleGeneration(clockOwner, scheduleGeneration);
            }
        }

        private void ClockLoop()
        {
            using (MidiThreadScheduling.RequestRealtimeScheduling()) ClockLoopCore();
        }

        private void ClockLoopCore()
        {
            while (!stopping)
            {
                long epoch, index, outputGenerationAtSchedule;
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
                    outputGenerationAtSchedule = scheduleOutputGeneration;
                }
                if (!running || period <= 0d) { Thread.Sleep(2); continue; }
                var lease = output;
                if (lease == null || !lease.IsOk || lease.ConnectionGeneration != outputGenerationAtSchedule)
                { Thread.Sleep(2); continue; }

                var deadline = epoch + (long)(index * period);
                var remaining = deadline - Stopwatch.GetTimestamp();
                var queueLookaheadTicks = Stopwatch.Frequency * ClockQueueLookaheadMilliseconds / 1000d;
                if (remaining > queueLookaheadTicks)
                {
                    // Keep several future pulses ready so OS wake jitter cannot make the scheduler skip one.
                    var milliseconds = (int)(remaining * 1000d / Stopwatch.Frequency) - ClockQueueLookaheadMilliseconds - 1;
                    if (milliseconds > 0) Thread.Sleep(milliseconds);
                    continue;
                }

                var queued = false;
                lock (scheduleLock)
                {
                    if (!scheduleRunning || generation != scheduleGeneration || index != nextPulseIndex) continue;
                    var schedulingNow = Stopwatch.GetTimestamp();
                    if (IsClockPulseTooLateToQueue(deadline, schedulingNow, period))
                    {
                        var latestDueIndex = GetLatestDuePulseIndex(epoch, schedulingNow, period, index);
                        if (latestDueIndex > index)
                        {
                            metrics.RecordMissed(latestDueIndex - index);
                            nextPulseIndex = latestDueIndex;
                            continue;
                        }
                    }
                    nextPulseIndex++;
                    var periodTicks = Math.Max(1L, (long)period);
                    queued = lease.QueueClock(deadline, periodTicks, generation, metrics);
                }
                if (!queued) metrics.RecordMissed();
            }
        }

        internal static long GetLatestDuePulseIndex(long epoch, long now, double period, long minimumIndex)
        {
            if (!(period > 0d) || double.IsNaN(period) || double.IsInfinity(period)) return minimumIndex;

            var elapsed = (double)(now - epoch);
            var estimate = Math.Ceiling(elapsed / period);
            if (estimate < minimumIndex) estimate = minimumIndex;
            if (estimate > long.MaxValue - 1d) return long.MaxValue;
            var firstNotEarlierIndex = (long)estimate;
            var firstDeadline = epoch + (long)(firstNotEarlierIndex * period);

            if (firstDeadline < now)
            {
                firstNotEarlierIndex++;
                firstDeadline = epoch + (long)(firstNotEarlierIndex * period);
            }
            else if (firstNotEarlierIndex > minimumIndex)
            {
                var previousDeadline = epoch + (long)((firstNotEarlierIndex - 1L) * period);
                if (previousDeadline >= now) firstNotEarlierIndex--;
            }

            var candidateDeadline = epoch + (long)(firstNotEarlierIndex * period);
            return candidateDeadline <= now ? firstNotEarlierIndex : Math.Max(minimumIndex, firstNotEarlierIndex - 1L);
        }

        internal static double GetSeekThresholdBeat(double bpm, double frameDeltaSeconds)
        {
            var frameBeatSpan = Math.Max(0d, frameDeltaSeconds) * Math.Max(0d, bpm) / 60d;
            return Math.Max(0.1d, frameBeatSpan * 2d);
        }

        internal static double GetExpectedBeatAtTime(long epochTicks, long nowTicks, double bpm)
            => (nowTicks - epochTicks) * bpm / (Stopwatch.Frequency * 60d);

        internal static bool HasTimelineSeeked(long scheduledRevision, long currentRevision,
            double beat, double expectedBeat, double thresholdBeat)
            => scheduledRevision != currentRevision || Math.Abs(beat - expectedBeat) > thresholdBeat;

        internal static bool IsClockPulseTooLateToQueue(long deadlineTicks, long nowTicks, double periodTicks)
            => periodTicks > 0d && nowTicks - deadlineTicks >= periodTicks;

        private void SendSongPosition(double beat)
        {
            var sixteenths = Mathf.Clamp((int)Math.Floor(Math.Max(0d, beat) * 4d), 0, 16383);
            Send(new[] { (byte)0xF2, (byte)(sixteenths & 0x7F), (byte)((sixteenths >> 7) & 0x7F) });
        }

        private void Send(byte[] message)
        {
            if (output == null || !output.QueueControl(message))
                services?.Report("MIDI_CLOCK_SEND_QUEUE", "Could not queue a MIDI Clock transport message.");
        }

        public void Shutdown(RuntimeServices runtimeServices)
        {
            var shouldStop = scheduleRunning;
            stopping = true;
            StopSchedule();
            if (worker != null && worker != Thread.CurrentThread) worker.Join(2000);
            worker = null;
            if (shouldStop) Send(StopMessage);
            if (output != null)
            {
                output.ReleaseClock(clockOwner);
                output.Dispose();
                output = null;
            }
            transport = null;
            services = null;
            hasEverStarted = false;
            status = "Stopped";
        }
    }
}
