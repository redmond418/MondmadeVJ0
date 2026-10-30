using System;
using R3;
using UnityEngine;

namespace MondmadeVJ0
{
    public enum ModifierClock { Unscaled, Scaled }
    public enum WaveShape { Sine, Triangle, Saw, ReverseSaw, Square }

    internal static class ModifierStream
    {
        public static Observable<TOut> Map<TIn, TOut>(Observable<TIn> source, RouteContext context, RouteNode node, Func<TIn, TOut> map, Func<TOut, bool> valid = null)
        {
            return Observable.Create<TOut>(observer =>
            {
                var hasValid = false;
                var last = default(TOut);
                return SourceStream.Subscribe(source, value =>
                {
                    try
                    {
                        var mapped = map(value);
                        if (valid != null && !valid(mapped)) throw new ArithmeticException("A non-finite value was rejected.");
                        last = mapped;
                        hasValid = true;
                        observer.OnNext(mapped);
                    }
                    catch (Exception exception)
                    {
                        context.Report("INVALID_VALUE", "Modifier rejected an invalid value and retained its last valid output.", node, exception);
                        if (!hasValid)
                        {
                            hasValid = true;
                            last = default;
                            observer.OnNext(last);
                        }
                    }
                }, observer.OnErrorResume);
            });
        }

        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
        public static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        public static bool Finite(Vector4 value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);
        public static bool Finite(Color value) => Finite(value.r) && Finite(value.g) && Finite(value.b) && Finite(value.a);
        public static double Now(RouteContext context, ModifierClock clock) => context.FrameClock == null ? UnityEngine.Time.realtimeSinceStartupAsDouble : clock == ModifierClock.Scaled ? context.FrameClock.Time : context.FrameClock.UnscaledTime;
        public static double Delta(RouteContext context, ModifierClock clock) => context.FrameClock == null ? 0d : clock == ModifierClock.Scaled ? context.FrameClock.DeltaTime : context.FrameClock.UnscaledDeltaTime;
        public static float Repeat(float value, float length) => value - Mathf.Floor(value / length) * length;
        public static float PingPong(float value, float length) => length - Mathf.Abs(Repeat(value, length * 2f) - length);
    }

    [Serializable]
    public abstract class GateValueModifierBase : RouteNode
    {
        [SerializeReference] protected IValueSource<bool> gate;
    }

    [Serializable]
    public abstract class GateValueModifier<T> : GateValueModifierBase, IValueModifier<T>
    {
        public Observable<T> Apply(Observable<T> source, RouteContext context)
        {
            if (gate == null) throw new InvalidOperationException("Gate value modifier requires a Bool source.");
            var gateStream = gate.Build(context);
            return Observable.Create<T>(observer =>
            {
                var subscriptions = new CompositeDisposable();
                var hasValue = false;
                var latestValue = default(T);
                var gateOpen = false;

                subscriptions.Add(SourceStream.Subscribe(source, value =>
                {
                    latestValue = value;
                    hasValue = true;
                    if (gateOpen) observer.OnNext(value);
                }, observer.OnErrorResume));

                subscriptions.Add(SourceStream.Subscribe(gateStream, value =>
                {
                    var wasOpen = gateOpen;
                    gateOpen = value;
                    if (!wasOpen && gateOpen && hasValue) observer.OnNext(latestValue);
                }, observer.OnErrorResume));

                return subscriptions;
            });
        }
    }

    [Serializable] public sealed class GateFloatModifier : GateValueModifier<float> { }
    [Serializable] public sealed class GateIntModifier : GateValueModifier<int> { }
    [Serializable] public sealed class GateBoolModifier : GateValueModifier<bool> { }
    [Serializable] public sealed class GateVector2Modifier : GateValueModifier<Vector2> { }
    [Serializable] public sealed class GateVector3Modifier : GateValueModifier<Vector3> { }
    [Serializable] public sealed class GateVector4Modifier : GateValueModifier<Vector4> { }
    [Serializable] public sealed class GateColorModifier : GateValueModifier<Color> { }

