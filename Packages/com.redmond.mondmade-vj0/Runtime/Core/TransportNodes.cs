using System;
using R3;
using UnityEngine;

namespace MondmadeVJ0
{
    internal static class TransportNodeHelpers
    {
        public static IMusicalTransport RequireTransport(RouteContext context) => context.Transport ?? throw new InvalidOperationException("Transport service is not registered.");
        public static Observable<T> Observe<T>(RouteContext context, RouteNode node, Func<IMusicalTransport, T> read, bool emitEachFrame = false)
        {
            var transport = RequireTransport(context);
            if (context.FrameClock == null) throw new InvalidOperationException(node.GetType().Name + " requires FrameClock.");
            return Observable.Create<T>(observer =>
            {
                var value = read(transport);
                observer.OnNext(value);
                Action<double, double> update = (_, __) =>
                {
                    var next = read(transport);
                    if (emitEachFrame || !System.Collections.Generic.EqualityComparer<T>.Default.Equals(value, next))
                    {
                        value = next;
                        observer.OnNext(next);
                    }
                };
                context.FrameClock.Updated += update;
                return Disposable.Create(() => context.FrameClock.Updated -= update);
            });
        }
        public static Observable<Unit> Event(RouteContext context, RouteNode node, bool bars)
        {
            var transport = RequireTransport(context);
            return Observable.Create<Unit>(observer =>
            {
                Action<long> callback = _ => observer.OnNext(Unit.Default);
                if (bars) transport.Bar += callback; else transport.Beat += callback;
                return Disposable.Create(() => { if (bars) transport.Bar -= callback; else transport.Beat -= callback; });
            });
        }
        public static double Grid(QuantizeGrid grid, IMusicalTransport transport) => TriggerModifierClock.GridBeats(grid, transport);
    }

    [Serializable] public sealed class TransportBpmFloatSource : RouteNode, IValueSource<float> { public Observable<float> Build(RouteContext c) => TransportNodeHelpers.Observe(c,this,t=>(float)t.EffectiveBpm); }
    [Serializable] public sealed class TransportBaseBpmFloatSource : RouteNode, IValueSource<float> { public Observable<float> Build(RouteContext c) => TransportNodeHelpers.Observe(c,this,t=>(float)t.BaseBpm); }
    [Serializable] public sealed class TransportMultiplierFloatSource : RouteNode, IValueSource<float> { public Observable<float> Build(RouteContext c) => TransportNodeHelpers.Observe(c,this,t=>(float)t.Multiplier); }
    [Serializable] public sealed class BeatPhaseFloatSource : RouteNode, IValueSource<float> { public Observable<float> Build(RouteContext c) => TransportNodeHelpers.Observe(c,this,t=>(float)t.BeatPhase); }
    [Serializable] public sealed class BarPhaseFloatSource : RouteNode, IValueSource<float> { public Observable<float> Build(RouteContext c) => TransportNodeHelpers.Observe(c,this,t=>(float)t.BarPhase); }
    [Serializable]
    public sealed class SubdivisionPhaseFloatSource : RouteNode, IValueSource<float>
    {
        [SerializeField] private QuantizeGrid grid = QuantizeGrid.Beat;
        public Observable<float> Build(RouteContext c) => TransportNodeHelpers.Observe(c,this,t=>(float)(t.BeatPosition / TransportNodeHelpers.Grid(grid,t) - Math.Floor(t.BeatPosition / TransportNodeHelpers.Grid(grid,t))));
    }
    [Serializable] public sealed class BeatIndexIntSource : RouteNode, IValueSource<int> { public Observable<int> Build(RouteContext c) => TransportNodeHelpers.Observe(c,this,t=>SourceStream.SaturateInt(t.BeatIndex)); }
    [Serializable] public sealed class BarIndexIntSource : RouteNode, IValueSource<int> { public Observable<int> Build(RouteContext c) => TransportNodeHelpers.Observe(c,this,t=>SourceStream.SaturateInt(t.BarIndex)); }
    [Serializable] public sealed class BeatTriggerSource : RouteNode, ITriggerSource { public Observable<Unit> Build(RouteContext c) => TransportNodeHelpers.Event(c,this,false); }
    [Serializable] public sealed class BarTriggerSource : RouteNode, ITriggerSource { public Observable<Unit> Build(RouteContext c) => TransportNodeHelpers.Event(c,this,true); }

