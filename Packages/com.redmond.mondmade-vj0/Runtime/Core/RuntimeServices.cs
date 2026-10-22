using System;
using System.Collections.Generic;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.Threading;
using R3;

namespace MondmadeVJ0
{
    public sealed class RuntimeServices
    {
        public const int MonitorHistoryCapacity = 4096;
        public const int MonitorRouteSelectionLimit = 32;
        private readonly Dictionary<Type, object> services = new Dictionary<Type, object>();
        private readonly List<RuntimeDiagnostic> diagnostics = new List<RuntimeDiagnostic>(4096);
        private readonly Stopwatch lifetime = Stopwatch.StartNew();
        private readonly Dictionary<string, DiagnosticThrottle> throttles = new Dictionary<string, DiagnosticThrottle>();
        private readonly object monitorLock = new object();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly RuntimeRouteTrace[] routeHistory = new RuntimeRouteTrace[MonitorHistoryCapacity];
        private readonly RuntimeMidiTrace[] midiHistory = new RuntimeMidiTrace[MonitorHistoryCapacity];
        private readonly RuntimeTransportTrace[] transportHistory = new RuntimeTransportTrace[MonitorHistoryCapacity];
#else
        private readonly RuntimeRouteTrace[] routeHistory = Array.Empty<RuntimeRouteTrace>();
        private readonly RuntimeMidiTrace[] midiHistory = Array.Empty<RuntimeMidiTrace>();
        private readonly RuntimeTransportTrace[] transportHistory = Array.Empty<RuntimeTransportTrace>();
#endif
        private readonly Dictionary<string, RouteTraceSelection> routeTraceSelections = new Dictionary<string, RouteTraceSelection>(StringComparer.Ordinal);
        private readonly HashSet<string> selectedRouteIds = new HashSet<string>(StringComparer.Ordinal);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly long[] routeDurationHistogram = new long[64];
        private readonly long[] frameDurationHistogram = new long[64];
#else
        private readonly long[] routeDurationHistogram = Array.Empty<long>();
        private readonly long[] frameDurationHistogram = Array.Empty<long>();
#endif
        private int diagnosticStart;
        private int routeHistoryStart, routeHistoryCount, midiHistoryStart, midiHistoryCount, transportHistoryStart, transportHistoryCount;
        private long monitorSequence, droppedRouteHistory, droppedMidiHistory, droppedTransportHistory;
        private long routeTraceCount, sourceValueCount, modifierValueCount, finalValueCount, outputWriteCount;
        private long rawMidiInputCount, logicalMidiInputCount, midiLedOutputCount, transportTapCount, transportClockPulseCount;
        private long routeProcessingCount, routeProcessingTicks, routeProcessingMaximumTicks;
        private long frameCallbackSamples, frameCallbackTicks, frameCallbackMaximumTicks, lastFrameCallbackCount;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private int monitorCaptureEnabled = 1;
#else
        private int monitorCaptureEnabled;
#endif
        public long DroppedDiagnosticCount { get; private set; }