    [Serializable] public sealed class AddFloatModifier : RouteNode, IValueModifier<float> { [SerializeField] private float amount; public Observable<float> Apply(Observable<float> s, RouteContext c) => ModifierStream.Map(s, c, this, x => x + amount, ModifierStream.Finite); }
    [Serializable] public sealed class MultiplyFloatModifier : RouteNode, IValueModifier<float> { [SerializeField] private float factor = 1f; public Observable<float> Apply(Observable<float> s, RouteContext c) => ModifierStream.Map(s, c, this, x => x * factor, ModifierStream.Finite); }
    [Serializable]
    public sealed class RemapFloatModifier : RouteNode, IValueModifier<float>
    {
        [SerializeField] private float inputMin, inputMax = 1f, outputMin, outputMax = 1f;
        [SerializeField] private bool clampOutput;
        public Observable<float> Apply(Observable<float> s, RouteContext c) { if (inputMin == inputMax) throw new InvalidOperationException("Remap input range cannot be zero."); return ModifierStream.Map(s, c, this, x => { var y = outputMin + (x - inputMin) / (inputMax - inputMin) * (outputMax - outputMin); return clampOutput ? Mathf.Clamp(y, Mathf.Min(outputMin, outputMax), Mathf.Max(outputMin, outputMax)) : y; }, ModifierStream.Finite); }
    }
    [Serializable]
    public sealed class ClampFloatModifier : RouteNode, IValueModifier<float>
    {
        [SerializeField] private float minimum, maximum = 1f;
        public Observable<float> Apply(Observable<float> s, RouteContext c) { if (minimum > maximum) throw new InvalidOperationException("Clamp minimum exceeds maximum."); return ModifierStream.Map(s, c, this, x => Mathf.Clamp(x, minimum, maximum), ModifierStream.Finite); }
    }
    [Serializable] public sealed class Invert01FloatModifier : RouteNode, IValueModifier<float> { public Observable<float> Apply(Observable<float> s, RouteContext c) => ModifierStream.Map(s, c, this, x => 1f - Mathf.Clamp01(x), ModifierStream.Finite); }
    [Serializable] public sealed class AbsFloatModifier : RouteNode, IValueModifier<float> { public Observable<float> Apply(Observable<float> s, RouteContext c) => ModifierStream.Map(s, c, this, Mathf.Abs, ModifierStream.Finite); }
    [Serializable] public sealed class SignFloatModifier : RouteNode, IValueModifier<float> { public Observable<float> Apply(Observable<float> s, RouteContext c) => ModifierStream.Map(s, c, this, Mathf.Sign, ModifierStream.Finite); }
    [Serializable]
    public sealed class PowerFloatModifier : RouteNode, IValueModifier<float>
    {
        [SerializeField] private float exponent = 1f;
        public Observable<float> Apply(Observable<float> s, RouteContext c) => ModifierStream.Map(s, c, this, x => (float)Math.Pow(x, exponent), ModifierStream.Finite);
    }
    [Serializable]
    public sealed class CurveFloatModifier : RouteNode, IValueModifier<float>
    {
        [SerializeField] private AnimationCurve curve = AnimationCurve.Linear(0, 0, 1, 1);
        public Observable<float> Apply(Observable<float> s, RouteContext c) => ModifierStream.Map(s, c, this, x => curve == null ? throw new InvalidOperationException("Curve is missing.") : curve.Evaluate(x), ModifierStream.Finite);
    }
    [Serializable]
    public sealed class DeadZoneFloatModifier : RouteNode, IValueModifier<float>
    {
        [SerializeField, Range(0f, 1f)] private float threshold = 0.05f;
        public Observable<float> Apply(Observable<float> s, RouteContext c) { if (threshold < 0f || threshold >= 1f || !ModifierStream.Finite(threshold)) throw new InvalidOperationException("Dead-zone threshold must be in [0, 1)."); return ModifierStream.Map(s, c, this, x => { x = Mathf.Clamp01(x); return x <= threshold ? 0f : (x - threshold) / (1f - threshold); }, ModifierStream.Finite); }
    }
    [Serializable]
    public sealed class QuantizeFloatModifier : RouteNode, IValueModifier<float>
    {
        [SerializeField, Min(2)] private int steps = 8;
        public Observable<float> Apply(Observable<float> s, RouteContext c) { if (steps < 2) throw new InvalidOperationException("Quantize requires at least two steps."); return ModifierStream.Map(s, c, this, x => Mathf.Round(Mathf.Clamp01(x) * (steps - 1)) / (steps - 1f), ModifierStream.Finite); }
    }
    [Serializable] public sealed class FloorFloatModifier : RouteNode, IValueModifier<float> { public Observable<float> Apply(Observable<float> s, RouteContext c) => ModifierStream.Map(s, c, this, Mathf.Floor, ModifierStream.Finite); }
    [Serializable] public sealed class CeilFloatModifier : RouteNode, IValueModifier<float> { public Observable<float> Apply(Observable<float> s, RouteContext c) => ModifierStream.Map(s, c, this, Mathf.Ceil, ModifierStream.Finite); }
    [Serializable] public sealed class RoundFloatModifier : RouteNode, IValueModifier<float> { public Observable<float> Apply(Observable<float> s, RouteContext c) => ModifierStream.Map(s, c, this, x => (float)Math.Round(x, MidpointRounding.ToEven), ModifierStream.Finite); }
    [Serializable]
    public sealed class RepeatFloatModifier : RouteNode, IValueModifier<float>
    {
        [SerializeField] private float minimum, maximum = 1f;
        public Observable<float> Apply(Observable<float> s, RouteContext c) { var length = maximum - minimum; if (!(length > 0f) || !ModifierStream.Finite(length)) throw new InvalidOperationException("Repeat range must be finite and positive."); return ModifierStream.Map(s, c, this, x => minimum + ModifierStream.Repeat(x - minimum, length), ModifierStream.Finite); }
    }
    [Serializable]
    public sealed class PingPongFloatModifier : RouteNode, IValueModifier<float>
    {
        [SerializeField] private float minimum, maximum = 1f;
        public Observable<float> Apply(Observable<float> s, RouteContext c) { var length = maximum - minimum; if (!(length > 0f) || !ModifierStream.Finite(length)) throw new InvalidOperationException("PingPong range must be finite and positive."); return ModifierStream.Map(s, c, this, x => minimum + ModifierStream.PingPong(x - minimum, length), ModifierStream.Finite); }
    }

