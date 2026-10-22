using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
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

    internal static class MidiThreadScheduling
    {
#if UNITY_EDITOR_OSX || (!UNITY_EDITOR && UNITY_STANDALONE_OSX)
        private const uint QosClassUserInteractive = 0x21;

        [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "pthread_set_qos_class_self_np", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SetCurrentThreadQos(uint qosClass, int relativePriority);
#elif UNITY_EDITOR_WIN || (!UNITY_EDITOR && UNITY_STANDALONE_WIN)
        private const int AvrtPriorityCritical = 2;

        [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr AvSetMmThreadCharacteristics(string taskName, ref uint taskIndex);

        [DllImport("avrt.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AvSetMmThreadPriority(IntPtr avrtHandle, int priority);

        [DllImport("avrt.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AvRevertMmThreadCharacteristics(IntPtr avrtHandle);
#endif

        public static IDisposable RequestRealtimeScheduling()
        {
#if UNITY_EDITOR_OSX || (!UNITY_EDITOR && UNITY_STANDALONE_OSX)
            try
            {
                SetCurrentThreadQos(QosClassUserInteractive, 0);
            }
            catch { }
#elif UNITY_EDITOR_WIN || (!UNITY_EDITOR && UNITY_STANDALONE_WIN)
            try
            {
                uint taskIndex = 0;
                var handle = AvSetMmThreadCharacteristics("Pro Audio", ref taskIndex);
                if (handle != IntPtr.Zero)
                {
                    AvSetMmThreadPriority(handle, AvrtPriorityCritical);
                    return new AvrtThreadLease(handle);
                }
            }
            catch { }
#endif
            return EmptyLease.Instance;
        }

#if UNITY_EDITOR_WIN || (!UNITY_EDITOR && UNITY_STANDALONE_WIN)
        private sealed class AvrtThreadLease : IDisposable
        {
            private IntPtr handle;

            public AvrtThreadLease(IntPtr handle) => this.handle = handle;

            public void Dispose()
            {
                var current = Interlocked.Exchange(ref handle, IntPtr.Zero);
                if (current == IntPtr.Zero) return;
                try { AvRevertMmThreadCharacteristics(current); }
                catch { }
            }
        }
#endif

        private sealed class EmptyLease : IDisposable
        {
            public static readonly EmptyLease Instance = new EmptyLease();
            public void Dispose() { }
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
        public long SentLedMessages => port == null ? 0 : port.SentLedMessages;
        public long SentLedControls => port == null ? 0 : port.SentLedControls;
        public double MaximumLedSendDurationMilliseconds => port == null ? 0d : port.MaximumLedSendDurationMilliseconds;
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
        public bool QueueApcPadLed(string key, byte pad, byte red, byte green, byte blue)
            => port != null && port.QueueApcPadLed(key, pad, red, green, blue);
        public bool QueueApcPadLeds(IReadOnlyList<ApcPadLedUpdate> updates)
            => port != null && port.QueueApcPadLeds(updates);
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
        private const int ClockSpinWaitLeadMilliseconds = 3;
        private const int ClockLedGuardMilliseconds = 8;
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
        private long sentLedMessages;
        private long sentLedControls;
        private long maximumLedSendDurationTicks;
        private RtMidi.MidiOut output;
        private int clockOwner;
        private int clockScheduleGeneration;
        private long connectionGeneration;
        private int inFlight;

        public int Ordinal { get; }
        public bool IsConnected => Volatile.Read(ref connected) != 0;
        public string Status => Volatile.Read(ref status);
        public long ConnectionGeneration => Interlocked.Read(ref connectionGeneration);
        public long SentLedMessages => Interlocked.Read(ref sentLedMessages);
        public long SentLedControls => Interlocked.Read(ref sentLedControls);
        public double MaximumLedSendDurationMilliseconds
            => Interlocked.Read(ref maximumLedSendDurationTicks) * 1000d / Stopwatch.Frequency;
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

        public bool QueueApcPadLed(string key, byte pad, byte red, byte green, byte blue)
            => QueueApcPadLeds(new[] { new ApcPadLedUpdate(key, pad, red, green, blue) });

        public bool QueueApcPadLeds(IReadOnlyList<ApcPadLedUpdate> updates)
        {
            if (updates == null || updates.Count == 0) return updates != null;
            lock (queueLock)
            {
                if (stopping) return false;
                var accepted = queue.EnqueueApcPadLeds(updates);
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
            using (MidiThreadScheduling.RequestRealtimeScheduling()) WorkerLoopCore();
        }

        private void WorkerLoopCore()
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
                int waitMilliseconds;
                lock (queueLock)
                {
                    var nowTicks = Stopwatch.GetTimestamp();
                    var clockSpinLeadTicks = Stopwatch.Frequency * ClockSpinWaitLeadMilliseconds / 1000d;
                    var clockLedGuardTicks = Stopwatch.Frequency * ClockLedGuardMilliseconds / 1000d;
                    hasPacket = queue.TryDequeue(nowTicks, clockSpinLeadTicks, out packet, clockLedGuardTicks);
                    waitMilliseconds = queue.GetClockPollWaitMilliseconds(nowTicks, clockSpinLeadTicks);
                }
                if (!hasPacket)
                {
                    if (stopping) break;
                    wake.WaitOne(waitMilliseconds);
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
                        else
                        {
                            var sendStartTicks = packet.Kind == MidiOutputPacketKind.Led ? Stopwatch.GetTimestamp() : 0L;
                            output.SendMessage(packet.Message);
                            if (packet.Kind == MidiOutputPacketKind.Led)
                            {
                                UpdateMaximum(ref maximumLedSendDurationTicks, Stopwatch.GetTimestamp() - sendStartTicks);
                                Interlocked.Increment(ref sentLedMessages);
                                Interlocked.Add(ref sentLedControls, packet.LedControlCount);
                            }
                        }
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
                    if (Stopwatch.GetTimestamp() >= packet.DeadlineTicks) packet.ClockMetrics?.RecordMissed();
                    return false;
                }

                var remaining = packet.DeadlineTicks - Stopwatch.GetTimestamp();
                if (remaining <= 0) return true;
                if (remaining > Stopwatch.Frequency * ClockSpinWaitLeadMilliseconds / 1000d)
                {
                    // Recheck each millisecond so a long OS wait cannot carry the worker past the deadline.
                    wake.WaitOne(1);
                }
                else Thread.SpinWait(32);
            }

            if (Stopwatch.GetTimestamp() >= packet.DeadlineTicks) packet.ClockMetrics?.RecordMissed();
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

        private static void UpdateMaximum(ref long target, long value)
        {
            long current;
            while (value > (current = Interlocked.Read(ref target)) && Interlocked.CompareExchange(ref target, value, current) != current) { }
        }

        public void Dispose()
        {
            stopping = true;
            wake.Set();
            if (worker != Thread.CurrentThread && worker.Join(2000)) wake.Dispose();
        }
    }

    internal enum MidiOutputPacketKind { Control, Clock, Led, ApcPadLed }

    internal readonly struct ApcPadLedUpdate
    {
        public readonly string Key;
        public readonly byte Pad, Red, Green, Blue;

        public ApcPadLedUpdate(string key, byte pad, byte red, byte green, byte blue)
        {
            Key = key;
            Pad = pad;
            Red = red;
            Green = green;
            Blue = blue;
        }
    }

    internal readonly struct MidiOutputPacket
    {
        public readonly MidiOutputPacketKind Kind;
        public readonly byte[] Message;
        public readonly string Key;
        public readonly long DeadlineTicks;
        public readonly long PeriodTicks;
        public readonly int Generation;
        public readonly MidiClockSendMetrics ClockMetrics;
        public readonly int LedControlCount;
        public readonly ApcPadLedUpdate[] ApcPadLedUpdates;

        public MidiOutputPacket(MidiOutputPacketKind kind, byte[] message, string key = null,
            long deadlineTicks = 0, long periodTicks = 0, int generation = 0, MidiClockSendMetrics clockMetrics = null,
            int ledControlCount = 1, ApcPadLedUpdate[] apcPadLedUpdates = null)
        {
            Kind = kind;
            Message = message;
            Key = key;
            DeadlineTicks = deadlineTicks;
            PeriodTicks = periodTicks;
            Generation = generation;
            ClockMetrics = clockMetrics;
            LedControlCount = ledControlCount;
            ApcPadLedUpdates = apcPadLedUpdates;
        }
    }

    internal sealed class MidiOutputWorkQueue
    {
        private const int ControlCapacity = 256;
        private const int LedCapacity = 1024;
        private const int ClockCapacity = 8;
        private const int ApcPadSysExCapacity = 32;
        private static readonly byte[] ClockPulseMessage = { 0xF8 };
        private readonly Queue<MidiOutputPacket> controls = new Queue<MidiOutputPacket>();
        private readonly Queue<MidiOutputPacket> clocks = new Queue<MidiOutputPacket>();
        private readonly LinkedList<string> ledOrder = new LinkedList<string>();
        private readonly Dictionary<string, LinkedListNode<string>> ledNodes = new Dictionary<string, LinkedListNode<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, MidiOutputPacket> leds = new Dictionary<string, MidiOutputPacket>(StringComparer.Ordinal);
        private int clockGeneration;
        private long droppedMessages;

        public int Count => controls.Count + leds.Count + clocks.Count;
        public long DroppedMessages => droppedMessages;

        public void SetClockGeneration(int generation)
        {
            if (clockGeneration == generation) return;
            clockGeneration = generation;
            if (clocks.Count == 0) return;

            var now = Stopwatch.GetTimestamp();
            var retained = new Queue<MidiOutputPacket>(clocks.Count);
            while (clocks.Count > 0)
            {
                var packet = clocks.Dequeue();
                if (packet.Generation == generation) retained.Enqueue(packet);
                else if (packet.DeadlineTicks <= now) packet.ClockMetrics?.RecordMissed();
            }
            while (retained.Count > 0) clocks.Enqueue(retained.Dequeue());
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
                ledNodes.Add(key, ledOrder.AddLast(key));
            }
            leds[key] = new MidiOutputPacket(MidiOutputPacketKind.Led, (byte[])message.Clone(), key, ledControlCount: 1);
            return true;
        }

        public bool EnqueueLed(string key, byte status, byte number, byte value)
        {
            if (!leds.ContainsKey(key))
            {
                if (leds.Count >= LedCapacity) { droppedMessages++; return false; }
                ledNodes.Add(key, ledOrder.AddLast(key));
            }
            leds[key] = new MidiOutputPacket(MidiOutputPacketKind.Led, new[] { status, number, value }, key, ledControlCount: 1);
            return true;
        }

        public bool EnqueueApcPadLed(string key, byte pad, byte red, byte green, byte blue)
        {
            return EnqueueApcPadLeds(new[] { new ApcPadLedUpdate(key, pad, red, green, blue) });
        }

        public bool EnqueueApcPadLeds(IReadOnlyList<ApcPadLedUpdate> updates)
        {
            if (updates == null) return false;
            if (updates.Count == 0) return true;
            var newKeys = 0;
            for (var i = 0; i < updates.Count; i++)
            {
                var update = updates[i];
                if (string.IsNullOrEmpty(update.Key) || update.Pad > 0x3F) return false;
                if (leds.ContainsKey(update.Key)) continue;
                var duplicate = false;
                for (var j = 0; j < i; j++)
                    if (string.Equals(updates[j].Key, update.Key, StringComparison.Ordinal)) { duplicate = true; break; }
                if (!duplicate) newKeys++;
            }
            if (leds.Count + newKeys > LedCapacity) { droppedMessages += updates.Count; return false; }
            for (var i = 0; i < updates.Count; i++)
            {
                var update = updates[i];
                if (!leds.ContainsKey(update.Key)) ledNodes.Add(update.Key, ledOrder.AddLast(update.Key));
                // The four-byte form is private to the queue and converted to Akai bulk SysEx on dequeue.
                leds[update.Key] = new MidiOutputPacket(MidiOutputPacketKind.ApcPadLed,
                    new[] { update.Pad, update.Red, update.Green, update.Blue }, update.Key, ledControlCount: 1);
            }
            return true;
        }

        public bool EnqueueClock(long deadlineTicks, long periodTicks, int generation, MidiClockSendMetrics metrics)
        {
            if (periodTicks <= 0 || generation != clockGeneration) { droppedMessages++; return false; }
            if (clocks.Count >= ClockCapacity) { droppedMessages++; return false; }
            var packet = new MidiOutputPacket(MidiOutputPacketKind.Clock, ClockPulseMessage,
                deadlineTicks: deadlineTicks, periodTicks: periodTicks, generation: generation, clockMetrics: metrics);
            if (clocks.Count == 0 || deadlineTicks >= GetLastClockDeadline()) clocks.Enqueue(packet);
            else
            {
                droppedMessages++;
                return false;
            }
            return true;
        }

        public bool TryDequeue(long nowTicks, double clockSpinLeadTicks, out MidiOutputPacket packet, double clockLedGuardTicks = 0d)
        {
            if (controls.Count > 0) { packet = controls.Dequeue(); return true; }
            if (clocks.Count > 0 && clocks.Peek().DeadlineTicks - nowTicks <= clockSpinLeadTicks)
            {
                packet = clocks.Dequeue();
                return true;
            }
            if (clocks.Count > 0 && clocks.Peek().DeadlineTicks - nowTicks <= clockLedGuardTicks)
            {
                packet = default;
                return false;
            }
            while (ledOrder.First != null)
            {
                if (!leds.TryGetValue(ledOrder.First.Value, out var first))
                {
                    RemoveFirstLed();
                    continue;
                }

                if (first.Kind == MidiOutputPacketKind.ApcPadLed)
                {
                    packet = DequeueApcPadBatch();
                    return true;
                }

                packet = first;
                RemoveLed(first.Key);
                return true;
            }
            packet = default;
            return false;
        }

        public int GetClockPollWaitMilliseconds(long nowTicks, double clockSpinLeadTicks)
        {
            if (clocks.Count == 0) return 100;
            var untilSpinWindowTicks = clocks.Peek().DeadlineTicks - nowTicks - clockSpinLeadTicks;
            if (untilSpinWindowTicks <= Stopwatch.Frequency / 1000d) return 1;

            // Wake just before the final spin window so the output worker does not poll at 1 kHz for the full lookahead.
            var milliseconds = (int)(untilSpinWindowTicks * 1000d / Stopwatch.Frequency);
            return Math.Max(1, milliseconds - 1);
        }

        private long GetLastClockDeadline()
        {
            var lastDeadline = 0L;
            foreach (var packet in clocks) lastDeadline = packet.DeadlineTicks;
            return lastDeadline;
        }

        private void DiscardClockQueue(bool countAsMissed)
        {
            while (clocks.Count > 0)
            {
                var packet = clocks.Dequeue();
                if (countAsMissed) packet.ClockMetrics?.RecordMissed();
            }
        }

        public void DiscardRealtime()
        {
            droppedMessages += controls.Count;
            controls.Clear();
            DiscardClockQueue(countAsMissed: true);
        }

        public void RequeueLedUnlessNewer(MidiOutputPacket packet)
        {
            if (packet.Kind != MidiOutputPacketKind.Led) return;
            if (packet.ApcPadLedUpdates != null)
            {
                for (var i = packet.ApcPadLedUpdates.Length - 1; i >= 0; i--)
                    RequeueApcPadLedUnlessNewer(packet.ApcPadLedUpdates[i]);
                return;
            }
            if (string.IsNullOrEmpty(packet.Key) || leds.ContainsKey(packet.Key)) return;
            if (leds.Count >= LedCapacity) { droppedMessages++; return; }
            ledNodes.Add(packet.Key, ledOrder.AddFirst(packet.Key));
            leds.Add(packet.Key, packet);
        }

        private void RequeueApcPadLedUnlessNewer(ApcPadLedUpdate update)
        {
            if (leds.ContainsKey(update.Key)) return;
            if (leds.Count >= LedCapacity) { droppedMessages++; return; }
            var node = ledOrder.AddFirst(update.Key);
            ledNodes.Add(update.Key, node);
            leds.Add(update.Key, new MidiOutputPacket(MidiOutputPacketKind.ApcPadLed,
                new[] { update.Pad, update.Red, update.Green, update.Blue }, update.Key, ledControlCount: 1));
        }

        private MidiOutputPacket DequeueApcPadBatch()
        {
            var message = new List<byte>(7 + ApcPadSysExCapacity * 8 + 1)
            {
                0xF0, 0x47, 0x7F, 0x4F, 0x24
            };
            var dataLengthOffset = message.Count;
            message.Add(0);
            message.Add(0);
            var updates = new List<ApcPadLedUpdate>(ApcPadSysExCapacity);
            var count = 0;
            var node = ledOrder.First;
            while (node != null && count < ApcPadSysExCapacity)
            {
                var next = node.Next;
                if (leds.TryGetValue(node.Value, out var candidate) && candidate.Kind == MidiOutputPacketKind.ApcPadLed)
                {
                    var data = candidate.Message;
                    message.Add(data[0]);
                    message.Add(data[0]);
                    updates.Add(new ApcPadLedUpdate(candidate.Key, data[0], data[1], data[2], data[3]));
                    AppendSevenBitPair(message, data[1]);
                    AppendSevenBitPair(message, data[2]);
                    AppendSevenBitPair(message, data[3]);
                    RemoveLed(candidate.Key);
                    count++;
                }
                node = next;
            }

            var dataLength = count * 8;
            message[dataLengthOffset] = (byte)((dataLength >> 7) & 0x7F);
            message[dataLengthOffset + 1] = (byte)(dataLength & 0x7F);
            message.Add(0xF7);
            return new MidiOutputPacket(MidiOutputPacketKind.Led, message.ToArray(), ledControlCount: count,
                apcPadLedUpdates: updates.ToArray());
        }

        private static void AppendSevenBitPair(List<byte> message, byte value)
        {
            message.Add((byte)((value >> 7) & 0x7F));
            message.Add((byte)(value & 0x7F));
        }

        private void RemoveFirstLed() => RemoveLed(ledOrder.First.Value);

        private void RemoveLed(string key)
        {
            if (ledNodes.TryGetValue(key, out var node))
            {
                ledOrder.Remove(node);
                ledNodes.Remove(key);
            }
            leds.Remove(key);
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
