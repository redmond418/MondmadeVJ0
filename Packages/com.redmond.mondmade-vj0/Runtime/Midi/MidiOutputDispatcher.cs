using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using RtMidi;

namespace MondmadeVJ0
{
    internal static class MidiOutputDispatcher
    {
        private static readonly object RegistryLock = new object();
        private static readonly Dictionary<int, Entry> Ports = new Dictionary<int, Entry>();

        public static MidiOutputLease Acquire(int ordinal, string clientName, out string error)
        {
            error = null;
            if (ordinal < 0)
            {
                error = "The MIDI output port ordinal is not configured.";
                return null;
            }

            string portName;
            try
            {
                using (var probe = MidiOut.Create())
                {
                    if (ordinal >= probe.PortCount)
                    {
                        error = "The configured MIDI output port is not currently available.";
                        return null;
                    }
                    portName = probe.GetPortName(ordinal);
                }
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return null;
            }

            lock (RegistryLock)
            {
                if (Ports.TryGetValue(ordinal, out var entry))
                {
                    if (!string.Equals(entry.PortName, portName, StringComparison.Ordinal))
                    {
                        error = "The configured MIDI output ordinal now refers to a different port.";
                        return null;
                    }
                    entry.ReferenceCount++;
                    return new MidiOutputLease(entry.Port);
                }

                var port = new MidiOutputPort(ordinal, portName, clientName);
                Ports.Add(ordinal, new Entry(portName, port));
                return new MidiOutputLease(port);
            }
        }

        internal static void Release(MidiOutputPort port)
        {
            var dispose = false;
            lock (RegistryLock)
            {
                if (!Ports.TryGetValue(port.Ordinal, out var entry) || !ReferenceEquals(entry.Port, port)) return;
                entry.ReferenceCount--;
                if (entry.ReferenceCount <= 0)
                {
                    Ports.Remove(port.Ordinal);
                    dispose = true;
                }
            }
            if (dispose) port.Dispose();
        }

        private sealed class Entry
        {
            public readonly string PortName;
            public readonly MidiOutputPort Port;
            public int ReferenceCount = 1;
            public Entry(string portName, MidiOutputPort port) { PortName = portName; Port = port; }
        }
    }

    internal sealed class MidiOutputLease : IDisposable
    {
        private MidiOutputPort port;
        internal MidiOutputLease(MidiOutputPort port) { this.port = port; }