    internal sealed class TemporalFloatState { public float Current, Target, Velocity; public bool Initialized; }
    internal sealed class IntegratedFloatState { public float Current, Velocity; public bool Initialized; }

    [Serializable]
    public sealed class IntegrateFloatModifier : RouteNode, IValueModifier<float>
    {
        [SerializeField] private float initialValue;
        [SerializeField] private ModifierClock clock;

        public Observable<float> Apply(Observable<float> source, RouteContext context)
        {
            if (context.FrameClock == null) throw new InvalidOperationException("Time-based modifiers require FrameClock.");
            if (!ModifierStream.Finite(initialValue)) throw new InvalidOperationException("Integral initial value must be finite.");
            var state = context.GetNodeState(this, "integral", () => new IntegratedFloatState { Current = initialValue });
            return Observable.Create<float>(observer =>
            {
                var disposables = new CompositeDisposable();
                var emittedCurrent = false;
                disposables.Add(SourceStream.Subscribe(source, value =>
                {
                    if (!ModifierStream.Finite(value))
                    {
                        context.Report("INVALID_VALUE", "Integral modifier ignored a non-finite velocity input.", this);
                        return;
                    }

                    state.Velocity = value;
                    if (!emittedCurrent)
                    {
                        state.Initialized = true;
                        emittedCurrent = true;
                        observer.OnNext(state.Current);
                    }
                }, observer.OnErrorResume));

                Action<double, double> update = (_, __) =>
                {
                    if (!state.Initialized) return;
                    var delta = (float)ModifierStream.Delta(context, clock);
                    if (!(delta > 0f) || !ModifierStream.Finite(delta)) return;

                    var next = state.Current + state.Velocity * delta;
                    if (!ModifierStream.Finite(next))
                    {
                        context.Report("INVALID_VALUE", "Integral modifier retained its last finite accumulated value.", this);
                        return;
                    }

                    if (next == state.Current) return;
                    state.Current = next;
                    observer.OnNext(next);
                };
                context.FrameClock.Updated += update;
                disposables.Add(Disposable.Create(() => context.FrameClock.Updated -= update));
                return disposables;
            });
        }
    }

    internal static class TemporalFloat
    {
        public static Observable<float> Build(Observable<float> source, RouteContext context, RouteNode node, ModifierClock clock, Func<TemporalFloatState, float, float, float> step)
        {
            if (context.FrameClock == null) throw new InvalidOperationException("Time-based modifiers require FrameClock.");
            var state = context.GetNodeState(node, "temporal", () => new TemporalFloatState());
            return Observable.Create<float>(observer =>
            {
                var d = new CompositeDisposable();
                d.Add(SourceStream.Subscribe(source, value =>
                {
                    if (!ModifierStream.Finite(value)) { context.Report("INVALID_VALUE", "Temporal modifier ignored a non-finite input.", node); return; }
                    state.Target = value;
                    if (!state.Initialized) { state.Current = state.Target; state.Initialized = true; observer.OnNext(state.Current); }
                }, observer.OnErrorResume));
                Action<double, double> update = (_, __) =>
                {
                    if (!state.Initialized) return;
                    var next = step(state, state.Target, (float)ModifierStream.Delta(context, clock));
                    if (!ModifierStream.Finite(next)) { context.Report("INVALID_VALUE", "Temporal modifier retained its last finite value.", node); return; }
                    if (!Mathf.Approximately(next, state.Current)) { state.Current = next; observer.OnNext(next); }
                };
                context.FrameClock.Updated += update;
                d.Add(Disposable.Create(() => context.FrameClock.Updated -= update));
                return d;
            });
        }
    }
    [Serializable]
    public sealed class SmoothDampFloatModifier : RouteNode, IValueModifier<float>
    {
        [SerializeField, Min(0f)] private float smoothTime = 0.1f;
        [SerializeField] private float maxSpeed = Mathf.Infinity;
        [SerializeField] private ModifierClock clock;
        public Observable<float> Apply(Observable<float> s, RouteContext c) { if (smoothTime < 0f || !ModifierStream.Finite(smoothTime)) throw new InvalidOperationException("Smooth time must be non-negative."); return TemporalFloat.Build(s, c, this, clock, (state, target, dt) => smoothTime == 0f ? state.Current = target : Mathf.SmoothDamp(state.Current, target, ref state.Velocity, smoothTime, maxSpeed, dt)); }
    }
    [Serializable]
    public sealed class ExponentialSmoothFloatModifier : RouteNode, IValueModifier<float>
    {
        [SerializeField, Min(0f)] private float timeConstant = 0.1f;
        [SerializeField] private ModifierClock clock;
        public Observable<float> Apply(Observable<float> s, RouteContext c) { if (timeConstant < 0f || !ModifierStream.Finite(timeConstant)) throw new InvalidOperationException("Time constant must be non-negative."); return TemporalFloat.Build(s, c, this, clock, (state, target, dt) => { if (timeConstant == 0f) return target; var alpha = 1f - Mathf.Exp(-dt / timeConstant); return state.Current + (target - state.Current) * alpha; }); }
    }
    [Serializable]
    public sealed class AttackReleaseFloatModifier : RouteNode, IValueModifier<float>
    {
        [SerializeField, Min(0f)] private float attack = 0.03f, release = 0.4f;
        [SerializeField] private ModifierClock clock;
        public Observable<float> Apply(Observable<float> s, RouteContext c) { if (attack < 0f || release < 0f || !ModifierStream.Finite(attack + release)) throw new InvalidOperationException("Attack and release must be non-negative."); return TemporalFloat.Build(s, c, this, clock, (state, target, dt) => { var tau = target > state.Current ? attack : release; return tau == 0f ? target : state.Current + (target - state.Current) * (1f - Mathf.Exp(-dt / tau)); }); }
    }
    [Serializable]
    public sealed class SlewRateFloatModifier : RouteNode, IValueModifier<float>
    {
        [SerializeField, Min(0f)] private float unitsPerSecond = 1f;
        [SerializeField] private ModifierClock clock;
        public Observable<float> Apply(Observable<float> s, RouteContext c) { if (unitsPerSecond < 0f || !ModifierStream.Finite(unitsPerSecond)) throw new InvalidOperationException("Slew rate must be non-negative."); return TemporalFloat.Build(s, c, this, clock, (state, target, dt) => Mathf.MoveTowards(state.Current, target, unitsPerSecond * dt)); }
    }
    [Serializable]
    public sealed class WaveShapeFloatModifier : RouteNode, IValueModifier<float>
    {
        [SerializeField] private WaveShape shape;
        public Observable<float> Apply(Observable<float> s, RouteContext c) => ModifierStream.Map(s, c, this, x => { var p = ModifierStream.Repeat(x, 1f); switch (shape) { case WaveShape.Sine: return 0.5f + 0.5f * Mathf.Sin(p * Mathf.PI * 2f); case WaveShape.Triangle: return 1f - Mathf.Abs(2f * p - 1f); case WaveShape.Saw: return p; case WaveShape.ReverseSaw: return 1f - p; default: return p < 0.5f ? 1f : 0f; } }, ModifierStream.Finite);
    }

