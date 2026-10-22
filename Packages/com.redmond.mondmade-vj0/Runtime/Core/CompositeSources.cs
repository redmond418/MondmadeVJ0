using System;
using System.Collections.Generic;
using System.Linq;
using R3;
using UnityEngine;

namespace MondmadeVJ0
{
    internal static class SourceStream
    {
        public static IDisposable Subscribe<T>(Observable<T> stream, Action<T> onNext, Action<Exception> onError)
        {
            return stream.Subscribe(onNext, onError, result =>
            {
                if (result.IsFailure) onError(result.Exception);
            });
        }

        public static Observable<T> Merge<T>(IReadOnlyList<Observable<T>> streams)
        {
            return Observable.Create<T>(observer =>
            {
                var subscriptions = new CompositeDisposable();
                for (var i = 0; i < streams.Count; i++)
                    subscriptions.Add(Subscribe(streams[i], observer.OnNext, observer.OnErrorResume));
                return subscriptions;
            });
        }

        public static int SaturateInt(long value)
        {
            if (value > int.MaxValue) return int.MaxValue;
            if (value < int.MinValue) return int.MinValue;
            return (int)value;
        }

        public static int PositiveModulo(int value, int modulus)
        {
            var result = value % modulus;
            return result < 0 ? result + modulus : result;
        }

        public static void ReportInvalid(RouteContext context, RouteNode node, string message)
        {
            context.Report("INVALID_VALUE", message, node);
        }
    }

    public enum FloatBinaryOperation { Add, Subtract, Multiply, Divide, Minimum, Maximum, Modulo, Power }
    public enum IntBinaryOperation { Add, Subtract, Multiply, Divide, Minimum, Maximum, Modulo }
    public enum BoolLogicOperation { And, Or, Xor }
    public enum CompareOperation { Less, LessOrEqual, Equal, NotEqual, GreaterOrEqual, Greater }

    [Serializable]
    public sealed class FloatBinarySource : RouteNode, IValueSource<float>
    {
        [SerializeReference] private IValueSource<float> left;
        [SerializeReference] private IValueSource<float> right;
        [SerializeField] private FloatBinaryOperation operation;

        public Observable<float> Build(RouteContext context)
        {
            if (left == null || right == null) throw new InvalidOperationException("FloatBinarySource requires both inputs.");
            var leftStream = left.Build(context);
            var rightStream = right.Build(context);
            return Observable.Create<float>(observer =>
            {
                var subscriptions = new CompositeDisposable();
                var a = 0f;
                var b = 0f;
                var hasA = false;
                var hasB = false;
                var hasValid = false;
                Action evaluate = () =>
                {
                    if (!hasA || !hasB) return;
                    double result;
                    switch (operation)
                    {
                        case FloatBinaryOperation.Add: result = (double)a + b; break;
                        case FloatBinaryOperation.Subtract: result = (double)a - b; break;
                        case FloatBinaryOperation.Multiply: result = (double)a * b; break;
                        case FloatBinaryOperation.Divide:
                            if (b == 0f) { Invalid("Float division by zero was rejected."); return; }
                            result = (double)a / b;
                            break;
                        case FloatBinaryOperation.Minimum: result = Math.Min(a, b); break;
                        case FloatBinaryOperation.Maximum: result = Math.Max(a, b); break;
                        case FloatBinaryOperation.Modulo:
                            if (b == 0f) { Invalid("Float modulo by zero was rejected."); return; }
                            result = a % b;
                            break;
                        case FloatBinaryOperation.Power: result = Math.Pow(a, b); break;
                        default: throw new ArgumentOutOfRangeException();
                    }
                    if (double.IsNaN(result) || double.IsInfinity(result) || result > float.MaxValue || result < -float.MaxValue)
                    {
                        Invalid("A non-finite float operation result was rejected.");
                        return;
                    }
                    hasValid = true;
                    observer.OnNext((float)result);
                };
                void Invalid(string message)
                {
                    SourceStream.ReportInvalid(context, this, message);
                    if (!hasValid) { hasValid = true; observer.OnNext(0f); }
                }
                subscriptions.Add(SourceStream.Subscribe(leftStream, value => { a = value; hasA = true; evaluate(); }, observer.OnErrorResume));
                subscriptions.Add(SourceStream.Subscribe(rightStream, value => { b = value; hasB = true; evaluate(); }, observer.OnErrorResume));
                return subscriptions;
            });
        }
    }

