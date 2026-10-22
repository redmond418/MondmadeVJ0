using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

namespace MondmadeVJ0
{
    public enum TriggerTimeUnit { Seconds, Beats }
    public enum QuantizeGrid { Bar, Beat, HalfBeat, QuarterBeat, EighthBeat, SixteenthBeat, HalfTriplet, QuarterTriplet, EighthTriplet, SixteenthTriplet }

    internal sealed class TriggerCounterState { public long Count; }
    internal sealed class TriggerTimeState { public double Last, Deadline; public bool HasLast, Pending; }

    internal static class TriggerModifierClock
    {
        public static double Now(RouteContext context, TriggerTimeUnit unit)
        {
            if (unit == TriggerTimeUnit.Beats)
            {
                if (context.Transport == null) throw new InvalidOperationException("Beat-based trigger modifier requires Transport.");
                return context.Transport.BeatPosition;
            }
            return context.FrameClock?.UnscaledTime ?? UnityEngine.Time.realtimeSinceStartupAsDouble;
        }
        public static Observable<Unit> OnFrame(RouteContext context, RouteNode node, Action update)
        {
            if (context.FrameClock == null) throw new InvalidOperationException(node.GetType().Name + " requires FrameClock.");
            return Observable.Create<Unit>(observer =>
            {
                Action<double, double> callback = (_, __) => { update(); };
                context.FrameClock.Updated += callback;
                return Disposable.Create(() => context.FrameClock.Updated -= callback);
            });
        }
        public static double GridBeats(QuantizeGrid grid, IMusicalTransport transport) => grid switch
        {
            QuantizeGrid.Bar => transport.BarLengthBeats,
            QuantizeGrid.Beat => 1d,
            QuantizeGrid.HalfBeat => 1d / 2,
            QuantizeGrid.QuarterBeat => 1d / 4,
            QuantizeGrid.EighthBeat => 1d / 8,
            QuantizeGrid.SixteenthBeat => 1d / 16,
            QuantizeGrid.HalfTriplet => 1d / 3,
            QuantizeGrid.QuarterTriplet => 1d / 6,
            QuantizeGrid.EighthTriplet => 1d / 12,
            _ => 1d / 24,
        };

        public static double GridPhase(QuantizeGrid grid, IMusicalTransport transport)
        {
            if (grid == QuantizeGrid.Bar) return transport.BarPhase;
            var step = GridBeats(grid, transport);
            var phase = transport.BeatPosition / step;
            return phase - Math.Floor(phase);
        }

        public static double NextQuantizeBoundary(QuantizeGrid grid, IMusicalTransport transport, double position, double tolerance)
        {
            var step = GridBeats(grid, transport);
            var origin = grid == QuantizeGrid.Bar ? position - transport.BarPhase * transport.BarLengthBeats : 0d;
            var boundary = origin + Math.Ceiling((position - origin - tolerance) / step) * step;
            return Math.Max(0d, boundary);
        }

        public static double NextSubdivisionBoundary(QuantizeGrid grid, IMusicalTransport transport, double position, double tolerance)
        {
            var step = GridBeats(grid, transport);
            var origin = grid == QuantizeGrid.Bar ? position - transport.BarPhase * transport.BarLengthBeats : 0d;
            return origin + (Math.Floor((position - origin) / step + tolerance) + 1d) * step;
        }
    }

    [Serializable]
    public sealed class CooldownTriggerModifier : RouteNode, ITriggerModifier
    {
        [SerializeField, Min(0f)] private float seconds = 0.1f;
        public Observable<Unit> Apply(Observable<Unit> source, RouteContext context)
        {
            if (seconds < 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) throw new InvalidOperationException("Cooldown must be finite and non-negative.");
            var state = context.GetNodeState(this, "cooldown", () => new TriggerTimeState());
            return Observable.Create<Unit>(observer => SourceStream.Subscribe(source, value =>
            {
                var now = TriggerModifierClock.Now(context, TriggerTimeUnit.Seconds);
                if (!state.HasLast || now - state.Last >= seconds) { state.HasLast = true; state.Last = now; observer.OnNext(value); }
            }, observer.OnErrorResume));
        }
    }