    [Serializable] public sealed class AddIntModifier : RouteNode, IValueModifier<int> { [SerializeField] private int amount; public Observable<int> Apply(Observable<int> s, RouteContext c) => s.Select(x => SourceStream.SaturateInt((long)x + amount)); }
    [Serializable] public sealed class MultiplyIntModifier : RouteNode, IValueModifier<int> { [SerializeField] private int factor = 1; public Observable<int> Apply(Observable<int> s, RouteContext c) => s.Select(x => SourceStream.SaturateInt((long)x * factor)); }
    [Serializable]
    public sealed class ClampIntModifier : RouteNode, IValueModifier<int>
    {
        [SerializeField] private int minimum, maximum = 100;
        public Observable<int> Apply(Observable<int> s, RouteContext c) { if (minimum > maximum) throw new InvalidOperationException("Clamp minimum exceeds maximum."); return s.Select(x => Mathf.Clamp(x, minimum, maximum)); }
    }
    [Serializable]
    public sealed class WrapIntModifier : RouteNode, IValueModifier<int>
    {
        [SerializeField] private int minimum, maximum = 7;
        public Observable<int> Apply(Observable<int> s, RouteContext c) { if (minimum > maximum) throw new InvalidOperationException("Wrap minimum exceeds maximum."); var width = (long)maximum - minimum + 1; return s.Select(x => minimum + (int)(((x - (long)minimum) % width + width) % width)); }
    }
    [Serializable]
    public sealed class ModuloIntModifier : RouteNode, IValueModifier<int>
    {
        [SerializeField, Min(1)] private int modulus = 1;
        public Observable<int> Apply(Observable<int> s, RouteContext c) { if (modulus <= 0) throw new InvalidOperationException("Modulo must be positive."); return s.Select(x => SourceStream.PositiveModulo(x, modulus)); }
    }
    [Serializable]
    public sealed class LookupIntModifier : RouteNode, IValueModifier<int>
    {
        [SerializeField] private int[] values = { 0, 1 };
        public Observable<int> Apply(Observable<int> s, RouteContext c) { if (values == null || values.Length == 0) throw new InvalidOperationException("Lookup cannot be empty."); return s.Select(x => values[Mathf.Clamp(x, 0, values.Length - 1)]); }
    }
    [Serializable] public sealed class AbsIntModifier : RouteNode, IValueModifier<int> { public Observable<int> Apply(Observable<int> s, RouteContext c) => s.Select(x => x == int.MinValue ? int.MaxValue : Math.Abs(x)); }
    [Serializable] public sealed class NotBoolModifier : RouteNode, IValueModifier<bool> { public Observable<bool> Apply(Observable<bool> s, RouteContext c) => s.Select(x => !x); }