    [Serializable]
    public sealed class IntBinarySource : RouteNode, IValueSource<int>
    {
        [SerializeReference] private IValueSource<int> left;
        [SerializeReference] private IValueSource<int> right;
        [SerializeField] private IntBinaryOperation operation;

        public Observable<int> Build(RouteContext context)
        {
            if (left == null || right == null) throw new InvalidOperationException("IntBinarySource requires both inputs.");
            var leftStream = left.Build(context);
            var rightStream = right.Build(context);
            return Observable.Create<int>(observer =>
            {
                var subscriptions = new CompositeDisposable();
                var a = 0;
                var b = 0;
                var hasA = false;
                var hasB = false;
                var hasValid = false;
                Action evaluate = () =>
                {
                    if (!hasA || !hasB) return;
                    long result;
                    switch (operation)
                    {
                        case IntBinaryOperation.Add: result = (long)a + b; break;
                        case IntBinaryOperation.Subtract: result = (long)a - b; break;
                        case IntBinaryOperation.Multiply: result = (long)a * b; break;
                        case IntBinaryOperation.Divide:
                            if (b == 0) { Invalid("Int division by zero was rejected."); return; }
                            result = a == int.MinValue && b == -1 ? int.MaxValue : a / b;
                            break;
                        case IntBinaryOperation.Minimum: result = Math.Min(a, b); break;
                        case IntBinaryOperation.Maximum: result = Math.Max(a, b); break;
                        case IntBinaryOperation.Modulo:
                            if (b == 0) { Invalid("Int modulo by zero was rejected."); return; }
                            result = SourceStream.PositiveModulo(a, Math.Abs((long)b) > int.MaxValue ? int.MaxValue : Math.Abs(b));
                            break;
                        default: throw new ArgumentOutOfRangeException();
                    }
                    hasValid = true;
                    observer.OnNext(SourceStream.SaturateInt(result));
                };
                void Invalid(string message)
                {
                    SourceStream.ReportInvalid(context, this, message);
                    if (!hasValid) { hasValid = true; observer.OnNext(0); }
                }
                subscriptions.Add(SourceStream.Subscribe(leftStream, value => { a = value; hasA = true; evaluate(); }, observer.OnErrorResume));
                subscriptions.Add(SourceStream.Subscribe(rightStream, value => { b = value; hasB = true; evaluate(); }, observer.OnErrorResume));
                return subscriptions;
            });
        }
    }

    [Serializable]
    public sealed class BoolLogicSource : RouteNode, IValueSource<bool>
    {
        [SerializeReference] private IValueSource<bool> left;
        [SerializeReference] private IValueSource<bool> right;
        [SerializeField] private BoolLogicOperation operation;
        public Observable<bool> Build(RouteContext context)
        {
            if (left == null || right == null) throw new InvalidOperationException("BoolLogicSource requires both inputs.");
            return left.Build(context).CombineLatest(right.Build(context), (a, b) => operation == BoolLogicOperation.And ? a && b : operation == BoolLogicOperation.Or ? a || b : a ^ b);
        }
    }

    [Serializable]
    public sealed class FloatCompareBoolSource : RouteNode, IValueSource<bool>
    {
        [SerializeReference] private IValueSource<float> left;
        [SerializeReference] private IValueSource<float> right;
        [SerializeField] private CompareOperation operation;
        [SerializeField, Min(0f)] private float epsilon = 0.00001f;
        public Observable<bool> Build(RouteContext context)
        {
            if (left == null || right == null) throw new InvalidOperationException("FloatCompareBoolSource requires both inputs.");
            if (epsilon < 0f || float.IsNaN(epsilon)) throw new InvalidOperationException("Float comparison epsilon must be non-negative.");
            return left.Build(context).CombineLatest(right.Build(context), (a, b) => Compare(a, b));
        }
        private bool Compare(float a, float b)
        {
            var difference = (double)a - b;
            switch (operation)
            {
                case CompareOperation.Less: return difference < -epsilon;
                case CompareOperation.LessOrEqual: return difference <= epsilon;
                case CompareOperation.Equal: return Math.Abs(difference) <= epsilon;
                case CompareOperation.NotEqual: return Math.Abs(difference) > epsilon;
                case CompareOperation.GreaterOrEqual: return difference >= -epsilon;
                default: return difference > epsilon;
            }
        }
    }