    [Serializable]
    public sealed class DelayTriggerModifier : RouteNode, ITriggerModifier
    {
        [SerializeField, Min(0f)] private float delay = 0.1f;
        [SerializeField] private TriggerTimeUnit timeUnit;
        private const int Capacity = 1024;
        public Observable<Unit> Apply(Observable<Unit> source, RouteContext context)
        {
            if (delay < 0f || float.IsNaN(delay) || float.IsInfinity(delay)) throw new InvalidOperationException("Delay must be finite and non-negative.");
            if (timeUnit == TriggerTimeUnit.Beats && context.Transport == null) throw new InvalidOperationException("Beat delay requires Transport.");
            return Observable.Create<Unit>(observer =>
            {
                var subscriptions = new CompositeDisposable();
                var queue = new Queue<double>();
                subscriptions.Add(SourceStream.Subscribe(source, _ =>
                {
                    if (queue.Count >= Capacity) { context.Report("TRIGGER_QUEUE_FULL", "Delay queue is full; the new trigger was dropped.", this); return; }
                    queue.Enqueue(TriggerModifierClock.Now(context, timeUnit) + delay);
                }, observer.OnErrorResume));
                if (context.FrameClock == null) throw new InvalidOperationException("DelayTriggerModifier requires FrameClock.");
                Action<double, double> update = (_, __) =>
                {
                    if (timeUnit == TriggerTimeUnit.Beats && !context.Transport.IsRunning) { queue.Clear(); return; }
                    var now = TriggerModifierClock.Now(context, timeUnit);
                    var sent = 0;
                    while (queue.Count > 0 && queue.Peek() <= now && sent++ < 256) { queue.Dequeue(); observer.OnNext(Unit.Default); }
                };
                context.FrameClock.Updated += update;
                subscriptions.Add(Disposable.Create(() => { context.FrameClock.Updated -= update; queue.Clear(); }));
                return subscriptions;
            });
        }
    }

    [Serializable]
    public sealed class EveryNthTriggerModifier : RouteNode, ITriggerModifier
    {
        [SerializeField, Min(1)] private int every = 2;
        public Observable<Unit> Apply(Observable<Unit> source, RouteContext context)
        {
            if (every < 1) throw new InvalidOperationException("EveryNth requires N >= 1.");
            var state = context.GetNodeState(this, "counter", () => new TriggerCounterState());
            return Observable.Create<Unit>(observer => SourceStream.Subscribe(source, _ => { state.Count++; if (state.Count % every == 0) observer.OnNext(Unit.Default); }, observer.OnErrorResume));
        }
    }

    [Serializable]
    public sealed class RandomChanceTriggerModifier : RouteNode, ITriggerModifier
    {
        [SerializeField, Range(0f, 1f)] private float probability = 0.5f;
        public Observable<Unit> Apply(Observable<Unit> source, RouteContext context)
        {
            if (probability < 0f || probability > 1f || !ModifierStream.Finite(probability)) throw new InvalidOperationException("Probability must be in [0, 1].");
            var random = context.GetNodeState(this, "random", () => new DeterministicRandomState(context.RouteId + "/" + NodeId));
            return Observable.Create<Unit>(observer => SourceStream.Subscribe(source, _ => { if (random.NextUnit() < probability) observer.OnNext(Unit.Default); }, observer.OnErrorResume));
        }
    }

    [Serializable]
    public sealed class GateTriggerModifier : RouteNode, ITriggerModifier
    {
        [SerializeReference] private IValueSource<bool> gate;
        public Observable<Unit> Apply(Observable<Unit> source, RouteContext context)
        {
            if (gate == null) throw new InvalidOperationException("GateTriggerModifier requires a Bool source.");
            var gateStream = gate.Build(context);
            return Observable.Create<Unit>(observer =>
            {
                var d = new CompositeDisposable();
                var isOpen = false;
                d.Add(SourceStream.Subscribe(gateStream, value => isOpen = value, observer.OnErrorResume));
                d.Add(SourceStream.Subscribe(source, value => { if (isOpen) observer.OnNext(value); }, observer.OnErrorResume));
                return d;
            });
        }
    }

