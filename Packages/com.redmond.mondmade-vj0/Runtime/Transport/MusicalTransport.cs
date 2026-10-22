using System;
using System.Collections.Generic;
using UnityEngine;

namespace MondmadeVJ0
{
    public enum TransportClockSource { Unscaled, Dsp, External }
    public enum TransportSyncState { Internal, Acquiring, Locked, Lost }

    [DisallowMultipleComponent]
    public sealed class MusicalTransport : MonoBehaviour, IRuntimeModule, IMusicalTransport, IExternalMidiClock, IExternalTransportClock
    {
        [SerializeField, Range(30f, 300f)] private double baseBpm = 120d;
        [SerializeField, Min(0.0001f)] private double multiplier = 1d;
        [SerializeField, Min(1)] private int beatsPerBar = 4;
        [SerializeField] private int beatUnit = 4;
        [SerializeField] private bool startsRunning = true;
        [SerializeField] private TransportClockSource clockSource;

        private RuntimeServices services;
        private double beatPosition;
        private double barOrigin;
        private int pendingNumerator;
        private int pendingDenominator;
        private double lastDspTime;
        private double lastExternalTime;
        private double lastExternalBeat;
        private double externalAcquiringSince;
        private bool hasExternalClock;
        private bool running;
        private readonly List<double> tapIntervals = new List<double>(8);
        private readonly List<double> candidateIntervals = new List<double>(2);
        private double lastTap = double.NaN;
        private double lastMidiClockPulse = double.NaN;
        private readonly List<double> midiClockIntervals = new List<double>(24);
        private readonly double[] medianScratch = new double[24];
        private readonly object callbackLock = new object();
        private Action<long>[] beatCallbacks = Array.Empty<Action<long>>();
        private Action<long>[] barCallbacks = Array.Empty<Action<long>>();

        public string ModuleName => nameof(MusicalTransport);
        public double BaseBpm => baseBpm;
        public double Multiplier => multiplier;
        public double EffectiveBpm => baseBpm * multiplier;
        public double BeatPosition => beatPosition;
        public long BeatIndex => FloorToLong(beatPosition);
        public double BeatPhase => PositiveFraction(beatPosition);
        public long BarIndex => BarLengthBeats <= 0d ? 0 : FloorToLong((beatPosition - barOrigin) / BarLengthBeats);
        public double BarPhase => BarLengthBeats <= 0d ? 0 : PositiveFraction((beatPosition - barOrigin) / BarLengthBeats);
        public double BarLengthBeats => beatsPerBar * 4d / beatUnit;
        public long TimelineRevision { get; private set; }
        public bool IsRunning => running;
        public TransportSyncState SyncState { get; private set; } = TransportSyncState.Internal;
        public event Action<long> Beat
        {
            add { lock (callbackLock) beatCallbacks = AddCallback(beatCallbacks, value); }
            remove { lock (callbackLock) beatCallbacks = RemoveCallback(beatCallbacks, value); }
        }
        public event Action<long> Bar
        {
            add { lock (callbackLock) barCallbacks = AddCallback(barCallbacks, value); }
            remove { lock (callbackLock) barCallbacks = RemoveCallback(barCallbacks, value); }
        }

        public void Register(RuntimeServices runtimeServices)
        {
            services = runtimeServices ?? throw new ArgumentNullException(nameof(runtimeServices));
            services.Register<IMusicalTransport>(this);
            services.Register<IExternalMidiClock>(this);
            services.Register<IExternalTransportClock>(this);
        }

        public void Initialize(RuntimeServices runtimeServices)
        {
            services = runtimeServices;
            running = startsRunning;
            beatPosition = Math.Max(0d, beatPosition);
            barOrigin = 0d;
            lastDspTime = AudioSettings.dspTime;
            SyncState = clockSource == TransportClockSource.External ? TransportSyncState.Lost : TransportSyncState.Internal;
        }

        public void DrainInputQueue(RuntimeServices runtimeServices, int maxEvents) { }