    [Serializable]
    public abstract class VectorValueModifier : RouteNode
    {
        [SerializeField] protected bool affectX = true, affectY = true, affectZ = true, affectW = true;
        protected Vector4 Mask(Vector4 v) => new Vector4(affectX ? v.x : 0f, affectY ? v.y : 0f, affectZ ? v.z : 0f, affectW ? v.w : 0f);
        protected abstract Vector4 To4(object v);
        protected abstract object From4(Vector4 v);
    }

    [Serializable]
    public abstract class VectorModifier<T> : VectorValueModifier, IValueModifier<T>
    {
        protected abstract Vector4 ConvertIn(T value);
        protected abstract T ConvertOut(Vector4 value);
        public abstract Observable<T> Apply(Observable<T> source, RouteContext context);
        protected Observable<T> Map(Observable<T> s, RouteContext c, Func<Vector4, Vector4> f) => ModifierStream.Map<T, T>(s, c, this, x => ConvertOut(f(ConvertIn(x))), value => { var v = ConvertIn(value); return ModifierStream.Finite(v); });
        protected override Vector4 To4(object v) => ConvertIn((T)v);
        protected override object From4(Vector4 v) => ConvertOut(v);
    }

    internal sealed class IntegratedVectorState { public Vector4 Current, Velocity; public bool Initialized; }

    [Serializable]
    public abstract class IntegrateVectorModifier<T> : VectorModifier<T>
    {
        [SerializeField] private ModifierClock clock;
        protected abstract Vector4 InitialValue { get; }
        protected abstract Vector4 ToVector(T value);
        protected abstract T FromVector(Vector4 value);

        protected override Vector4 ConvertIn(T value) => ToVector(value);
        protected override T ConvertOut(Vector4 value) => FromVector(value);

        public override Observable<T> Apply(Observable<T> source, RouteContext context)
        {
            if (context.FrameClock == null) throw new InvalidOperationException("Time-based modifiers require FrameClock.");
            if (!ModifierStream.Finite(InitialValue)) throw new InvalidOperationException("Vector integral initial value must be finite.");
            var state = context.GetNodeState(this, "integral-vector", () => new IntegratedVectorState { Current = InitialValue });
            return Observable.Create<T>(observer =>
            {
                var disposables = new CompositeDisposable();
                var emittedCurrent = false;
                disposables.Add(SourceStream.Subscribe(source, value =>
                {
                    var velocity = ConvertIn(value);
                    if (!ModifierStream.Finite(velocity))
                    {
                        context.Report("INVALID_VALUE", "Vector integral modifier ignored a non-finite velocity input.", this);
                        return;
                    }

                    state.Velocity = Mask(velocity);
                    if (!emittedCurrent)
                    {
                        state.Initialized = true;
                        emittedCurrent = true;
                        observer.OnNext(ConvertOut(state.Current));
                    }
                }, observer.OnErrorResume));

                Action<double, double> update = (_, __) =>
                {
                    if (!state.Initialized) return;
                    var delta = (float)ModifierStream.Delta(context, clock);
                    if (!(delta > 0f) || !ModifierStream.Finite(delta)) return;

                    var next = state.Current + state.Velocity * delta;
                    if (!ModifierStream.Finite(next))
                    {
                        context.Report("INVALID_VALUE", "Vector integral modifier retained its last finite accumulated value.", this);
                        return;
                    }

                    if (next.x == state.Current.x && next.y == state.Current.y && next.z == state.Current.z && next.w == state.Current.w) return;
                    state.Current = next;
                    observer.OnNext(ConvertOut(next));
                };
                context.FrameClock.Updated += update;
                disposables.Add(Disposable.Create(() => context.FrameClock.Updated -= update));
                return disposables;
            });
        }
    }

    [Serializable]
    public sealed class IntegrateVector2Modifier : IntegrateVectorModifier<Vector2>
    {
        [SerializeField] private Vector2 initialValue;
        protected override Vector4 InitialValue => new Vector4(initialValue.x, initialValue.y, 0f, 0f);
        protected override Vector4 ToVector(Vector2 value) => new Vector4(value.x, value.y, 0f, 0f);
        protected override Vector2 FromVector(Vector4 value) => new Vector2(value.x, value.y);
    }

    [Serializable]
    public sealed class IntegrateVector3Modifier : IntegrateVectorModifier<Vector3>
    {
        [SerializeField] private Vector3 initialValue;
        protected override Vector4 InitialValue => new Vector4(initialValue.x, initialValue.y, initialValue.z, 0f);
        protected override Vector4 ToVector(Vector3 value) => new Vector4(value.x, value.y, value.z, 0f);
        protected override Vector3 FromVector(Vector4 value) => new Vector3(value.x, value.y, value.z);
    }

    [Serializable]
    public sealed class IntegrateVector4Modifier : IntegrateVectorModifier<Vector4>
    {
        [SerializeField] private Vector4 initialValue;
        protected override Vector4 InitialValue => initialValue;
        protected override Vector4 ToVector(Vector4 value) => value;
        protected override Vector4 FromVector(Vector4 value) => value;
    }