        public IFrameClock FrameClock { get; internal set; }
        public IMusicalTransport Transport { get; internal set; }
        public MondmadeVJ0Runtime Runtime { get; internal set; }
        public IReadOnlyList<RuntimeDiagnostic> Diagnostics => diagnostics;
        public bool MonitorCaptureSupported
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return true;
#else
                return false;
#endif
            }
        }
        public bool MonitorCaptureEnabled
        {
            get => MonitorCaptureSupported && Volatile.Read(ref monitorCaptureEnabled) != 0;
            set
            {
                var enabled = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                enabled = value;
#else
                enabled = false;
#endif
                var previous = Interlocked.Exchange(ref monitorCaptureEnabled, enabled ? 1 : 0);
                if ((previous != 0) == enabled) return;
                lock (monitorLock)
                {
                    foreach (var selection in routeTraceSelections.Values)
                    {
                        Interlocked.Exchange(ref selection.LastStageTimestampTicks, 0L);
                        Interlocked.Exchange(ref selection.LastStageFrameIndex, -1L);
                    }
                }
            }
        }
        public int SelectedRouteCount { get { lock (monitorLock) return selectedRouteIds.Count; } }
        public long DroppedRouteHistoryCount => Interlocked.Read(ref droppedRouteHistory);
        public long DroppedMidiHistoryCount => Interlocked.Read(ref droppedMidiHistory);
        public long DroppedTransportHistoryCount => Interlocked.Read(ref droppedTransportHistory);
        public long RouteTraceCount => Interlocked.Read(ref routeTraceCount);
        public long SourceValueCount => Interlocked.Read(ref sourceValueCount);
        public long ModifierValueCount => Interlocked.Read(ref modifierValueCount);
        public long FinalValueCount => Interlocked.Read(ref finalValueCount);
        public long OutputWriteCount => Interlocked.Read(ref outputWriteCount);
        public long RawMidiInputCount => Interlocked.Read(ref rawMidiInputCount);
        public long LogicalMidiInputCount => Interlocked.Read(ref logicalMidiInputCount);
        public long MidiLedOutputCount => Interlocked.Read(ref midiLedOutputCount);
        public long TransportTapCount => Interlocked.Read(ref transportTapCount);
        public long TransportClockPulseCount => Interlocked.Read(ref transportClockPulseCount);
        public long RouteProcessingCount => Interlocked.Read(ref routeProcessingCount);
        public double RouteProcessingMeanMilliseconds => ToMilliseconds(Interlocked.Read(ref routeProcessingTicks), RouteProcessingCount);
        public double RouteProcessingMaximumMilliseconds => TicksToMilliseconds(Interlocked.Read(ref routeProcessingMaximumTicks));
        public double RouteProcessingP99Milliseconds => HistogramPercentile(routeDurationHistogram, RouteProcessingCount);
        public long FrameCallbackSamples => Interlocked.Read(ref frameCallbackSamples);
        public long LastFrameCallbackCount => Interlocked.Read(ref lastFrameCallbackCount);
        public double FrameCallbackMeanMilliseconds => ToMilliseconds(Interlocked.Read(ref frameCallbackTicks), FrameCallbackSamples);
        public double FrameCallbackMaximumMilliseconds => TicksToMilliseconds(Interlocked.Read(ref frameCallbackMaximumTicks));
        public double FrameCallbackP99Milliseconds => HistogramPercentile(frameDurationHistogram, FrameCallbackSamples);

        public RouteTraceSelection GetRouteTraceSelection(string routeId)
        {
            if (string.IsNullOrEmpty(routeId)) return null;
            lock (monitorLock)
            {
                if (!routeTraceSelections.TryGetValue(routeId, out var selection))
                    routeTraceSelections.Add(routeId, selection = new RouteTraceSelection(routeId));
                return selection;
            }
        }

        public bool SetRouteTraceSelected(string routeId, bool selected)
        {
            if (string.IsNullOrEmpty(routeId)) return false;
            lock (monitorLock)
            {
                if (!routeTraceSelections.TryGetValue(routeId, out var trace))
                    routeTraceSelections.Add(routeId, trace = new RouteTraceSelection(routeId));
                if (selected)
                {
                    if (!selectedRouteIds.Contains(routeId) && selectedRouteIds.Count >= MonitorRouteSelectionLimit) return false;
                    selectedRouteIds.Add(routeId);
                    trace.IsSelected = true;
                    Interlocked.Exchange(ref trace.LastStageTimestampTicks, 0L);
                    Interlocked.Exchange(ref trace.LastStageFrameIndex, -1L);
                }
                else
                {
                    selectedRouteIds.Remove(routeId);
                    trace.IsSelected = false;
                    Interlocked.Exchange(ref trace.LastStageTimestampTicks, 0L);
                    Interlocked.Exchange(ref trace.LastStageFrameIndex, -1L);
                }
                return true;
            }
        }

        internal Observable<T> TraceRoute<T>(Observable<T> stream, RouteTraceSelection selection, string routeName,
            RuntimeTraceStage stage, int index, string nodeId, string nodeName)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (stream == null || selection == null) return stream;
            return stream.Select(value =>
            {
                if (MonitorCaptureEnabled && selection.IsSelected)
                {
                    var now = Stopwatch.GetTimestamp();
                    var previous = Interlocked.Exchange(ref selection.LastStageTimestampTicks, now);
                    var frame = FrameClock?.FrameIndex ?? -1L;
                    var previousFrame = Interlocked.Exchange(ref selection.LastStageFrameIndex, frame);
                    var elapsed = previousFrame == frame && (stage == RuntimeTraceStage.Modifier || stage == RuntimeTraceStage.TriggerModifier) && previous > 0L
                        ? now - previous : 0L;
                    RecordRouteValue(selection.RouteId, routeName, stage, index, nodeId, nodeName, value, null, elapsed);
                }
                return value;
            });