        public bool IsOk => port != null && port.IsConnected;
        public string Status => port == null ? "Closed" : port.Status;
        public string TakeError() => port?.TakeError();
        public long ConnectionGeneration => port == null ? 0 : port.ConnectionGeneration;
        public int PortOrdinal => port == null ? -1 : port.Ordinal;
        public long DroppedMessages => port == null ? 0 : port.DroppedMessages;
        public int PendingMessageCount => port == null ? 0 : port.PendingMessageCount;
        public bool IsIdle => port == null || port.IsIdle;
        public bool TryClaimClock(int owner) => port != null && port.TryClaimClock(owner);
        public bool SetClockScheduleGeneration(int owner, int generation) => port != null && port.SetClockScheduleGeneration(owner, generation);
        public void ReleaseClock(int owner) => port?.ReleaseClock(owner);
        public bool QueueControl(byte[] message) => port != null && port.QueueControl(message);
        public bool QueueLed(string key, byte[] message) => port != null && port.QueueLed(key, message);
        public bool QueueLed(string key, byte status, byte number, byte value) => port != null && port.QueueLed(key, status, number, value);
        public bool QueueClock(long deadlineTicks, long periodTicks, int generation, MidiClockSendMetrics metrics)
            => port != null && port.QueueClock(deadlineTicks, periodTicks, generation, metrics);

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref port, null);
            if (current != null) MidiOutputDispatcher.Release(current);
        }
    }

    internal sealed class MidiOutputPort : IDisposable
    {
        private readonly object queueLock = new object();
        private readonly object clockSendLock = new object();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly MidiOutputWorkQueue queue = new MidiOutputWorkQueue();
        private readonly Thread worker;
        private readonly string expectedPortName;
        private readonly string clientName;
        private volatile bool stopping;
        private int connected;
        private string status = "Waiting for MIDI output port";
        private string lastError;
        private RtMidi.MidiOut output;
        private int clockOwner;
        private int clockScheduleGeneration;
        private long connectionGeneration;
        private int inFlight;

        public int Ordinal { get; }
        public bool IsConnected => Volatile.Read(ref connected) != 0;
        public string Status => Volatile.Read(ref status);
        public long ConnectionGeneration => Interlocked.Read(ref connectionGeneration);
        public long DroppedMessages { get { lock (queueLock) return queue.DroppedMessages; } }
        public int PendingMessageCount { get { lock (queueLock) return queue.Count + Volatile.Read(ref inFlight); } }
        public bool IsIdle => PendingMessageCount == 0;

        public MidiOutputPort(int ordinal, string portName, string clientName)
        {
            Ordinal = ordinal;
            expectedPortName = portName;
            this.clientName = string.IsNullOrEmpty(clientName) ? "Output" : clientName;
            worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "MondmadeVJ0 MIDI Output " + ordinal
            };
            try { worker.Priority = ThreadPriority.Highest; }
            catch { }
            worker.Start();
        }

        public bool TryClaimClock(int owner)
        {
            lock (queueLock)
            {
                if (clockOwner != 0 && clockOwner != owner) return false;
                clockOwner = owner;
                Volatile.Write(ref clockScheduleGeneration, 0);
                queue.SetClockGeneration(0);
                return true;
            }
        }

        public bool SetClockScheduleGeneration(int owner, int generation)
        {
            lock (clockSendLock)
            {
                lock (queueLock)
                {
                    if (clockOwner != owner || owner == 0) return false;
                    Volatile.Write(ref clockScheduleGeneration, generation);
                    queue.SetClockGeneration(generation);
                }
            }
            wake.Set();
            return true;
        }

        public void ReleaseClock(int owner)
        {
            lock (clockSendLock)
            {
                lock (queueLock)
                {
                    if (clockOwner == owner)
                    {
                        clockOwner = 0;
                        Volatile.Write(ref clockScheduleGeneration, 0);
                        queue.SetClockGeneration(0);
                    }
                }
            }
            wake.Set();
        }

        public bool QueueControl(byte[] message)
        {
            if (message == null || message.Length == 0) return false;
            lock (queueLock)
            {
                if (stopping) return false;
                var accepted = queue.EnqueueControl(message);
                if (accepted) wake.Set();
                return accepted;
            }
        }

        public bool QueueLed(string key, byte[] message)
        {
            if (string.IsNullOrEmpty(key) || message == null || message.Length == 0) return false;
            lock (queueLock)
            {
                if (stopping) return false;
                var accepted = queue.EnqueueLed(key, message);
                if (accepted) wake.Set();
                return accepted;
            }
        }

        public bool QueueLed(string key, byte statusByte, byte number, byte value)
        {
            if (string.IsNullOrEmpty(key)) return false;
            lock (queueLock)
            {
                if (stopping) return false;
                var accepted = queue.EnqueueLed(key, statusByte, number, value);
                if (accepted) wake.Set();
                return accepted;
            }
        }

        public bool QueueClock(long deadlineTicks, long periodTicks, int generation, MidiClockSendMetrics metrics)
        {
            lock (queueLock)
            {
                if (stopping || clockOwner == 0) return false;
                var accepted = queue.EnqueueClock(deadlineTicks, periodTicks, generation, metrics);
                if (accepted) wake.Set();
                return accepted;
            }
        }

        public string TakeError()
        {
            lock (queueLock)
            {
                var error = lastError;
                lastError = null;
                return error;
            }
        }

        private void WorkerLoop()
        {
            var nextOpenAttempt = 0L;
            while (true)
            {
                if (stopping && PendingMessageCount == 0) break;

                if (output == null || !output.IsOk)
                {
                    if (output != null) MarkDisconnected(null, null);
                    lock (queueLock) queue.DiscardRealtime();
                    if (stopping) break;
                    var now = Stopwatch.GetTimestamp();
                    if (now >= nextOpenAttempt)
                    {
                        nextOpenAttempt = now + Stopwatch.Frequency;
                        TryOpenOutput();
                        continue;
                    }
                    var wait = (int)Math.Max(1d, Math.Min(100d, (nextOpenAttempt - now) * 1000d / Stopwatch.Frequency));
                    wake.WaitOne(wait);
                    continue;
                }

                MidiOutputPacket packet;
                bool hasPacket;
                lock (queueLock)
                {
                    hasPacket = queue.TryDequeue(out packet);
                }
                if (!hasPacket)
                {
                    if (stopping) break;
                    wake.WaitOne(100);
                    continue;
                }

                Interlocked.Increment(ref inFlight);
                try
                {
                    try
                    {
                        if (packet.Kind == MidiOutputPacketKind.Clock)
                        {
                            if (!WaitForClockDeadline(packet)) continue;

                            lock (clockSendLock)
                            {
                                if (packet.Generation != Volatile.Read(ref clockScheduleGeneration) ||
                                    Stopwatch.GetTimestamp() - packet.DeadlineTicks >= packet.PeriodTicks)
                                {
                                    packet.ClockMetrics?.RecordMissed();
                                    continue;
                                }

                                var invocationTicks = Stopwatch.GetTimestamp();
                                output.SendMessage(packet.Message);
                                packet.ClockMetrics?.RecordSent(packet.DeadlineTicks, invocationTicks, packet.Generation);
                            }
                        }
                        else output.SendMessage(packet.Message);
                    }
                    catch (Exception exception)
                    {
                        if (packet.Kind == MidiOutputPacketKind.Clock) packet.ClockMetrics?.RecordMissed();
                        MarkDisconnected(packet, exception);
                        nextOpenAttempt = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
                    }
                }
                finally { Interlocked.Decrement(ref inFlight); }
            }

            CloseOutput();
            Volatile.Write(ref connected, 0);
            Volatile.Write(ref status, "Closed");
        }

        private bool WaitForClockDeadline(MidiOutputPacket packet)
        {
            while (!stopping)
            {
                if (packet.Generation != Volatile.Read(ref clockScheduleGeneration))
                {
                    packet.ClockMetrics?.RecordMissed();
                    return false;
                }

                var remaining = packet.DeadlineTicks - Stopwatch.GetTimestamp();
                if (remaining <= 0) return true;
                if (remaining > Stopwatch.Frequency / 500d)
                {
                    var milliseconds = (int)(remaining * 1000d / Stopwatch.Frequency) - 1;
                    wake.WaitOne(Math.Max(1, milliseconds));
                }
                else Thread.SpinWait(32);
            }

            packet.ClockMetrics?.RecordMissed();
            return false;
        }

        private void TryOpenOutput()
        {
            RtMidi.MidiOut candidate = null;
            try
            {
                candidate = RtMidi.MidiOut.Create();
                if (Ordinal >= candidate.PortCount)
                {
                    Volatile.Write(ref status, "Waiting for MIDI output port");
                    candidate.Dispose();
                    return;
                }
                var currentName = candidate.GetPortName(Ordinal);
                if (!string.Equals(currentName, expectedPortName, StringComparison.Ordinal))
                {
                    Volatile.Write(ref status, "MIDI output ordinal changed; waiting for the configured port");
                    candidate.Dispose();
                    return;
                }
                candidate.OpenPort(Ordinal, "Mondmade VJ0 " + clientName);
                output = candidate;
                candidate = null;
                lock (queueLock) queue.DiscardRealtime();
                Interlocked.Increment(ref connectionGeneration);
                Volatile.Write(ref connected, 1);
                Volatile.Write(ref status, "Connected");
            }
            catch (Exception exception)
            {
                candidate?.Dispose();
                lock (queueLock) lastError = exception.Message;
                Volatile.Write(ref status, "Disconnected");
            }
        }

        private void MarkDisconnected(MidiOutputPacket? retryPacket, Exception exception)
        {
            Volatile.Write(ref connected, 0);
            Volatile.Write(ref status, "Disconnected");
            Interlocked.Increment(ref connectionGeneration);
            CloseOutput();
            lock (queueLock)
            {
                if (exception != null) lastError = exception.Message;
                queue.DiscardRealtime();
                if (retryPacket.HasValue)
                {
                    if (retryPacket.Value.Kind == MidiOutputPacketKind.Led) queue.RequeueLedUnlessNewer(retryPacket.Value);
                    else if (retryPacket.Value.Kind == MidiOutputPacketKind.Control) queue.RecordDroppedMessage();
                }
            }
        }

        private void CloseOutput()
        {
            var current = output;
            output = null;
            try { current?.Dispose(); }
            catch { }
        }

        public void Dispose()
        {
            stopping = true;
            wake.Set();
            if (worker != Thread.CurrentThread && worker.Join(2000)) wake.Dispose();
        }
    }

    internal enum MidiOutputPacketKind { Control, Clock, Led }

    internal readonly struct MidiOutputPacket
    {
        public readonly MidiOutputPacketKind Kind;
        public readonly byte[] Message;
        public readonly string Key;
        public readonly long DeadlineTicks;
        public readonly long PeriodTicks;
        public readonly int Generation;
        public readonly MidiClockSendMetrics ClockMetrics;

        public MidiOutputPacket(MidiOutputPacketKind kind, byte[] message, string key = null,
            long deadlineTicks = 0, long periodTicks = 0, int generation = 0, MidiClockSendMetrics clockMetrics = null)
        {
            Kind = kind;
            Message = message;
            Key = key;
            DeadlineTicks = deadlineTicks;
            PeriodTicks = periodTicks;
            Generation = generation;
            ClockMetrics = clockMetrics;
        }
    }

    internal sealed class MidiOutputWorkQueue
    {
        private const int ControlCapacity = 256;
        private const int LedCapacity = 1024;
        private static readonly byte[] ClockPulseMessage = { 0xF8 };
        private readonly Queue<MidiOutputPacket> controls = new Queue<MidiOutputPacket>();
        private readonly LinkedList<string> ledOrder = new LinkedList<string>();
        private readonly Dictionary<string, MidiOutputPacket> leds = new Dictionary<string, MidiOutputPacket>(StringComparer.Ordinal);
        private MidiOutputPacket clock;
        private bool hasClock;
        private int clockGeneration;
        private long droppedMessages;

        public int Count => controls.Count + leds.Count + (hasClock ? 1 : 0);
        public long DroppedMessages => droppedMessages;

        public void SetClockGeneration(int generation)
        {
            if (clockGeneration == generation) return;
            clockGeneration = generation;
            if (hasClock && clock.Generation != generation) DiscardClock();
        }

        public bool EnqueueControl(byte[] message)
        {
            if (controls.Count >= ControlCapacity) { droppedMessages++; return false; }
            controls.Enqueue(new MidiOutputPacket(MidiOutputPacketKind.Control, (byte[])message.Clone()));
            return true;
        }

        public void RecordDroppedMessage() => droppedMessages++;

        public bool EnqueueLed(string key, byte[] message)
        {
            if (!leds.ContainsKey(key))
            {
                if (leds.Count >= LedCapacity) { droppedMessages++; return false; }
                ledOrder.AddLast(key);
            }
            leds[key] = new MidiOutputPacket(MidiOutputPacketKind.Led, (byte[])message.Clone(), key);
            return true;
        }

        public bool EnqueueLed(string key, byte status, byte number, byte value)
        {
            if (!leds.ContainsKey(key))
            {
                if (leds.Count >= LedCapacity) { droppedMessages++; return false; }
                ledOrder.AddLast(key);
            }
            leds[key] = new MidiOutputPacket(MidiOutputPacketKind.Led, new[] { status, number, value }, key);
            return true;
        }

        public bool EnqueueClock(long deadlineTicks, long periodTicks, int generation, MidiClockSendMetrics metrics)
        {
            if (periodTicks <= 0 || generation != clockGeneration) { droppedMessages++; return false; }
            if (hasClock) clock.ClockMetrics?.RecordMissed();
            clock = new MidiOutputPacket(MidiOutputPacketKind.Clock, ClockPulseMessage,
                deadlineTicks: deadlineTicks, periodTicks: periodTicks, generation: generation, clockMetrics: metrics);
            hasClock = true;
            return true;
        }

        public bool TryDequeue(out MidiOutputPacket packet)
        {
            if (controls.Count > 0) { packet = controls.Dequeue(); return true; }
            if (hasClock) { packet = clock; clock = default; hasClock = false; return true; }
            while (ledOrder.First != null)
            {
                var key = ledOrder.First.Value;
                ledOrder.RemoveFirst();
                if (!leds.TryGetValue(key, out packet)) continue;
                leds.Remove(key);
                return true;
            }
            packet = default;
            return false;
        }

        public void DiscardClock()
        {
            if (hasClock) clock.ClockMetrics?.RecordMissed();
            clock = default;
            hasClock = false;
        }

        public void DiscardRealtime()
        {
            droppedMessages += controls.Count;
            controls.Clear();
            DiscardClock();
        }

        public void RequeueLedUnlessNewer(MidiOutputPacket packet)
        {
            if (packet.Kind != MidiOutputPacketKind.Led || leds.ContainsKey(packet.Key)) return;
            if (leds.Count >= LedCapacity) { droppedMessages++; return; }
            ledOrder.AddFirst(packet.Key);
            leds.Add(packet.Key, packet);
        }
    }

    internal sealed class MidiClockSendMetrics
    {
        private const int MaximumHistogramMicroseconds = 10000;
        private readonly long[] latenessSamples = new long[131072];
        private readonly long[] latenessHistogram = new long[MaximumHistogramMicroseconds + 2];
        private long sampleCount;
        private long sentPulses;
        private long missedPulses;
        private long maximumLatenessMicroseconds;
        private long maximumDeadlineDriftTicks;
        private long nextP99CalculationTicks;
        private long cachedP99Microseconds;
        private int baselineGeneration = int.MinValue;
        private long baselineDeadline;
        private long baselineActual;

        public long SentPulses => Interlocked.Read(ref sentPulses);
        public long MissedPulses => Interlocked.Read(ref missedPulses);
        public double MaximumLatenessMilliseconds => Interlocked.Read(ref maximumLatenessMicroseconds) / 1000d;
        public double MaximumDeadlineDriftMilliseconds => Interlocked.Read(ref maximumDeadlineDriftTicks) * 1000d / Stopwatch.Frequency;
        public double P99LatenessMilliseconds
        {
            get
            {
                var now = Stopwatch.GetTimestamp();
                var next = Interlocked.Read(ref nextP99CalculationTicks);
                if (now >= next && Interlocked.CompareExchange(ref nextP99CalculationTicks, now + Stopwatch.Frequency, next) == next)
                    Interlocked.Exchange(ref cachedP99Microseconds, CalculateP99Microseconds());
                return Interlocked.Read(ref cachedP99Microseconds) / 1000d;
            }
        }

        public void RecordMissed() => Interlocked.Increment(ref missedPulses);
        public void RecordMissed(long count)
        {
            if (count > 0) Interlocked.Add(ref missedPulses, count);
        }

        public void RecordSent(long deadline, long actual, int generation)
        {
            Interlocked.Increment(ref sentPulses);
            var late = Math.Max(0L, actual - deadline);
            var latenessMicroseconds = (long)Math.Ceiling(late * 1_000_000d / Stopwatch.Frequency);
            UpdateMaximum(ref maximumLatenessMicroseconds, latenessMicroseconds);
                var sampleIndex = Interlocked.Read(ref sampleCount);
            var histogramIndex = (int)Math.Min(latenessMicroseconds, MaximumHistogramMicroseconds + 1);
            var sampleSlot = (int)(sampleIndex % latenessSamples.Length);
            if (sampleIndex >= latenessSamples.Length)
            {
                var oldIndex = (int)Math.Min(Interlocked.Exchange(ref latenessSamples[sampleSlot], latenessMicroseconds), MaximumHistogramMicroseconds + 1);
                Interlocked.Decrement(ref latenessHistogram[oldIndex]);
            }
            else Interlocked.Exchange(ref latenessSamples[sampleSlot], latenessMicroseconds);
            Interlocked.Increment(ref latenessHistogram[histogramIndex]);
                Interlocked.Increment(ref sampleCount);

            if (baselineGeneration != generation)
            {
                baselineGeneration = generation;
                baselineDeadline = deadline;
                baselineActual = actual;
                return;
            }
            var drift = Math.Abs((actual - baselineActual) - (deadline - baselineDeadline));
            UpdateMaximum(ref maximumDeadlineDriftTicks, drift);
        }

        private long CalculateP99Microseconds()
        {
            var total = Math.Min(Interlocked.Read(ref sampleCount), latenessSamples.Length);
            if (total <= 0) return 0;
            var target = (long)Math.Ceiling(total * 0.99d);
            long accumulated = 0;
            for (var i = 0; i < latenessHistogram.Length; i++)
            {
                accumulated += Interlocked.Read(ref latenessHistogram[i]);
                if (accumulated >= target) return i;
            }
            return MaximumHistogramMicroseconds + 1;
        }

        private static void UpdateMaximum(ref long target, long value)
        {
            long current;
            while (value > (current = Interlocked.Read(ref target)) && Interlocked.CompareExchange(ref target, value, current) != current) { }
        }
    }
}