    [Serializable]
    public abstract class VectorAddModifier<T> : VectorModifier<T>
    {
        [SerializeField] protected Vector4 amount;
        protected override Vector4 ConvertIn(T value) => Get(value);
        protected override T ConvertOut(Vector4 value) => Put(value);
        protected abstract Vector4 Get(T value); protected abstract T Put(Vector4 value);
        public override Observable<T> Apply(Observable<T> s, RouteContext c) => Map(s, c, v => v + Mask(amount));
    }
    [Serializable] public sealed class AddVector2Modifier : VectorAddModifier<Vector2> { protected override Vector4 Get(Vector2 v) => new Vector4(v.x,v.y,0,0); protected override Vector2 Put(Vector4 v) => new Vector2(v.x,v.y); }
    [Serializable] public sealed class AddVector3Modifier : VectorAddModifier<Vector3> { protected override Vector4 Get(Vector3 v) => new Vector4(v.x,v.y,v.z,0); protected override Vector3 Put(Vector4 v) => new Vector3(v.x,v.y,v.z); }
    [Serializable] public sealed class AddVector4Modifier : VectorAddModifier<Vector4> { protected override Vector4 Get(Vector4 v) => v; protected override Vector4 Put(Vector4 v) => v; }
    [Serializable]
    public abstract class VectorScaleModifier<T> : VectorModifier<T>
    {
        [SerializeField] protected float scalar = 1f; [SerializeField] protected Vector4 factors = Vector4.one;
        protected override Vector4 ConvertIn(T value) => Get(value); protected override T ConvertOut(Vector4 value) => Put(value);
        protected abstract Vector4 Get(T value); protected abstract T Put(Vector4 value);
        public override Observable<T> Apply(Observable<T> s, RouteContext c) => Map(s,c,v => { var f=Mask(factors); return new Vector4(v.x*(affectX?scalar*f.x:1f),v.y*(affectY?scalar*f.y:1f),v.z*(affectZ?scalar*f.z:1f),v.w*(affectW?scalar*f.w:1f)); });
    }
    [Serializable] public sealed class MultiplyVector2Modifier : VectorScaleModifier<Vector2> { protected override Vector4 Get(Vector2 v)=>new Vector4(v.x,v.y,0,0); protected override Vector2 Put(Vector4 v)=>new Vector2(v.x,v.y); }
    [Serializable] public sealed class MultiplyVector3Modifier : VectorScaleModifier<Vector3> { protected override Vector4 Get(Vector3 v)=>new Vector4(v.x,v.y,v.z,0); protected override Vector3 Put(Vector4 v)=>new Vector3(v.x,v.y,v.z); }
    [Serializable] public sealed class MultiplyVector4Modifier : VectorScaleModifier<Vector4> { protected override Vector4 Get(Vector4 v)=>v; protected override Vector4 Put(Vector4 v)=>v; }

    [Serializable]
    public abstract class SmoothDampVectorModifier<T> : VectorModifier<T>
    {
        [SerializeField, Min(0f)] private float smoothTime = 0.15f;
        [SerializeField, Min(0f)] private float maxSpeed = Mathf.Infinity;
        [SerializeField] private ModifierClock clock = ModifierClock.Unscaled;
        private sealed class State { public Vector4 Current, Target, Velocity; public bool Initialized; }
        protected abstract Vector4 Get(T value);
        protected abstract T Put(Vector4 value);
        protected override Vector4 ConvertIn(T value) => Get(value);
        protected override T ConvertOut(Vector4 value) => Put(value);