    [Serializable]
    public sealed class IntCompareBoolSource : RouteNode, IValueSource<bool>
    {
        [SerializeReference] private IValueSource<int> left;
        [SerializeReference] private IValueSource<int> right;
        [SerializeField] private CompareOperation operation;
        public Observable<bool> Build(RouteContext context)
        {
            if (left == null || right == null) throw new InvalidOperationException("IntCompareBoolSource requires both inputs.");
            return left.Build(context).CombineLatest(right.Build(context), (a, b) => operation == CompareOperation.Less ? a < b : operation == CompareOperation.LessOrEqual ? a <= b : operation == CompareOperation.Equal ? a == b : operation == CompareOperation.NotEqual ? a != b : operation == CompareOperation.GreaterOrEqual ? a >= b : a > b);
        }
    }

    [Serializable]
    public abstract class SelectValueSource<T> : RouteNode, IValueSource<T>
    {
        [SerializeReference] private IValueSource<bool> condition;
        [SerializeReference] private IValueSource<T> whenTrue;
        [SerializeReference] private IValueSource<T> whenFalse;

        public Observable<T> Build(RouteContext context)
        {
            if (condition == null || whenTrue == null || whenFalse == null)
                throw new InvalidOperationException("SelectSource requires Condition, True, and False inputs.");
            var conditionStream = condition.Build(context);
            var trueStream = whenTrue.Build(context);
            var falseStream = whenFalse.Build(context);
            return Observable.Create<T>(observer =>
            {
                var subscriptions = new CompositeDisposable();
                var hasCondition = false;
                var hasTrue = false;
                var hasFalse = false;
                var currentCondition = false;
                var trueValue = default(T);
                var falseValue = default(T);
                var ready = false;
                Action emitIfReady = () =>
                {
                    if (!hasCondition || !hasTrue || !hasFalse) return;
                    var selected = currentCondition ? trueValue : falseValue;
                    if (!ready)
                    {
                        ready = true;
                        observer.OnNext(selected);
                    }
                };
                subscriptions.Add(SourceStream.Subscribe(conditionStream, value =>
                {
                    var changed = hasCondition && currentCondition != value;
                    currentCondition = value;
                    hasCondition = true;
                    emitIfReady();
                    if (ready && changed) observer.OnNext(currentCondition ? trueValue : falseValue);
                }, observer.OnErrorResume));
                subscriptions.Add(SourceStream.Subscribe(trueStream, value =>
                {
                    var changed = hasTrue && !EqualityComparer<T>.Default.Equals(trueValue, value);
                    trueValue = value;
                    hasTrue = true;
                    emitIfReady();
                    if (ready && currentCondition && changed) observer.OnNext(trueValue);
                }, observer.OnErrorResume));
                subscriptions.Add(SourceStream.Subscribe(falseStream, value =>
                {
                    var changed = hasFalse && !EqualityComparer<T>.Default.Equals(falseValue, value);
                    falseValue = value;
                    hasFalse = true;
                    emitIfReady();
                    if (ready && !currentCondition && changed) observer.OnNext(falseValue);
                }, observer.OnErrorResume));
                return subscriptions;
            });
        }
    }

    [Serializable] public sealed class FloatSelectSource : SelectValueSource<float> { }
    [Serializable] public sealed class IntSelectSource : SelectValueSource<int> { }
    [Serializable] public sealed class BoolSelectSource : SelectValueSource<bool> { }
    [Serializable] public sealed class Vector2SelectSource : SelectValueSource<Vector2> { }
    [Serializable] public sealed class Vector3SelectSource : SelectValueSource<Vector3> { }
    [Serializable] public sealed class Vector4SelectSource : SelectValueSource<Vector4> { }
    [Serializable] public sealed class ColorSelectSource : SelectValueSource<Color> { }

    [Serializable]
    public abstract class MostRecentValueSource<T> : RouteNode, IValueSource<T>
    {
        [SerializeReference] private List<IValueSource<T>> inputs = new List<IValueSource<T>>();
        [SerializeField, Min(0)] private int initialSourceIndex;

        public Observable<T> Build(RouteContext context)
        {
            if (inputs == null || inputs.Count == 0) throw new InvalidOperationException("MostRecentSource requires at least one input.");
            if (initialSourceIndex < 0 || initialSourceIndex >= inputs.Count) throw new InvalidOperationException("initialSourceIndex is outside the input list.");
            var streams = new Observable<T>[inputs.Count];
            for (var i = 0; i < inputs.Count; i++)
                streams[i] = (inputs[i] ?? throw new InvalidOperationException($"MostRecent input {i} is missing.")).Build(context);

            return Observable.Create<T>(observer =>
            {
                var subscriptions = new CompositeDisposable();
                var values = new T[streams.Length];
                var seen = new bool[streams.Length];
                var seenCount = 0;
                var initialized = false;
                var mostRecent = initialSourceIndex;
                for (var i = 0; i < streams.Length; i++)
                {
                    var index = i;
                    subscriptions.Add(SourceStream.Subscribe(streams[index], value =>
                    {
                        if (!seen[index])
                        {
                            seen[index] = true;
                            seenCount++;
                            values[index] = value;
                            if (!initialized && seenCount == streams.Length)
                            {
                                initialized = true;
                                observer.OnNext(values[mostRecent]);
                            }
                            return;
                        }
                        if (EqualityComparer<T>.Default.Equals(values[index], value)) return;
                        values[index] = value;
                        mostRecent = index;
                        if (initialized) observer.OnNext(value);
                    }, observer.OnErrorResume));
                }
                return subscriptions;
            });
        }
    }