    [Serializable]
    public sealed class SubdivisionTriggerSource : RouteNode, ITriggerSource
    {
        [SerializeField] private QuantizeGrid grid = QuantizeGrid.Beat;
        private const int MaxPerFrame = 256;
        public Observable<Unit> Build(RouteContext context)
        {
            var transport = TransportNodeHelpers.RequireTransport(context);
            if (context.FrameClock == null) throw new InvalidOperationException("SubdivisionTriggerSource requires FrameClock.");
            var step = TransportNodeHelpers.Grid(grid, transport);
            if (!(step > 0d) || double.IsNaN(step) || double.IsInfinity(step)) throw new InvalidOperationException("Subdivision grid is invalid.");
            return Observable.Create<Unit>(observer =>
            {
                var current = transport.BeatPosition;
                var next = (Math.Floor(current / step + 1e-7d) + 1d) * step;
                Action<double, double> update = (_, __) =>
                {
                    var now = transport.BeatPosition;
                    if (!transport.IsRunning || now + 1e-7d < current)
                    {
                        current = now;
                        next = (Math.Floor(now / step + 1e-7d) + 1d) * step;
                        return;
                    }
                    current = now;
                    var count = 0;
                    while (next <= now + 1e-7d && count < MaxPerFrame)
                    {
                        observer.OnNext(Unit.Default);
                        next += step;
                        count++;
                    }
                    if (next <= now + 1e-7d)
                    {
                        var dropped = (int)Math.Min(int.MaxValue, Math.Floor((now - next) / step) + 1d);
                        next += dropped * step;
                        context.Report("SUBDIVISION_FRAME_LIMIT", $"Dropped {dropped} overdue subdivision triggers.", this);
                    }
                };
                context.FrameClock.Updated += update;
                return Disposable.Create(() => context.FrameClock.Updated -= update);
            });
        }
    }

    [Serializable]
    public sealed class TransportTapTriggerOutput : RouteNode, ITriggerOutput
    {
        [NonSerialized] private RouteContext context;
        public void Initialize(RouteContext routeContext) { context = routeContext; TransportNodeHelpers.RequireTransport(context); }
        public void Invoke() { var timestamp = context.FrameClock?.UnscaledTime ?? UnityEngine.Time.realtimeSinceStartupAsDouble; context.Transport.Tap(timestamp); }
    }
    [Serializable]
    public sealed class TransportResetPhaseTriggerOutput : RouteNode, ITriggerOutput
    {
        [NonSerialized] private IMusicalTransport transport;
        public void Initialize(RouteContext context) => transport = TransportNodeHelpers.RequireTransport(context);
        public void Invoke() => transport.ResetPhase();
    }
    [Serializable]
    public sealed class TransportNudgeForwardTriggerOutput : RouteNode, ITriggerOutput
    {
        [NonSerialized] private IMusicalTransport transport;
        public void Initialize(RouteContext context) => transport = TransportNodeHelpers.RequireTransport(context);
        public void Invoke() => transport.Nudge(1d / 32d);
    }
    [Serializable]
    public sealed class TransportNudgeBackwardTriggerOutput : RouteNode, ITriggerOutput
    {
        [NonSerialized] private IMusicalTransport transport;
        public void Initialize(RouteContext context) => transport = TransportNodeHelpers.RequireTransport(context);
        public void Invoke() => transport.Nudge(-1d / 32d);
    }
    [Serializable]
    public sealed class TransportBpmFloatOutput : RouteNode, IValueOutput<float>
    {
        [NonSerialized] private IMusicalTransport transport;
        public void Initialize(RouteContext context) => transport = TransportNodeHelpers.RequireTransport(context);
        public void Write(float value) => transport.SetBaseBpm(value);
    }
    [Serializable]
    public sealed class TransportMultiplierFloatOutput : RouteNode, IValueOutput<float>
    {
        [NonSerialized] private IMusicalTransport transport;
        public void Initialize(RouteContext context) => transport = TransportNodeHelpers.RequireTransport(context);
        public void Write(float value) => transport.SetMultiplier(value);
    }
}
