using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MondmadeVJ0
{
#if UNITY_EDITOR_OSX || (!UNITY_EDITOR && UNITY_STANDALONE_OSX)
    /// <summary>
    /// Sends MIDI Clock through CoreMIDI with an absolute host-time timestamp.
    /// Ordinary controls and LED feedback continue to use the shared RtMidi output worker.
    /// </summary>
    internal sealed class CoreMidiScheduledClockOutput : IDisposable
    {
        private const string CoreMidiFramework = "/System/Library/Frameworks/CoreMIDI.framework/CoreMIDI";
        private const string CoreFoundationFramework = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const string LibSystem = "/usr/lib/libSystem.B.dylib";
        private const uint Utf8Encoding = 0x08000100;
        private const int MaximumMidiMessageLength = 256;
        private const int PacketListBufferSize = 1024;
        private const double DefaultScheduleLeadMilliseconds = 8d;
        private const double MaximumScheduleLeadMilliseconds = 8d;

        private readonly uint client;
        private readonly uint port;
        private readonly uint destination;
        private readonly uint timebaseNumerator;
        private readonly uint timebaseDenominator;
        private readonly IntPtr packetListBuffer;
        private readonly IntPtr packetDataBuffer;
        private readonly Queue<long> pendingDeadlines = new Queue<long>(2);
        private bool disposed;

        public double ScheduleLeadMilliseconds { get; }
        public long ScheduleLeadTicks => (long)Math.Ceiling(ScheduleLeadMilliseconds * Stopwatch.Frequency / 1000d);
        public string DestinationName { get; }

        private CoreMidiScheduledClockOutput(uint client, uint port, uint destination, string destinationName,
            uint timebaseNumerator, uint timebaseDenominator, double scheduleLeadMilliseconds,
            IntPtr packetListBuffer, IntPtr packetDataBuffer)
        {
            this.client = client;
            this.port = port;
            this.destination = destination;
            DestinationName = destinationName;
            this.timebaseNumerator = timebaseNumerator;
            this.timebaseDenominator = timebaseDenominator;
            ScheduleLeadMilliseconds = scheduleLeadMilliseconds;
            this.packetListBuffer = packetListBuffer;
            this.packetDataBuffer = packetDataBuffer;
        }

        public static CoreMidiScheduledClockOutput TryCreate(int ordinal, string expectedPortName, out string error)
        {
            error = null;
            uint destination = 0;
            uint client = 0;
            uint port = 0;
            IntPtr packetListBuffer = IntPtr.Zero;
            IntPtr packetDataBuffer = IntPtr.Zero;
            string destinationName = null;
            try
            {
                if (!TryResolveDestination(ordinal, expectedPortName, out destination, out destinationName))
                {
                    error = $"CoreMIDI could not resolve RtMidi port {ordinal} named '{expectedPortName}'. Available CoreMIDI destinations: {string.Join(" | ", GetDestinationNames())}.";
                    return null;
                }

                var timebase = GetMachTimebase();
                if (timebase.Numerator == 0 || timebase.Denominator == 0)
                {
                    error = "mach_timebase_info returned an invalid timebase.";
                    return null;
                }

                var clientName = CreateCfString("Mondmade VJ0 MIDI Clock");
                try
                {
                    var status = MidiClientCreate(clientName, IntPtr.Zero, IntPtr.Zero, out client);
                    if (status != 0) throw new InvalidOperationException($"MIDIClientCreate failed with OSStatus {status}.");
                }
                finally { CfRelease(clientName); }

                var outputName = CreateCfString("Mondmade VJ0 Scheduled Clock");
                try
                {
                    var status = MidiOutputPortCreate(client, outputName, out port);
                    if (status != 0) throw new InvalidOperationException($"MIDIOutputPortCreate failed with OSStatus {status}.");
                }
                finally { CfRelease(outputName); }

                var scheduleLeadMilliseconds = GetScheduleLeadMilliseconds(destination);
                if (scheduleLeadMilliseconds > MaximumScheduleLeadMilliseconds)
                    throw new InvalidOperationException($"CoreMIDI destination requires {scheduleLeadMilliseconds:F3} ms advance scheduling; the 8 ms single-pulse window cannot safely support it.");

                // Clock packets are sent only by the shared output worker. Keep its packet
                // storage alive for the connection to avoid native allocator jitter per pulse.
                packetListBuffer = Marshal.AllocHGlobal(PacketListBufferSize);
                packetDataBuffer = Marshal.AllocHGlobal(MaximumMidiMessageLength);
                return new CoreMidiScheduledClockOutput(client, port, destination, destinationName,
                    timebase.Numerator, timebase.Denominator, scheduleLeadMilliseconds,
                    packetListBuffer, packetDataBuffer);
            }
            catch (Exception exception)
            {
                if (packetDataBuffer != IntPtr.Zero) Marshal.FreeHGlobal(packetDataBuffer);
                if (packetListBuffer != IntPtr.Zero) Marshal.FreeHGlobal(packetListBuffer);
                if (port != 0) { try { MidiPortDispose(port); } catch { } }
                if (client != 0) { try { MidiClientDispose(client); } catch { } }
                error = exception.Message;
                return null;
            }
        }

        public void SendAt(byte[] message, long deadlineStopwatchTicks)
        {
            if (disposed) throw new ObjectDisposedException(nameof(CoreMidiScheduledClockOutput));
            if (message == null || message.Length == 0 || message.Length > MaximumMidiMessageLength)
                throw new ArgumentOutOfRangeException(nameof(message), "CoreMIDI MIDI packets must contain 1 to 256 bytes.");

            var nowTicks = Stopwatch.GetTimestamp();
            DiscardElapsedPackets(nowTicks);
            var hostTimestamp = ConvertDeadlineToHostTime(deadlineStopwatchTicks, nowTicks,
                MachAbsoluteTime(), timebaseNumerator, timebaseDenominator, Stopwatch.Frequency);
            Marshal.Copy(message, 0, packetDataBuffer, message.Length);
            var firstPacket = MidiPacketListInit(packetListBuffer);
            if (firstPacket == IntPtr.Zero)
                throw new InvalidOperationException("MIDIPacketListInit returned null.");

            var lastPacket = MidiPacketListAdd(packetListBuffer, PacketListBufferSize, firstPacket,
                hostTimestamp, (ushort)message.Length, packetDataBuffer);
            if (lastPacket == IntPtr.Zero)
                throw new InvalidOperationException("MIDIPacketListAdd could not append the MIDI Clock packet.");

            var status = MidiSend(port, destination, packetListBuffer);
            if (status != 0) throw new InvalidOperationException($"MIDISend failed with OSStatus {status}.");
            pendingDeadlines.Enqueue(deadlineStopwatchTicks);
        }

        public void WaitForPendingPackets()
        {
            if (disposed) return;
            while (pendingDeadlines.Count > 0)
            {
                var remainingTicks = pendingDeadlines.Peek() - Stopwatch.GetTimestamp();
                if (remainingTicks <= 0)
                {
                    pendingDeadlines.Dequeue();
                    continue;
                }

                var oneMillisecond = Math.Max(1L, Stopwatch.Frequency / 1000L);
                if (remainingTicks > oneMillisecond)
                {
                    var sleepTicks = remainingTicks - oneMillisecond / 2;
                    var sleepMilliseconds = (int)Math.Max(1L, sleepTicks * 1000L / Stopwatch.Frequency);
                    Thread.Sleep(sleepMilliseconds);
                    continue;
                }

                Thread.SpinWait(32);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            try { WaitForPendingPackets(); }
            catch { }
            disposed = true;
            try { MidiPortDispose(port); }
            catch { }
            try { MidiClientDispose(client); }
            catch { }
            Marshal.FreeHGlobal(packetDataBuffer);
            Marshal.FreeHGlobal(packetListBuffer);
        }

        internal static ulong ConvertDeadlineToHostTime(long deadlineTicks, long nowTicks, ulong nowHostTime,
            uint timebaseNumerator, uint timebaseDenominator, long stopwatchFrequency)
        {
            if (deadlineTicks <= nowTicks || timebaseNumerator == 0 || timebaseDenominator == 0 || stopwatchFrequency <= 0)
                return 0UL;

            var deltaTicks = deadlineTicks - nowTicks;
            var deltaNanoseconds = deltaTicks * (1_000_000_000d / stopwatchFrequency);
            var deltaHostTime = deltaNanoseconds * timebaseDenominator / timebaseNumerator;
            if (double.IsNaN(deltaHostTime) || double.IsInfinity(deltaHostTime) || deltaHostTime <= 0d)
                return nowHostTime;
            if (deltaHostTime >= ulong.MaxValue - nowHostTime) return ulong.MaxValue;
            return nowHostTime + (ulong)Math.Round(deltaHostTime);
        }

        internal static string[] GetDestinationNames()
        {
            var count = MidiGetNumberOfDestinations();
            var names = new string[(int)count];
            for (uint i = 0; i < count; i++)
                names[(int)i] = $"{i}: {GetObjectName(MidiGetDestination(i))}";
            return names;
        }

        private static bool TryResolveDestination(int ordinal, string expectedPortName, out uint destination, out string destinationName)
        {
            destination = 0;
            destinationName = null;
            if (ordinal < 0 || string.IsNullOrEmpty(expectedPortName)) return false;

            var count = MidiGetNumberOfDestinations();
            var destinations = new uint[(int)count];
            var names = new string[(int)count];
            for (uint i = 0; i < count; i++)
            {
                destinations[(int)i] = MidiGetDestination(i);
                names[(int)i] = destinations[(int)i] == 0 ? string.Empty : GetObjectName(destinations[(int)i]);
            }

            var resolvedIndex = ResolveUniqueDestinationIndex(expectedPortName, names);
            if (resolvedIndex < 0) return false;
            destination = destinations[resolvedIndex];
            destinationName = names[resolvedIndex];
            return destination != 0;
        }

        internal static int ResolveUniqueDestinationIndex(string expectedPortName, IReadOnlyList<string> candidateNames)
        {
            if (string.IsNullOrEmpty(expectedPortName) || candidateNames == null) return -1;

            var exactIndex = -1;
            var exactCount = 0;
            var compatibleIndex = -1;
            var compatibleCount = 0;
            for (var i = 0; i < candidateNames.Count; i++)
            {
                var name = candidateNames[i];
                if (string.Equals(expectedPortName, name, StringComparison.Ordinal))
                {
                    exactIndex = i;
                    exactCount++;
                }
                if (!ArePortNamesCompatible(expectedPortName, name)) continue;
                compatibleIndex = i;
                compatibleCount++;
            }

            if (exactCount == 1) return exactIndex;
            if (exactCount > 1) return -1;
            return compatibleCount == 1 ? compatibleIndex : -1;
        }

        internal static bool ArePortNamesCompatible(string rtMidiName, string coreMidiName)
        {
            if (string.IsNullOrEmpty(rtMidiName) || string.IsNullOrEmpty(coreMidiName)) return false;
            if (string.Equals(rtMidiName, coreMidiName, StringComparison.Ordinal)) return true;
            return HasSuffixWithSeparator(rtMidiName, coreMidiName) || HasSuffixWithSeparator(coreMidiName, rtMidiName);
        }

        private static bool HasSuffixWithSeparator(string value, string suffix)
        {
            if (value.Length <= suffix.Length || !value.EndsWith(suffix, StringComparison.Ordinal)) return false;
            var separator = value[value.Length - suffix.Length - 1];
            return char.IsWhiteSpace(separator) || separator == ':' || separator == '-';
        }

        private static string GetObjectName(uint midiObject)
        {
            var property = CreateCfString("name");
            IntPtr value = IntPtr.Zero;
            var buffer = Marshal.AllocHGlobal(1024);
            try
            {
                if (MidiObjectGetStringProperty(midiObject, property, out value) != 0 || value == IntPtr.Zero)
                    return string.Empty;
                if (!CfStringGetCString(value, buffer, 1024, Utf8Encoding)) return string.Empty;
                var bytes = new byte[1024];
                Marshal.Copy(buffer, bytes, 0, bytes.Length);
                var length = Array.IndexOf(bytes, (byte)0);
                if (length < 0) length = bytes.Length;
                return Encoding.UTF8.GetString(bytes, 0, length);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
                if (value != IntPtr.Zero) CfRelease(value);
                CfRelease(property);
            }
        }

        private static double GetScheduleLeadMilliseconds(uint destination)
        {
            var property = CreateCfString("advanceScheduleTimeMuSec");
            try
            {
                if (MidiObjectGetIntegerProperty(destination, property, out var microseconds) == 0 && microseconds > 0)
                    return Math.Max(DefaultScheduleLeadMilliseconds, microseconds / 1000d + 2d);
            }
            finally { CfRelease(property); }
            return DefaultScheduleLeadMilliseconds;
        }

        private void DiscardElapsedPackets(long nowTicks)
        {
            while (pendingDeadlines.Count > 0 && pendingDeadlines.Peek() <= nowTicks)
                pendingDeadlines.Dequeue();
        }

        private static IntPtr CreateCfString(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value + "\0");
            var pointer = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, pointer, bytes.Length);
                var result = CfStringCreateWithCString(IntPtr.Zero, pointer, Utf8Encoding);
                if (result == IntPtr.Zero) throw new InvalidOperationException("CFStringCreateWithCString returned null.");
                return result;
            }
            finally { Marshal.FreeHGlobal(pointer); }
        }

        private static MachTimebase GetMachTimebase()
        {
            if (MachTimebaseInfo(out var info) != 0) return default;
            return new MachTimebase(info.numer, info.denom);
        }

        private readonly struct MachTimebase
        {
            public readonly uint Numerator;
            public readonly uint Denominator;
            public MachTimebase(uint numerator, uint denominator) { Numerator = numerator; Denominator = denominator; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MachTimebaseInfoData
        {
            public uint numer;
            public uint denom;
        }

        [DllImport(CoreMidiFramework, EntryPoint = "MIDIGetNumberOfDestinations", CallingConvention = CallingConvention.Cdecl)]
        private static extern uint MidiGetNumberOfDestinations();
        [DllImport(CoreMidiFramework, EntryPoint = "MIDIGetDestination", CallingConvention = CallingConvention.Cdecl)]
        private static extern uint MidiGetDestination(uint index);
        [DllImport(CoreMidiFramework, EntryPoint = "MIDIObjectGetStringProperty", CallingConvention = CallingConvention.Cdecl)]
        private static extern int MidiObjectGetStringProperty(uint midiObject, IntPtr property, out IntPtr value);
        [DllImport(CoreMidiFramework, EntryPoint = "MIDIObjectGetIntegerProperty", CallingConvention = CallingConvention.Cdecl)]
        private static extern int MidiObjectGetIntegerProperty(uint midiObject, IntPtr property, out int value);
        [DllImport(CoreMidiFramework, EntryPoint = "MIDIClientCreate", CallingConvention = CallingConvention.Cdecl)]
        private static extern int MidiClientCreate(IntPtr name, IntPtr notifyProc, IntPtr refCon, out uint client);
        [DllImport(CoreMidiFramework, EntryPoint = "MIDIOutputPortCreate", CallingConvention = CallingConvention.Cdecl)]
        private static extern int MidiOutputPortCreate(uint client, IntPtr portName, out uint port);
        [DllImport(CoreMidiFramework, EntryPoint = "MIDIPortDispose", CallingConvention = CallingConvention.Cdecl)]
        private static extern int MidiPortDispose(uint port);
        [DllImport(CoreMidiFramework, EntryPoint = "MIDIClientDispose", CallingConvention = CallingConvention.Cdecl)]
        private static extern int MidiClientDispose(uint client);
        [DllImport(CoreMidiFramework, EntryPoint = "MIDIPacketListInit", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr MidiPacketListInit(IntPtr packetList);
        [DllImport(CoreMidiFramework, EntryPoint = "MIDIPacketListAdd", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr MidiPacketListAdd(IntPtr packetList, int packetListSize, IntPtr currentPacket,
            ulong timestamp, ushort dataLength, IntPtr data);
        [DllImport(CoreMidiFramework, EntryPoint = "MIDISend", CallingConvention = CallingConvention.Cdecl)]
        private static extern int MidiSend(uint port, uint destination, IntPtr packetList);
        [DllImport(CoreFoundationFramework, EntryPoint = "CFStringCreateWithCString", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr CfStringCreateWithCString(IntPtr allocator, IntPtr cString, uint encoding);
        [DllImport(CoreFoundationFramework, EntryPoint = "CFStringGetCString", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CfStringGetCString(IntPtr value, IntPtr buffer, int bufferSize, uint encoding);
        [DllImport(CoreFoundationFramework, EntryPoint = "CFRelease", CallingConvention = CallingConvention.Cdecl)]
        private static extern void CfRelease(IntPtr value);
        [DllImport(LibSystem, EntryPoint = "mach_timebase_info", CallingConvention = CallingConvention.Cdecl)]
        private static extern int MachTimebaseInfo(out MachTimebaseInfoData info);
        [DllImport(LibSystem, EntryPoint = "mach_absolute_time", CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong MachAbsoluteTime();
    }
#endif
}