        public void Tick(RuntimeServices runtimeServices, double deltaTime)
        {
            if (!running) return;
            double delta = Math.Max(0d, deltaTime);
            if (clockSource == TransportClockSource.Dsp)
            {
                var now = AudioSettings.dspTime;
                delta = lastDspTime <= 0d ? 0d : Math.Max(0d, now - lastDspTime);
                lastDspTime = now;
            }
            else if (clockSource == TransportClockSource.External)
            {
                if (!hasExternalClock)
                {
                    SyncState = TransportSyncState.Lost;
                    return;
                }
                var now = Time.realtimeSinceStartupAsDouble;
                var elapsed = now - lastExternalTime;
                if (elapsed >= 0.5d) SyncState = TransportSyncState.Lost;
                else SyncState = now - externalAcquiringSince < 1d ? TransportSyncState.Acquiring : TransportSyncState.Locked;
                var estimated = lastExternalBeat + elapsed * EffectiveBpm / 60d;
                AdvancePosition(estimated);
                return;
            }
            AdvancePosition(beatPosition + delta * EffectiveBpm / 60d);
        }

        public void FlushFrame(RuntimeServices runtimeServices) { }

        public void Shutdown(RuntimeServices runtimeServices)
        {
            running = false;
            services = null;
            tapIntervals.Clear();
            candidateIntervals.Clear();
        }

        public void SetBaseBpm(double bpm)
        {
            if (double.IsNaN(bpm) || double.IsInfinity(bpm) || bpm < 30d || bpm > 300d) throw new ArgumentOutOfRangeException(nameof(bpm), "BPM must be in [30, 300].");
            baseBpm = bpm;
        }

        public void SetMultiplier(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0d || value > 64d) throw new ArgumentOutOfRangeException(nameof(value), "Multiplier must be finite and in (0, 64].");
            multiplier = value;
        }

        public void SetRunning(bool value)
        {
            if (running && !value) AdvanceTimelineRevision();
            running = value;
            if (value && clockSource == TransportClockSource.Dsp) lastDspTime = AudioSettings.dspTime;
        }