    [Serializable]
    public sealed class QuantizeTriggerModifier : RouteNode, ITriggerModifier
    {
        [SerializeField] private QuantizeGrid grid;
        private const int Capacity = 1024;
        private const int PerFrameLimit = 256;
        private const double BoundaryTolerance = 1e-7;
        public Observable<Unit> Apply(Observable<Unit> source, RouteContext context)
        {
            if (context.Transport == null) throw new InvalidOperationException("Quantize requires Transport.");
            var state = context.GetNodeState(this, "queue", () => new QuantizeState());
            var step = TriggerModifierClock.GridBeats(grid, context.Transport);
            if (!(step > 0d) || double.IsNaN(step) || double.IsInfinity(step)) throw new InvalidOperationException("Quantize grid is invalid.");
            return Observable.Create<Unit>(observer =>
            {
                var d = new CompositeDisposable();
                var transport = context.Transport;
                var lastPosition = transport.BeatPosition;
                var timelineRevision = transport.TimelineRevision;
                state.Targets.Clear();
                d.Add(SourceStream.Subscribe(source, _ =>
                {
                    var current = transport.BeatPosition;
                    if (timelineRevision != transport.TimelineRevision || current + BoundaryTolerance < lastPosition)
                    {
                        state.Targets.Clear();
                        timelineRevision = transport.TimelineRevision;
                    }
                    lastPosition = current;
                    if (!transport.IsRunning) { context.Report("QUANTIZE_STOPPED", "Quantize input was dropped while Transport is stopped.", this); return; }
                    if (state.Targets.Count >= Capacity) { context.Report("TRIGGER_QUEUE_FULL", "Quantize queue is full; the new trigger was dropped.", this); return; }
                    var currentStep = TriggerModifierClock.GridBeats(grid, transport);
                    if (!(currentStep > 0d) || double.IsNaN(currentStep) || double.IsInfinity(currentStep)) { context.Report("QUANTIZE_GRID_INVALID", "Quantize grid became invalid; the trigger was dropped.", this); return; }
                    var boundary = TriggerModifierClock.NextQuantizeBoundary(grid, transport, current, BoundaryTolerance);
                    state.Targets.Add(boundary);
                    state.Targets.Sort();
                }, observer.OnErrorResume));
                if (context.FrameClock == null) throw new InvalidOperationException("Quantize requires FrameClock.");
                Action<double, double> update = (_, __) =>
                {
                    var position = transport.BeatPosition;
                    if (!transport.IsRunning || timelineRevision != transport.TimelineRevision || position + BoundaryTolerance < lastPosition)
                    {
                        state.Targets.Clear();
                        timelineRevision = transport.TimelineRevision;
                        lastPosition = position;
                        return;
                    }
                    lastPosition = position;
                    var sent = 0;
                    while (state.Targets.Count > 0 && state.Targets[0] <= position + BoundaryTolerance && sent < PerFrameLimit)
                    {
                        state.Targets.RemoveAt(0);
                        observer.OnNext(Unit.Default);
                        sent++;
                    }
                    if (state.Targets.Count > 0 && state.Targets[0] <= position + BoundaryTolerance)
                    {
                        var dropped = 0;
                        while (state.Targets.Count > 0 && state.Targets[0] <= position + BoundaryTolerance) { state.Targets.RemoveAt(0); dropped++; }
                        context.Report("QUANTIZE_FRAME_LIMIT", $"Dropped {dropped} overdue quantized triggers after the per-frame cap.", this);
                    }
                };
                context.FrameClock.Updated += update;
                d.Add(Disposable.Create(() => { context.FrameClock.Updated -= update; state.Targets.Clear(); }));
                return d;
            });
        }
        private sealed class QuantizeState { public readonly List<double> Targets = new List<double>(); }
    }

    [Serializable]
    public sealed class DebounceTriggerModifier : RouteNode, ITriggerModifier
    {
        [SerializeField, Min(0f)] private float seconds = 0.05f;
        public Observable<Unit> Apply(Observable<Unit> source, RouteContext context)
        {
            if (seconds < 0f || !ModifierStream.Finite(seconds)) throw new InvalidOperationException("Debounce duration must be non-negative.");
            var state = context.GetNodeState(this, "debounce", () => new TriggerTimeState());
            return Observable.Create<Unit>(observer =>
            {
                var d = new CompositeDisposable();
                d.Add(SourceStream.Subscribe(source, _ => { state.Pending = true; state.Deadline = TriggerModifierClock.Now(context, TriggerTimeUnit.Seconds) + seconds; }, observer.OnErrorResume));
                if (context.FrameClock == null) throw new InvalidOperationException("Debounce requires FrameClock.");
                Action<double, double> update = (_, __) =>
                {
                    if (!state.Pending || TriggerModifierClock.Now(context, TriggerTimeUnit.Seconds) < state.Deadline) return;
                    state.Pending = false;
                    observer.OnNext(Unit.Default);
                };
                context.FrameClock.Updated += update;
                d.Add(Disposable.Create(() => { context.FrameClock.Updated -= update; state.Pending = false; }));
                return d;
            });
        }
    }
}