        public override Observable<T> Apply(Observable<T> source, RouteContext context)
        {
            if (smoothTime < 0f || !ModifierStream.Finite(smoothTime)) throw new InvalidOperationException("Vector SmoothDamp time must be finite and non-negative.");
            if (maxSpeed < 0f || float.IsNaN(maxSpeed)) throw new InvalidOperationException("Vector SmoothDamp max speed must be non-negative.");
            if (context.FrameClock == null) throw new InvalidOperationException("Vector SmoothDamp requires FrameClock.");
            var state = context.GetNodeState(this, "smooth-damp-vector", () => new State());
            return Observable.Create<T>(observer =>
            {
                var subscriptions = new CompositeDisposable();
                subscriptions.Add(SourceStream.Subscribe(source, value =>
                {
                    var target = Get(value);
                    if (!ModifierStream.Finite(target)) { context.Report("INVALID_VALUE", "Vector SmoothDamp ignored a non-finite input.", this); return; }
                    state.Target = target;
                    if (!state.Initialized)
                    {
                        state.Current = target;
                        state.Velocity = Vector4.zero;
                        state.Initialized = true;
                        observer.OnNext(value);
                    }
                }, observer.OnErrorResume));

                Action<double, double> update = (_, __) =>
                {
                    if (!state.Initialized) return;
                    var delta = (float)ModifierStream.Delta(context, clock);
                    if (!(delta > 0f) || !ModifierStream.Finite(delta)) return;
                    for (var i = 0; i < 4; i++)
                    {
                        var affected = i == 0 ? affectX : i == 1 ? affectY : i == 2 ? affectZ : affectW;
                        var target = state.Target[i];
                        if (affected)
                        {
                            var velocity = state.Velocity[i];
                            state.Current[i] = Mathf.SmoothDamp(state.Current[i], target, ref velocity, smoothTime, maxSpeed, delta);
                            state.Velocity[i] = velocity;
                        }
                        else state.Current[i] = target;
                    }
                    observer.OnNext(Put(state.Current));
                };
                context.FrameClock.Updated += update;
                subscriptions.Add(Disposable.Create(() => context.FrameClock.Updated -= update));
                return subscriptions;
            });
        }
    }
    [Serializable] public sealed class SmoothDampVector2Modifier : SmoothDampVectorModifier<Vector2> { protected override Vector4 Get(Vector2 v) => new Vector4(v.x, v.y, 0f, 0f); protected override Vector2 Put(Vector4 v) => new Vector2(v.x, v.y); }
    [Serializable] public sealed class SmoothDampVector3Modifier : SmoothDampVectorModifier<Vector3> { protected override Vector4 Get(Vector3 v) => new Vector4(v.x, v.y, v.z, 0f); protected override Vector3 Put(Vector4 v) => new Vector3(v.x, v.y, v.z); }
    [Serializable] public sealed class SmoothDampVector4Modifier : SmoothDampVectorModifier<Vector4> { protected override Vector4 Get(Vector4 v) => v; protected override Vector4 Put(Vector4 v) => v; }
    [Serializable]
    public abstract class NormalizeVectorModifier<T> : VectorModifier<T>
    {
        protected abstract Vector4 Get(T v); protected abstract T Put(Vector4 v);
        protected override Vector4 ConvertIn(T value)=>Get(value); protected override T ConvertOut(Vector4 value)=>Put(value);
        public override Observable<T> Apply(Observable<T> s, RouteContext c)=>Map(s,c,v=>{ var m=v.magnitude; return m>0f?v/m:Vector4.zero; });
    }
    [Serializable] public sealed class NormalizeVector2Modifier : NormalizeVectorModifier<Vector2> { protected override Vector4 Get(Vector2 v)=>new Vector4(v.x,v.y,0,0); protected override Vector2 Put(Vector4 v){var x=new Vector2(v.x,v.y); return x.sqrMagnitude>0?x.normalized:Vector2.zero;} }
    [Serializable] public sealed class NormalizeVector3Modifier : NormalizeVectorModifier<Vector3> { protected override Vector4 Get(Vector3 v)=>new Vector4(v.x,v.y,v.z,0); protected override Vector3 Put(Vector4 v){var x=new Vector3(v.x,v.y,v.z); return x.sqrMagnitude>0?x.normalized:Vector3.zero;} }
    [Serializable] public sealed class NormalizeVector4Modifier : NormalizeVectorModifier<Vector4> { protected override Vector4 Get(Vector4 v)=>v; protected override Vector4 Put(Vector4 v)=>v.sqrMagnitude>0?v.normalized:Vector4.zero; }
    [Serializable]
    public abstract class AbsoluteVectorModifier<T> : VectorModifier<T>
    {
        protected abstract Vector4 Get(T v); protected abstract T Put(Vector4 v); protected override Vector4 ConvertIn(T value)=>Get(value); protected override T ConvertOut(Vector4 value)=>Put(value);
        public override Observable<T> Apply(Observable<T> s, RouteContext c)=>Map(s,c,v=>new Vector4(affectX?Mathf.Abs(v.x):v.x,affectY?Mathf.Abs(v.y):v.y,affectZ?Mathf.Abs(v.z):v.z,affectW?Mathf.Abs(v.w):v.w));
    }
    [Serializable] public sealed class AbsoluteVector2Modifier : AbsoluteVectorModifier<Vector2> { protected override Vector4 Get(Vector2 v)=>new Vector4(v.x,v.y,0,0); protected override Vector2 Put(Vector4 v)=>new Vector2(v.x,v.y); }
    [Serializable] public sealed class AbsoluteVector3Modifier : AbsoluteVectorModifier<Vector3> { protected override Vector4 Get(Vector3 v)=>new Vector4(v.x,v.y,v.z,0); protected override Vector3 Put(Vector4 v)=>new Vector3(v.x,v.y,v.z); }
    [Serializable] public sealed class AbsoluteVector4Modifier : AbsoluteVectorModifier<Vector4> { protected override Vector4 Get(Vector4 v)=>v; protected override Vector4 Put(Vector4 v)=>v; }
    [Serializable]
    public abstract class ClampMagnitudeVectorModifier<T> : VectorModifier<T>
    {
        [SerializeField, Min(0f)] private float maximum = 1f; protected abstract Vector4 Get(T v); protected abstract T Put(Vector4 v); protected override Vector4 ConvertIn(T value)=>Get(value); protected override T ConvertOut(Vector4 value)=>Put(value);
        public override Observable<T> Apply(Observable<T> s, RouteContext c) { if(maximum<0||!ModifierStream.Finite(maximum)) throw new InvalidOperationException("Maximum magnitude must be non-negative."); return Map(s,c,v=>v.magnitude>maximum&&v.magnitude>0?v*(maximum/v.magnitude):v); }
    }
    [Serializable] public sealed class ClampMagnitudeVector2Modifier : ClampMagnitudeVectorModifier<Vector2> { protected override Vector4 Get(Vector2 v)=>new Vector4(v.x,v.y,0,0); protected override Vector2 Put(Vector4 v)=>new Vector2(v.x,v.y); }
    [Serializable] public sealed class ClampMagnitudeVector3Modifier : ClampMagnitudeVectorModifier<Vector3> { protected override Vector4 Get(Vector3 v)=>new Vector4(v.x,v.y,v.z,0); protected override Vector3 Put(Vector4 v)=>new Vector3(v.x,v.y,v.z); }
    [Serializable] public sealed class ClampMagnitudeVector4Modifier : ClampMagnitudeVectorModifier<Vector4> { protected override Vector4 Get(Vector4 v)=>v; protected override Vector4 Put(Vector4 v)=>v; }
    [Serializable]
    public abstract class RemapVectorModifier<T> : VectorModifier<T>
    {
        [SerializeField] private Vector4 inputMin, inputMax=Vector4.one, outputMin, outputMax=Vector4.one; [SerializeField] private bool clampOutput;
        protected abstract Vector4 Get(T v); protected abstract T Put(Vector4 v); protected override Vector4 ConvertIn(T value)=>Get(value); protected override T ConvertOut(Vector4 value)=>Put(value);
        public override Observable<T> Apply(Observable<T> s, RouteContext c) { var a=Mask(inputMin); var b=Mask(inputMax); if((affectX&&a.x==b.x)||(affectY&&a.y==b.y)||(affectZ&&a.z==b.z)||(affectW&&a.w==b.w)) throw new InvalidOperationException("Remap input range cannot be zero."); return Map(s,c,v=>{var o=v; for(var i=0;i<4;i++){if(!(i==0?affectX:i==1?affectY:i==2?affectZ:affectW)) continue; var den=b[i]-a[i]; o[i]=outputMin[i]+(v[i]-a[i])/den*(outputMax[i]-outputMin[i]); if(clampOutput)o[i]=Mathf.Clamp(o[i],Mathf.Min(outputMin[i],outputMax[i]),Mathf.Max(outputMin[i],outputMax[i]));} return o;}); }
    }
    [Serializable] public sealed class RemapVector2Modifier : RemapVectorModifier<Vector2> { protected override Vector4 Get(Vector2 v)=>new Vector4(v.x,v.y,0,0); protected override Vector2 Put(Vector4 v)=>new Vector2(v.x,v.y); }
    [Serializable] public sealed class RemapVector3Modifier : RemapVectorModifier<Vector3> { protected override Vector4 Get(Vector3 v)=>new Vector4(v.x,v.y,v.z,0); protected override Vector3 Put(Vector4 v)=>new Vector3(v.x,v.y,v.z); }
    [Serializable] public sealed class RemapVector4Modifier : RemapVectorModifier<Vector4> { protected override Vector4 Get(Vector4 v)=>v; protected override Vector4 Put(Vector4 v)=>v; }