        public void SetTimeSignature(int numerator, int denominator)
        {
            if (numerator < 1 || denominator < 1 || (denominator & (denominator - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(numerator), "Time signature must use a positive numerator and a power-of-two denominator.");
            pendingNumerator = numerator;
            pendingDenominator = denominator;
            var currentBarBoundary = barOrigin + Math.Ceiling((beatPosition - barOrigin - 1e-10d) / BarLengthBeats) * BarLengthBeats;
            if (Math.Abs(beatPosition - currentBarBoundary) <= 1e-10d) ApplyPendingMeter(currentBarBoundary);
        }

        public void Tap(double timestamp)
        {
            if (double.IsNaN(timestamp) || double.IsInfinity(timestamp)) return;
            if (double.IsNaN(lastTap) || timestamp <= lastTap || timestamp - lastTap > 2d)
            {
                lastTap = timestamp;
                tapIntervals.Clear();
                candidateIntervals.Clear();
                services?.RecordTransportEvent(RuntimeTransportEventKind.Tap, timestamp, EffectiveBpm, beatPosition, "Tap history started");
                return;
            }
            var interval = timestamp - lastTap;
            lastTap = timestamp;
            var bpm = 60d / interval;
            if (bpm < 30d || bpm > 300d)
            {
                tapIntervals.Clear(); candidateIntervals.Clear();
                services?.RecordTransportEvent(RuntimeTransportEventKind.Tap, timestamp, EffectiveBpm, beatPosition, "Tap outside 30–300 BPM");
                return;
            }
            var medianInterval = Median(tapIntervals);
            if (tapIntervals.Count >= 3 && medianInterval > 0d && Math.Abs(interval - medianInterval) / medianInterval > 0.2d)
            {
                if (candidateIntervals.Count == 0 || Math.Abs(interval - candidateIntervals[candidateIntervals.Count - 1]) / candidateIntervals[candidateIntervals.Count - 1] <= 0.2d)
                    candidateIntervals.Add(interval);
                else { candidateIntervals.Clear(); candidateIntervals.Add(interval); }
                if (candidateIntervals.Count < 2)
                {
                    services?.RecordTransportEvent(RuntimeTransportEventKind.Tap, timestamp, EffectiveBpm, beatPosition, "Tempo change candidate");
                    return;
                }
                tapIntervals.Clear();
                tapIntervals.AddRange(candidateIntervals);
                candidateIntervals.Clear();
            }
            else
            {
                candidateIntervals.Clear();
                tapIntervals.Add(interval);
                if (tapIntervals.Count > 8) tapIntervals.RemoveAt(0);
            }
            var median = Median(tapIntervals);
            if (median > 0d) SetBaseBpm(60d / median);
            services?.RecordTransportEvent(RuntimeTransportEventKind.Tap, timestamp, EffectiveBpm, beatPosition, "Tap accepted");
        }

        public void ResetPhase()
        {
            AdvanceTimelineRevision();
            beatPosition = 0d;
            barOrigin = 0d;
            services?.RecordTransportEvent(RuntimeTransportEventKind.Seek, Time.realtimeSinceStartupAsDouble, EffectiveBpm, beatPosition, "Reset phase");
            if (clockSource == TransportClockSource.External)
            {
                lastExternalBeat = 0d;
                lastExternalTime = Time.realtimeSinceStartupAsDouble;
                if (hasExternalClock) SyncState = TransportSyncState.Acquiring;
            }
        }

        public void Nudge(double beats)
        {
            var clamped = Math.Max(-1d / 32d, Math.Min(1d / 32d, beats));
            beatPosition = Math.Max(0d, beatPosition + clamped);
        }

        public void Seek(double position)
        {
            if (double.IsNaN(position) || double.IsInfinity(position) || position < 0d) throw new ArgumentOutOfRangeException(nameof(position));
            AdvanceTimelineRevision();
            beatPosition = position;
            barOrigin = position - PositiveFraction((position - barOrigin) / BarLengthBeats) * BarLengthBeats;
            services?.RecordTransportEvent(RuntimeTransportEventKind.Seek, Time.realtimeSinceStartupAsDouble, EffectiveBpm, beatPosition, "Seek");
            if (clockSource == TransportClockSource.External)
            {
                lastExternalBeat = position;
                lastExternalTime = Time.realtimeSinceStartupAsDouble;
                if (hasExternalClock) SyncState = TransportSyncState.Acquiring;
            }
        }

        public void SetClockSource(TransportClockSource source)
        {
            if (clockSource != source) AdvanceTimelineRevision();
            clockSource = source;
            SyncState = source == TransportClockSource.External ? TransportSyncState.Lost : TransportSyncState.Internal;
            hasExternalClock = false;
            lastDspTime = AudioSettings.dspTime;
        }

        public void SetExternalClock(double bpm, double externalBeat, bool externalRunning, double monotonicTimestamp)
        {
            if (clockSource != TransportClockSource.External) return;
            if (double.IsNaN(bpm) || double.IsInfinity(bpm) || bpm < 30d || bpm > 300d || double.IsNaN(externalBeat) || double.IsInfinity(externalBeat) || externalBeat < 0d)
            {
                services?.Report("EXTERNAL_CLOCK_PACKET", "Rejected invalid external transport clock values.");
                return;
            }
            SetBaseBpm(bpm);
            var now = monotonicTimestamp > 0d ? monotonicTimestamp : Time.realtimeSinceStartupAsDouble;
            if (!hasExternalClock || !running || !externalRunning)
            {
                AdvanceTimelineRevision();
                beatPosition = externalBeat;
                lastExternalBeat = externalBeat;
                lastExternalTime = now;
                externalAcquiringSince = now;
                hasExternalClock = true;
                running = externalRunning;
                SyncState = TransportSyncState.Acquiring;
                return;
            }
            var elapsed = Math.Max(0d, now - lastExternalTime);
            var prediction = lastExternalBeat + elapsed * EffectiveBpm / 60d;
            var error = externalBeat - prediction;
            var correction = Math.Max(-1d / 96d, Math.Min(1d / 96d, error));
            if (SyncState == TransportSyncState.Lost) externalAcquiringSince = now;
            lastExternalBeat = prediction + correction;
            lastExternalTime = now;
            AdvancePosition(lastExternalBeat);
            running = true;
            SyncState = now - externalAcquiringSince < 1d ? TransportSyncState.Acquiring : TransportSyncState.Locked;
        }

        public void OnClockPulse(double monotonicTimestamp, double nativeTimestamp)
        {
            if (clockSource != TransportClockSource.External || !running) return;
            services?.RecordTransportEvent(RuntimeTransportEventKind.MidiClockPulse, monotonicTimestamp, EffectiveBpm, beatPosition);
            if (!double.IsNaN(lastMidiClockPulse))
            {
                var interval = monotonicTimestamp - lastMidiClockPulse;
                if (interval > 0d && interval < 1d)
                {
                    midiClockIntervals.Add(interval);
                    if (midiClockIntervals.Count > 24) midiClockIntervals.RemoveAt(0);
                    if (midiClockIntervals.Count >= 24)
                    {
                        var estimate = 60d / (Median(midiClockIntervals) * 24d);
                        if (estimate >= 30d && estimate <= 300d) baseBpm = estimate;
                    }
                }
            }
            lastMidiClockPulse = monotonicTimestamp;
            var pulseTime = monotonicTimestamp > 0d ? monotonicTimestamp : Time.realtimeSinceStartupAsDouble;
            var reacquiring = SyncState == TransportSyncState.Lost;
            var nextBeat = (hasExternalClock ? lastExternalBeat : beatPosition) + 1d / 24d;
            if (reacquiring && hasExternalClock)
                nextBeat = lastExternalBeat + Math.Max(0d, pulseTime - lastExternalTime) * EffectiveBpm / 60d;
            SetExternalClock(baseBpm, nextBeat, true, pulseTime);
        }

        public void OnStart(double monotonicTimestamp)
        {
            if (clockSource != TransportClockSource.External) return;
            services?.RecordTransportEvent(RuntimeTransportEventKind.MidiClockStart, monotonicTimestamp, EffectiveBpm, beatPosition);
            midiClockIntervals.Clear();
            lastMidiClockPulse = double.NaN;
            ResetPhase();
            running = true;
            hasExternalClock = false;
            SyncState = TransportSyncState.Acquiring;
            externalAcquiringSince = monotonicTimestamp;
        }

        public void OnContinue(double monotonicTimestamp)
        {
            if (clockSource != TransportClockSource.External) return;
            services?.RecordTransportEvent(RuntimeTransportEventKind.MidiClockContinue, monotonicTimestamp, EffectiveBpm, beatPosition);
            running = true;
            lastMidiClockPulse = double.NaN;
            hasExternalClock = false;
            lastExternalTime = monotonicTimestamp;
            lastExternalBeat = beatPosition;
            externalAcquiringSince = monotonicTimestamp;
            SyncState = TransportSyncState.Acquiring;
        }

        public void OnStop(double monotonicTimestamp)
        {
            if (clockSource != TransportClockSource.External) return;
            services?.RecordTransportEvent(RuntimeTransportEventKind.MidiClockStop, monotonicTimestamp, EffectiveBpm, beatPosition);
            if (running) AdvanceTimelineRevision();
            running = false;
            lastMidiClockPulse = double.NaN;
            midiClockIntervals.Clear();
            SyncState = TransportSyncState.Lost;
        }

        public void OnSongPositionPointer(int sixteenthNotes, double monotonicTimestamp)
        {
            if (clockSource != TransportClockSource.External) return;
            if (sixteenthNotes < 0 || sixteenthNotes > 16383) { services?.Report("MIDI_SPP_RANGE", "Rejected an out-of-range Song Position Pointer."); return; }
            var position = sixteenthNotes / 4d;
            services?.RecordTransportEvent(RuntimeTransportEventKind.SongPosition, monotonicTimestamp, EffectiveBpm, position, sixteenthNotes.ToString());
            Seek(position);
            lastExternalBeat = position;
            lastExternalTime = monotonicTimestamp;
            hasExternalClock = true;
            lastMidiClockPulse = double.NaN;
            SyncState = TransportSyncState.Acquiring;
        }

        private void AdvancePosition(double position)
        {
            if (position <= beatPosition) { beatPosition = Math.Max(0d, position); return; }
            var old = beatPosition;
            beatPosition = position;
            var firstWholeBeat = FloorToLong(old) + 1;
            var lastWholeBeat = FloorToLong(position);
            var count = lastWholeBeat - firstWholeBeat + 1;
            var processed = 0;
            for (var beat = firstWholeBeat; beat <= lastWholeBeat && processed < 256; beat++, processed++)
            {
                SafeInvoke(beatCallbacks, beat, "TRANSPORT_BEAT");
                if (beat >= barOrigin && IsBarBoundary(beat, barOrigin, BarLengthBeats))
                {
                    if (pendingNumerator > 0) ApplyPendingMeter(beat);
                    SafeInvoke(barCallbacks, BarIndexAt(beat), "TRANSPORT_BAR");
                }
            }
            if (count > 256) services?.Report("TRANSPORT_BOUNDARY_LIMIT", $"Dropped {count - 256} overdue Beat notifications.");
        }

        private void AdvanceTimelineRevision() => TimelineRevision++;

        private void ApplyPendingMeter(double boundary)
        {
            if (pendingNumerator <= 0) return;
            beatsPerBar = pendingNumerator;
            beatUnit = pendingDenominator;
            pendingNumerator = pendingDenominator = 0;
            barOrigin = boundary;
        }

        private static bool IsBarBoundary(double beat, double origin, double length)
        {
            var bars = (beat - origin) / length;
            return Math.Abs(bars - Math.Round(bars)) <= 1e-9d;
        }
        private long BarIndexAt(double beat) => FloorToLong((beat - barOrigin) / BarLengthBeats);
        private static long FloorToLong(double value) => (long)Math.Floor(value);
        private static double PositiveFraction(double value) => value - Math.Floor(value);
        private double Median(List<double> values)
        {
            if (values.Count == 0) return 0d;
            var count = Math.Min(values.Count, medianScratch.Length);
            for (var i = 0; i < count; i++) medianScratch[i] = values[i];
            Array.Sort(medianScratch, 0, count);
            var middle = count / 2;
            return count % 2 == 0 ? (medianScratch[middle - 1] + medianScratch[middle]) * 0.5d : medianScratch[middle];
        }
        private void SafeInvoke(Action<long>[] callbacks, long value, string code)
        {
            for (var i = 0; i < callbacks.Length; i++)
            {
                try { callbacks[i](value); }
                catch (Exception exception) { services?.Report(code, "A Transport subscriber failed.", exception: exception); }
            }
        }

        private static Action<long>[] AddCallback(Action<long>[] current, Action<long> callback)
        {
            if (callback == null) return current;
            var result = new Action<long>[current.Length + 1];
            Array.Copy(current, result, current.Length);
            result[result.Length - 1] = callback;
            return result;
        }

        private static Action<long>[] RemoveCallback(Action<long>[] current, Action<long> callback)
        {
            if (callback == null) return current;
            for (var i = current.Length - 1; i >= 0; i--)
                if (current[i] == callback)
                {
                    var result = new Action<long>[current.Length - 1];
                    if (i > 0) Array.Copy(current, 0, result, 0, i);
                    if (i < current.Length - 1) Array.Copy(current, i + 1, result, i, current.Length - i - 1);
                    return result;
                }
            return current;
        }
    }
}