    [Serializable] public sealed class MostRecentFloatSource : MostRecentValueSource<float> { }
    [Serializable] public sealed class MostRecentIntSource : MostRecentValueSource<int> { }
    [Serializable] public sealed class MostRecentBoolSource : MostRecentValueSource<bool> { }
    [Serializable] public sealed class MostRecentVector2Source : MostRecentValueSource<Vector2> { }
    [Serializable] public sealed class MostRecentVector3Source : MostRecentValueSource<Vector3> { }
    [Serializable] public sealed class MostRecentVector4Source : MostRecentValueSource<Vector4> { }

    [Serializable]
    public abstract class TriggerListSource : RouteNode
    {
        [SerializeReference] protected List<ITriggerSource> inputs = new List<ITriggerSource>();
        protected Observable<Unit>[] BuildInputs(RouteContext context)
        {
            if (inputs == null || inputs.Count == 0) throw new InvalidOperationException("Trigger composite requires at least one input.");
            var streams = new Observable<Unit>[inputs.Count];
            for (var i = 0; i < inputs.Count; i++)
                streams[i] = (inputs[i] ?? throw new InvalidOperationException($"Trigger input {i} is missing.")).Build(context);
            return streams;
        }
    }

    [Serializable]
    public sealed class MergeTriggerSource : TriggerListSource, ITriggerSource
    {
        public Observable<Unit> Build(RouteContext context) => SourceStream.Merge(BuildInputs(context));
    }

    [Serializable]
    public sealed class AnyTriggerSource : TriggerListSource, ITriggerSource
    {
        public Observable<Unit> Build(RouteContext context)
        {
            var streams = BuildInputs(context);
            if (!context.Services.TryGet<FrameEndScheduler>(out var scheduler))
                throw new InvalidOperationException("AnyTriggerSource requires a FrameEndScheduler.");
            return Observable.Create<Unit>(observer =>
            {
                var subscriptions = new CompositeDisposable();
                var key = new object();
                var disposed = false;
                for (var i = 0; i < streams.Length; i++)
                    subscriptions.Add(SourceStream.Subscribe(streams[i], _ =>
                    {
                        scheduler.Schedule(key, () => { if (!disposed) observer.OnNext(Unit.Default); });
                    }, observer.OnErrorResume));
                subscriptions.Add(Disposable.Create(() => disposed = true));
                return subscriptions;
            });
        }
    }

    [Serializable]
    public sealed class AllTriggerSource : TriggerListSource, ITriggerSource
    {
        [SerializeField, Min(0f)] private float windowSeconds = 0.1f;
        public Observable<Unit> Build(RouteContext context)
        {
            var streams = BuildInputs(context);
            if (windowSeconds < 0f || float.IsNaN(windowSeconds)) throw new InvalidOperationException("AllTrigger windowSeconds must be non-negative.");
            return Observable.Create<Unit>(observer =>
            {
                var subscriptions = new CompositeDisposable();
                var flags = new bool[streams.Length];
                var firstTime = 0d;
                var active = false;
                for (var i = 0; i < streams.Length; i++)
                {
                    var index = i;
                    subscriptions.Add(SourceStream.Subscribe(streams[index], _ =>
                    {
                        var now = context.FrameClock?.UnscaledTime ?? Time.realtimeSinceStartupAsDouble;
                        if (!active || now - firstTime > windowSeconds)
                        {
                            Array.Clear(flags, 0, flags.Length);
                            active = true;
                            firstTime = now;
                        }
                        flags[index] = true;
                        for (var item = 0; item < flags.Length; item++)
                            if (!flags[item]) return;
                        observer.OnNext(Unit.Default);
                        Array.Clear(flags, 0, flags.Length);
                        active = false;
                    }, observer.OnErrorResume));
                }
                return subscriptions;
            });
        }
    }
}