    [Serializable]
    public sealed class MultiplyColorModifier : RouteNode, IValueModifier<Color>
    {
        [SerializeField] private Color multiplier = Color.white;
        public Observable<Color> Apply(Observable<Color> s, RouteContext c)=>ModifierStream.Map(s,c,this,x=>new Color(x.r*multiplier.r,x.g*multiplier.g,x.b*multiplier.b,x.a*multiplier.a),ModifierStream.Finite);
    }
    [Serializable] public sealed class IntensityColorModifier : RouteNode, IValueModifier<Color> { [SerializeField] private float intensity=1f; public Observable<Color> Apply(Observable<Color>s,RouteContext c)=>ModifierStream.Map(s,c,this,x=>new Color(x.r*intensity,x.g*intensity,x.b*intensity,x.a),ModifierStream.Finite); }
    [Serializable] public sealed class AlphaColorModifier : RouteNode, IValueModifier<Color> { [SerializeField] private float alpha=1f; [SerializeField] private bool multiply; public Observable<Color> Apply(Observable<Color>s,RouteContext c)=>ModifierStream.Map(s,c,this,x=>{x.a=multiply?x.a*alpha:alpha;return x;},ModifierStream.Finite); }
    [Serializable]
    public sealed class HSVColorModifier : RouteNode, IValueModifier<Color>
    {
        [SerializeField] private float hueOffset, saturationMultiplier=1f, valueMultiplier=1f;
        public Observable<Color> Apply(Observable<Color>s,RouteContext c)=>ModifierStream.Map(s,c,this,x=>{Color.RGBToHSV(x,out var h,out var sat,out var v); var y=Color.HSVToRGB(ModifierStream.Repeat(h+hueOffset,1f),Mathf.Clamp01(sat*saturationMultiplier),Mathf.Max(0f,v*valueMultiplier),true); y.a=x.a; return y;},ModifierStream.Finite);
    }
}