#else
            return stream;
#endif
        }

        internal void RecordRouteValue(string routeId, string routeName, RuntimeTraceStage stage, int index,
            string nodeId, string nodeName, object value, string outcome, long processingTicks = 0L)
        {
            if (!MonitorCaptureEnabled) return;
            lock (monitorLock)
            {
                var sample = new RuntimeRouteTrace(Interlocked.Increment(ref monitorSequence), MonitorTimestamp(), routeId,
                    routeName, stage, index, nodeId, nodeName, value, outcome, TicksToMilliseconds(processingTicks));
                Append(routeHistory, ref routeHistoryStart, ref routeHistoryCount, sample, ref droppedRouteHistory);
            }
            Interlocked.Increment(ref routeTraceCount);
            switch (stage)
            {
                case RuntimeTraceStage.SourceRaw: Interlocked.Increment(ref sourceValueCount); break;
                case RuntimeTraceStage.Modifier: Interlocked.Increment(ref modifierValueCount); break;
                case RuntimeTraceStage.Final: Interlocked.Increment(ref finalValueCount); break;
                case RuntimeTraceStage.Output:
                case RuntimeTraceStage.TriggerOutput: Interlocked.Increment(ref outputWriteCount); break;
            }
            if (processingTicks > 0L) RecordRouteProcessing(processingTicks);
        }

        public void RecordMidiRaw(string surfaceId, string surfaceName, int addressType, int channel, int number, float value)
        {
            if (!MonitorCaptureEnabled) return;
            lock (monitorLock)
            {
                Append(midiHistory, ref midiHistoryStart, ref midiHistoryCount,
                    new RuntimeMidiTrace(Interlocked.Increment(ref monitorSequence), MonitorTimestamp(), RuntimeMidiEventKind.RawInput,
                        surfaceId, surfaceName, null, null, channel, addressType, number, value), ref droppedMidiHistory);
            }
            Interlocked.Increment(ref rawMidiInputCount);
        }

        public void RecordMidiLogical(string surfaceId, string surfaceName, string controlId, string label, int channel,
            float value, float velocity, float pressure, bool pressed, bool pressedDown, bool released)
        {
            if (!MonitorCaptureEnabled) return;
            lock (monitorLock)
            {
                Append(midiHistory, ref midiHistoryStart, ref midiHistoryCount,
                    new RuntimeMidiTrace(Interlocked.Increment(ref monitorSequence), MonitorTimestamp(), RuntimeMidiEventKind.LogicalInput,
                        surfaceId, surfaceName, controlId, label, channel, -1, -1, value, velocity, pressure, pressed, pressedDown, released), ref droppedMidiHistory);
            }
            Interlocked.Increment(ref logicalMidiInputCount);
        }

        public void RecordMidiLedQueued(string surfaceId, string surfaceName, string controlId, int channel, int number, float value,
            int red, int green, int blue, float brightness, int effect)
        {
            if (!MonitorCaptureEnabled) return;
            lock (monitorLock)
            {
                Append(midiHistory, ref midiHistoryStart, ref midiHistoryCount,
                    new RuntimeMidiTrace(Interlocked.Increment(ref monitorSequence), MonitorTimestamp(), RuntimeMidiEventKind.LedOutputQueued,
                        surfaceId, surfaceName, controlId, "Queued", channel, -1, number, value, red: red, green: green, blue: blue,
                        brightness: brightness, effect: effect), ref droppedMidiHistory);
            }
            Interlocked.Increment(ref midiLedOutputCount);
        }

        public void RecordTransportEvent(RuntimeTransportEventKind kind, double timestamp, double bpm,
            double beatPosition, string detail = null)
        {
            if (!MonitorCaptureEnabled) return;
            lock (monitorLock)
            {
                var sample = new RuntimeTransportTrace(Interlocked.Increment(ref monitorSequence),
                    timestamp > 0d && !double.IsNaN(timestamp) && !double.IsInfinity(timestamp) ? timestamp : MonitorTimestamp(),
                    kind, bpm, beatPosition, detail);
                Append(transportHistory, ref transportHistoryStart, ref transportHistoryCount, sample, ref droppedTransportHistory);
            }
            if (kind == RuntimeTransportEventKind.Tap) Interlocked.Increment(ref transportTapCount);
            if (kind == RuntimeTransportEventKind.MidiClockPulse) Interlocked.Increment(ref transportClockPulseCount);
        }

        internal void RecordRouteProcessing(long elapsedTicks)
        {
            if (!MonitorCaptureEnabled) return;
            Interlocked.Increment(ref routeProcessingCount);
            Interlocked.Add(ref routeProcessingTicks, Math.Max(0L, elapsedTicks));
            UpdateMaximum(ref routeProcessingMaximumTicks, elapsedTicks);
            Interlocked.Increment(ref routeDurationHistogram[DurationBucket(elapsedTicks)]);
        }

        internal void RecordFrameCallbacks(int callbackCount, long elapsedTicks)
        {
            if (!MonitorCaptureEnabled) return;
            Interlocked.Exchange(ref lastFrameCallbackCount, callbackCount);
            Interlocked.Increment(ref frameCallbackSamples);
            Interlocked.Add(ref frameCallbackTicks, Math.Max(0L, elapsedTicks));
            UpdateMaximum(ref frameCallbackMaximumTicks, elapsedTicks);
            Interlocked.Increment(ref frameDurationHistogram[DurationBucket(elapsedTicks)]);
        }

        public int CopyRouteHistory(List<RuntimeRouteTrace> destination, string routeId = null, int maximumCount = MonitorHistoryCapacity)
            => CopyHistory(destination, routeHistory, ref routeHistoryStart, ref routeHistoryCount, routeId,
                item => item.RouteId, Math.Max(0, maximumCount));

        public int CopyMidiHistory(List<RuntimeMidiTrace> destination, int maximumCount = MonitorHistoryCapacity)
            => CopyHistory(destination, midiHistory, ref midiHistoryStart, ref midiHistoryCount, null, null, Math.Max(0, maximumCount));

        public int CopyTransportHistory(List<RuntimeTransportTrace> destination, int maximumCount = MonitorHistoryCapacity)
            => CopyHistory(destination, transportHistory, ref transportHistoryStart, ref transportHistoryCount, null, null, Math.Max(0, maximumCount));

        private int CopyHistory<T>(List<T> destination, T[] history, ref int start, ref int count, string routeId,
            Func<T, string> routeSelector, int maximumCount)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            lock (monitorLock)
            {
                if (maximumCount == 0) return 0;
                if (routeSelector != null && routeId != null)
                {
                    for (var offset = count - 1; offset >= 0 && destination.Count < maximumCount; offset--)
                    {
                        var item = history[(start + offset) % history.Length];
                        if (string.Equals(routeSelector(item), routeId, StringComparison.Ordinal)) destination.Add(item);
                    }
                    destination.Reverse();
                }
                else
                {
                    var skip = Math.Max(0, count - maximumCount);
                    for (var offset = skip; offset < count; offset++) destination.Add(history[(start + offset) % history.Length]);
                }
            }
            return destination.Count;
        }

        private static void Append<T>(T[] history, ref int start, ref int count, T item, ref long dropped)
        {
            if (count < history.Length)
            {
                history[(start + count) % history.Length] = item;
                count++;
                return;
            }
            history[start] = item;
            start = (start + 1) % history.Length;
            dropped++;
        }

        private static double MonitorTimestamp() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        private static void UpdateMaximum(ref long target, long value)
        {
            var current = Interlocked.Read(ref target);
            while (value > current)
            {
                var observed = Interlocked.CompareExchange(ref target, value, current);
                if (observed == current) break;
                current = observed;
            }
        }

        private static int DurationBucket(long ticks)
        {
            if (ticks <= 0) return 0;
            var microseconds = ticks * 1000000L / Stopwatch.Frequency;
            var bucket = 0;
            while (microseconds > 0 && bucket < 63) { bucket++; microseconds >>= 1; }
            return bucket;
        }

        private static double HistogramPercentile(long[] histogram, long count)
        {
            if (count <= 0) return 0d;
            var threshold = (long)Math.Ceiling(count * 0.99d);
            long cumulative = 0;
            for (var i = 0; i < histogram.Length; i++)
            {
                cumulative += Interlocked.Read(ref histogram[i]);
                if (cumulative >= threshold) return Math.Pow(2d, i) / 1000d;
            }
            return Math.Pow(2d, histogram.Length - 1) / 1000d;
        }

        private static double ToMilliseconds(long ticks, long count)
            => count <= 0 ? 0d : TicksToMilliseconds(ticks) / count;

        private static double TicksToMilliseconds(long ticks)
            => ticks <= 0 ? 0d : ticks * 1000d / Stopwatch.Frequency;

        public void Register<T>(T service) where T : class
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            var type = typeof(T);
            if (services.TryGetValue(type, out var existing) && !ReferenceEquals(existing, service))
                throw new InvalidOperationException($"Service {type.FullName} is already registered.");
            services[type] = service;
            if (service is IMusicalTransport transport) Transport = transport;
            if (service is IFrameClock clock) FrameClock = clock;
        }

        public bool TryGet<T>(out T service) where T : class
        {
            if (services.TryGetValue(typeof(T), out var value))
            {
                service = (T)value;
                return true;
            }
            service = null;
            return false;
        }

        public T Get<T>() where T : class
        {
            if (TryGet<T>(out var service)) return service;
            throw new InvalidOperationException($"Required service {typeof(T).FullName} is not registered.");
        }

        public void ClearServices()
        {
            services.Clear();
            FrameClock = null;
            Transport = null;
            Runtime = null;
        }

        public void Report(string code, string message, string routeId = null, string nodeId = null, Exception exception = null)
        {
            var diagnostic = new RuntimeDiagnostic(DateTime.UtcNow, code, message, routeId, nodeId, exception?.ToString());
            if (diagnostics.Count == 4096)
            {
                diagnostics[diagnosticStart] = diagnostic;
                diagnosticStart = (diagnosticStart + 1) % diagnostics.Count;
                DroppedDiagnosticCount++;
            }
            else diagnostics.Add(diagnostic);

            var key = $"{code}/{routeId}/{nodeId}/{message}";
            if (!throttles.TryGetValue(key, out var throttle))
            {
                throttle = new DiagnosticThrottle { LastLogTime = lifetime.Elapsed.TotalSeconds };
                throttles.Add(key, throttle);
                LogDiagnostic(diagnostic, 1);
                return;
            }

            throttle.Count++;
            var elapsed = lifetime.Elapsed.TotalSeconds - throttle.LastLogTime;
            if (elapsed >= 5d)
            {
                LogDiagnostic(diagnostic, throttle.Count);
                throttle.Count = 1;
                throttle.LastLogTime = lifetime.Elapsed.TotalSeconds;
            }
        }

        private static void LogDiagnostic(RuntimeDiagnostic diagnostic, int repeatCount)
        {
            if (Application.isEditor || Debug.isDebugBuild)
                UnityEngine.Debug.LogWarning($"[MondmadeVJ0:{diagnostic.Code}] {diagnostic.Message}" +
                    (repeatCount > 1 ? $" (repeated {repeatCount} times in the last interval)" : string.Empty));
        }

        public IReadOnlyList<RuntimeDiagnostic> GetDiagnostics()
        {
            if (diagnosticStart == 0) return diagnostics.ToArray();
            var ordered = new RuntimeDiagnostic[diagnostics.Count];
            var tail = diagnostics.Count - diagnosticStart;
            diagnostics.CopyTo(diagnosticStart, ordered, 0, tail);
            diagnostics.CopyTo(0, ordered, tail, diagnosticStart);
            return ordered;
        }

        public IDisposable Measure() => new StopWatchScope(Stopwatch.StartNew());

        private sealed class StopWatchScope : IDisposable
        {
            private Stopwatch watch;
            public StopWatchScope(Stopwatch watch) => this.watch = watch;
            public void Dispose() { watch?.Stop(); watch = null; }
        }

        private sealed class DiagnosticThrottle
        {
            public double LastLogTime;
            public int Count = 1;
        }
    }

    [Serializable]
    public readonly struct RuntimeDiagnostic
    {
        public readonly DateTime TimeUtc;
        public readonly string Code;
        public readonly string Message;
        public readonly string RouteId;
        public readonly string NodeId;
        public readonly string Exception;

        public RuntimeDiagnostic(DateTime timeUtc, string code, string message, string routeId, string nodeId, string exception)
        {
            TimeUtc = timeUtc;
            Code = code;
            Message = message;
            RouteId = routeId;
            NodeId = nodeId;
            Exception = exception;
        }
    }
}
