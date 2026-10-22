using System;
using R3;
using UnityEngine;

namespace MondmadeVJ0
{
    internal sealed class StatefulValue<T> : IDisposable
    {
        public readonly BehaviorSubject<T> Stream;
        public T Value { get; private set; }
        public StatefulValue(T initialValue)
        {
            Value = initialValue;
            Stream = new BehaviorSubject<T>(initialValue);
        }
        public void Set(T value)
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(Value, value)) return;
            Value = value;
            Stream.OnNext(value);
        }
        public void Dispose() => Stream.Dispose();
    }

    internal sealed class DeterministicRandomState
    {
        private ulong state;
        public DeterministicRandomState(string nodeId)
        {
            state = 14695981039346656037UL;
            for (var i = 0; i < nodeId.Length; i++)
            {
                state ^= nodeId[i];
                state *= 1099511628211UL;
            }
            if (state == 0) state = 0x9E3779B97F4A7C15UL;
        }
        public uint NextUInt32()
        {
            state ^= state >> 12;
            state ^= state << 25;
            state ^= state >> 27;
            return (uint)((state * 2685821657736338717UL) >> 32);
        }
        public float NextUnit() => (NextUInt32() >> 8) * (1f / 16777216f);
        public double NextUnitDouble() => ((ulong)NextUInt32() << 21 | (NextUInt32() & 0x1fffffUL)) * (1d / 9007199254740992d);
    }

    internal static class StatefulSourceHelpers
    {
        public static Observable<T> ObserveState<T>(StatefulValue<T> state, Observable<Unit> trigger, Action onTrigger)
        {
            return Observable.Create<T>(observer =>
            {
                var subscriptions = new CompositeDisposable();
                subscriptions.Add(SourceStream.Subscribe(state.Stream, observer.OnNext, observer.OnErrorResume));
                subscriptions.Add(SourceStream.Subscribe(trigger, _ => onTrigger(), observer.OnErrorResume));
                return subscriptions;
            });
        }

        public static double Time(RouteContext context, EnvelopeTimeUnit unit)
        {
            if (unit == EnvelopeTimeUnit.Seconds)
                return context.FrameClock?.UnscaledTime ?? UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (context.Transport == null)
                throw new InvalidOperationException("Beat-based source requires a Transport service.");
            return context.Transport.BeatPosition;
        }

        public static int WrapInclusive(int value, int min, int max)
        {
            if (min > max) throw new InvalidOperationException("Inclusive range minimum exceeds maximum.");
            var width = (long)max - min + 1L;
            var offset = ((long)value - min) % width;
            if (offset < 0) offset += width;
            return (int)(min + offset);
        }
    }

    [Serializable]
    public sealed class ToggleBoolSource : RouteNode, IValueSource<bool>
    {
        [SerializeReference] private ITriggerSource trigger;
        [SerializeField] private bool initialValue;
        public Observable<bool> Build(RouteContext context)
        {
            if (trigger == null) throw new InvalidOperationException("ToggleBoolSource trigger is missing.");
            var state = context.GetNodeState(this, () => new StatefulValue<bool>(initialValue));
            return StatefulSourceHelpers.ObserveState(state, trigger.Build(context), () => state.Set(!state.Value));
        }
    }

    [Serializable]
    public sealed class SetResetBoolSource : RouteNode, IValueSource<bool>
    {
        [SerializeReference] private ITriggerSource setTrigger;
        [SerializeReference] private ITriggerSource resetTrigger;
        [SerializeField] private bool initialValue;
        public Observable<bool> Build(RouteContext context)
        {
            if (setTrigger == null || resetTrigger == null) throw new InvalidOperationException("SetResetBoolSource requires Set and Reset triggers.");
            var state = context.GetNodeState(this, () => new StatefulValue<bool>(initialValue));
            var set = setTrigger.Build(context);
            var reset = resetTrigger.Build(context);
            return Observable.Create<bool>(observer =>
            {
                var subscriptions = new CompositeDisposable();
                subscriptions.Add(SourceStream.Subscribe(state.Stream, observer.OnNext, observer.OnErrorResume));
                subscriptions.Add(SourceStream.Subscribe(set, _ => state.Set(true), observer.OnErrorResume));
                subscriptions.Add(SourceStream.Subscribe(reset, _ => state.Set(false), observer.OnErrorResume));
                return subscriptions;
            });
        }
    }

    [Serializable]
    public sealed class PulseBoolSource : RouteNode, IValueSource<bool>
    {
        [SerializeReference] private ITriggerSource trigger;
        [SerializeField, Min(0f)] private float durationSeconds = 0.1f;
        [SerializeField] private bool initialValue;
        public Observable<bool> Build(RouteContext context)
        {
            if (trigger == null) throw new InvalidOperationException("PulseBoolSource trigger is missing.");
            if (durationSeconds < 0f || float.IsNaN(durationSeconds)) throw new InvalidOperationException("Pulse duration must be non-negative.");
            var state = context.GetNodeState(this, () => new StatefulValue<bool>(initialValue));
            var triggerStream = trigger.Build(context);
            return Observable.Create<bool>(observer =>
            {
                var subscriptions = new CompositeDisposable();
                var endTime = 0d;
                var active = false;
                subscriptions.Add(SourceStream.Subscribe(state.Stream, observer.OnNext, observer.OnErrorResume));
                subscriptions.Add(SourceStream.Subscribe(triggerStream, _ =>
                {
                    var now = context.FrameClock?.UnscaledTime ?? UnityEngine.Time.realtimeSinceStartupAsDouble;
                    active = durationSeconds > 0f;
                    endTime = now + durationSeconds;
                    state.Set(true);
                    if (!active) state.Set(false);
                }, observer.OnErrorResume));
                if (context.FrameClock == null) throw new InvalidOperationException("PulseBoolSource requires a FrameClock.");
                Action<double, double> update = (_, now) =>
                {
                    if (!active || now < endTime) return;
                    active = false;
                    state.Set(false);
                };
                context.FrameClock.Updated += update;
                subscriptions.Add(Disposable.Create(() => context.FrameClock.Updated -= update));
                return subscriptions;
            });
        }
    }

    [Serializable]
    public sealed class StepIntSource : RouteNode, IValueSource<int>
    {
        [SerializeReference] private ITriggerSource trigger;
        [SerializeField] private int initialValue;
        [SerializeField] private int step = 1;
        [SerializeField] private int minimum;
        [SerializeField] private int maximum = 7;
        public Observable<int> Build(RouteContext context)
        {
            if (trigger == null) throw new InvalidOperationException("StepIntSource trigger is missing.");
            if (minimum > maximum) throw new InvalidOperationException("StepIntSource minimum exceeds maximum.");
            var state = context.GetNodeState(this, () => new StatefulValue<int>(StatefulSourceHelpers.WrapInclusive(initialValue, minimum, maximum)));
            return StatefulSourceHelpers.ObserveState(state, trigger.Build(context), () =>
                state.Set(StatefulSourceHelpers.WrapInclusive(SourceStream.SaturateInt((long)state.Value + step), minimum, maximum)));
        }
    }

    [Serializable]
    public sealed class IncrementDecrementIntSource : RouteNode, IValueSource<int>
    {
        [SerializeReference] private ITriggerSource increment;
        [SerializeReference] private ITriggerSource decrement;
        [SerializeField] private int initialValue;
        [SerializeField, Min(1)] private int step = 1;
        [SerializeField] private int minimum;
        [SerializeField] private int maximum = 100;
        [SerializeField] private bool wrap;
        public Observable<int> Build(RouteContext context)
        {
            if (increment == null || decrement == null) throw new InvalidOperationException("IncrementDecrementIntSource requires both triggers.");
            if (minimum > maximum) throw new InvalidOperationException("IncrementDecrementIntSource minimum exceeds maximum.");
            var state = context.GetNodeState(this, () => new StatefulValue<int>(Mathf.Clamp(initialValue, minimum, maximum)));
            var up = increment.Build(context);
            var down = decrement.Build(context);
            return Observable.Create<int>(observer =>
            {
                var subscriptions = new CompositeDisposable();
                subscriptions.Add(SourceStream.Subscribe(state.Stream, observer.OnNext, observer.OnErrorResume));
                subscriptions.Add(SourceStream.Subscribe(up, _ => state.Set(Adjust(step)), observer.OnErrorResume));
                subscriptions.Add(SourceStream.Subscribe(down, _ => state.Set(Adjust(-(long)step)), observer.OnErrorResume));
                return subscriptions;
                int Adjust(long delta)
                {
                    var candidate = (long)state.Value + delta;
                    if (wrap) return StatefulSourceHelpers.WrapInclusive(SourceStream.SaturateInt(candidate), minimum, maximum);
                    return SourceStream.SaturateInt(Math.Max(minimum, Math.Min(maximum, candidate)));
                }
            });
        }
    }

    [Serializable]
    public sealed class TriggerValueFloatSource : RouteNode, IValueSource<float>
    {
        [SerializeReference] private ITriggerSource trigger;
        [SerializeReference] private IValueSource<float> valueSource;
        [SerializeField] private float initialValue;
        public Observable<float> Build(RouteContext context)
        {
            if (trigger == null || valueSource == null) throw new InvalidOperationException("TriggerValueFloatSource requires a trigger and value input.");
            var state = context.GetNodeState(this, () => new StatefulValue<float>(initialValue));
            var triggerStream = trigger.Build(context);
            var valueStream = valueSource.Build(context);
            return Observable.Create<float>(observer =>
            {
                var subscriptions = new CompositeDisposable();
                var latest = initialValue;
                var hasValue = false;
                subscriptions.Add(SourceStream.Subscribe(state.Stream, observer.OnNext, observer.OnErrorResume));
                subscriptions.Add(SourceStream.Subscribe(valueStream, value => { latest = value; hasValue = true; }, observer.OnErrorResume));
                subscriptions.Add(SourceStream.Subscribe(triggerStream, _ => { if (hasValue) state.Set(latest); }, observer.OnErrorResume));
                return subscriptions;
            });
        }
    }

    [Serializable]
    public sealed class TriggerValueIntSource : RouteNode, IValueSource<int>
    {
        [SerializeReference] private ITriggerSource trigger;
        [SerializeReference] private IValueSource<int> valueSource;
        [SerializeField] private int initialValue;
        public Observable<int> Build(RouteContext context)
        {
            if (trigger == null || valueSource == null) throw new InvalidOperationException("TriggerValueIntSource requires a trigger and value input.");
            var state = context.GetNodeState(this, () => new StatefulValue<int>(initialValue));
            var triggerStream = trigger.Build(context);
            var valueStream = valueSource.Build(context);
            return Observable.Create<int>(observer =>
            {
                var subscriptions = new CompositeDisposable();
                var latest = initialValue;
                var hasValue = false;
                subscriptions.Add(SourceStream.Subscribe(state.Stream, observer.OnNext, observer.OnErrorResume));
                subscriptions.Add(SourceStream.Subscribe(valueStream, value => { latest = value; hasValue = true; }, observer.OnErrorResume));
                subscriptions.Add(SourceStream.Subscribe(triggerStream, _ => { if (hasValue) state.Set(latest); }, observer.OnErrorResume));
                return subscriptions;
            });
        }
    }

    [Serializable]
    public sealed class TriggeredRandomFloatSource : RouteNode, IValueSource<float>
    {
        [SerializeReference] private ITriggerSource trigger;
        [SerializeField] private float minimum;
        [SerializeField] private float maximum = 1f;
        [SerializeField] private float initialValue;
        public Observable<float> Build(RouteContext context)
        {
            if (trigger == null) throw new InvalidOperationException("TriggeredRandomFloatSource trigger is missing.");
            if (minimum > maximum) throw new InvalidOperationException("Random float minimum exceeds maximum.");
            var random = context.GetNodeState(this, "random", () => new DeterministicRandomState(context.RouteId + "/" + NodeId));
            var state = context.GetNodeState(this, "value", () => new StatefulValue<float>(initialValue));
            return StatefulSourceHelpers.ObserveState(state, trigger.Build(context), () => state.Set(minimum + (maximum - minimum) * random.NextUnit()));
        }
    }

    [Serializable]
    public sealed class TriggeredRandomIntSource : RouteNode, IValueSource<int>
    {
        [SerializeReference] private ITriggerSource trigger;
        [SerializeField] private int minimum;
        [SerializeField] private int maximum = 7;
        [SerializeField] private int initialValue;
        public Observable<int> Build(RouteContext context)
        {
            if (trigger == null) throw new InvalidOperationException("TriggeredRandomIntSource trigger is missing.");
            if (minimum > maximum) throw new InvalidOperationException("Random int minimum exceeds maximum.");
            var random = context.GetNodeState(this, "random", () => new DeterministicRandomState(context.RouteId + "/" + NodeId));
            var state = context.GetNodeState(this, "value", () => new StatefulValue<int>(initialValue));
            var width = (ulong)((long)maximum - minimum + 1L);
            return StatefulSourceHelpers.ObserveState(state, trigger.Build(context), () =>
            {
                var offset = (long)(random.NextUInt32() % width);
                state.Set((int)(minimum + offset));
            });
        }
    }

    [Serializable]
    public abstract class SampleHoldSource<T> : RouteNode, IValueSource<T>
    {
        [SerializeReference] private IValueSource<T> input;
        [SerializeReference] private ITriggerSource trigger;
        [SerializeField] private T initialValue;
        public Observable<T> Build(RouteContext context)
        {
            if (input == null || trigger == null) throw new InvalidOperationException("SampleHoldSource requires a value input and trigger.");
            var valueStream = input.Build(context);
            var triggerStream = trigger.Build(context);
            var state = context.GetNodeState(this, () => new StatefulValue<T>(initialValue));
            return Observable.Create<T>(observer =>
            {
                var subscriptions = new CompositeDisposable();
                var latest = initialValue;
                var hasInput = false;
                subscriptions.Add(SourceStream.Subscribe(state.Stream, observer.OnNext, observer.OnErrorResume));
                subscriptions.Add(SourceStream.Subscribe(valueStream, value => { latest = value; hasInput = true; }, observer.OnErrorResume));
                subscriptions.Add(SourceStream.Subscribe(triggerStream, _ => { if (hasInput) state.Set(latest); }, observer.OnErrorResume));
                return subscriptions;
            });
        }
    }

    [Serializable] public sealed class SampleHoldFloatSource : SampleHoldSource<float> { }
    [Serializable] public sealed class SampleHoldVector2Source : SampleHoldSource<Vector2> { }
    [Serializable] public sealed class SampleHoldVector3Source : SampleHoldSource<Vector3> { }
    [Serializable] public sealed class SampleHoldVector4Source : SampleHoldSource<Vector4> { }

    public enum EnvelopeTimeUnit { Seconds, Beats }
    public enum EnvelopeRetriggerMode { Restart, Continue, IgnoreWhilePlaying }

    [Serializable]
    public sealed class TriggerEnvelopeFloatSource : RouteNode, IValueSource<float>
    {
        [SerializeReference] private ITriggerSource trigger;
        [SerializeField] private float start;
        [SerializeField] private float peak = 1f;
        [SerializeField] private float end;
        [SerializeField, Min(0f)] private float attack = 0.1f;
        [SerializeField, Min(0f)] private float hold;
        [SerializeField, Min(0f)] private float release = 0.2f;
        [SerializeField] private AnimationCurve attackCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField] private AnimationCurve releaseCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField] private EnvelopeRetriggerMode retriggerMode;
        [SerializeField] private EnvelopeTimeUnit timeUnit;

        public Observable<float> Build(RouteContext context)
        {
            if (trigger == null) throw new InvalidOperationException("TriggerEnvelopeFloatSource trigger is missing.");
            if (attack < 0f || hold < 0f || release < 0f || float.IsNaN(attack + hold + release))
                throw new InvalidOperationException("Envelope durations must be non-negative.");
            if (timeUnit == EnvelopeTimeUnit.Beats && context.Transport == null)
                throw new InvalidOperationException("Beat envelope requires a Transport service.");
            var state = context.GetNodeState(this, () => new StatefulValue<float>(start));
            var triggerStream = trigger.Build(context);
            return Observable.Create<float>(observer =>
            {
                var subscriptions = new CompositeDisposable();
                var active = false;
                var phase = 0;
                var startTime = 0d;
                var attackStart = start;
                var holdEnd = 0d;
                var releaseStart = 0d;
                subscriptions.Add(SourceStream.Subscribe(state.Stream, observer.OnNext, observer.OnErrorResume));
                if (context.FrameClock == null) throw new InvalidOperationException("TriggerEnvelopeFloatSource requires a FrameClock.");

                Action begin = () =>
                {
                    if (active && retriggerMode == EnvelopeRetriggerMode.IgnoreWhilePlaying) return;
                    var now = StatefulSourceHelpers.Time(context, timeUnit);
                    attackStart = retriggerMode == EnvelopeRetriggerMode.Continue ? state.Value : start;
                    startTime = now;
                    phase = 0;
                    active = true;
                    state.Set(attackStart);
                    Advance(now);
                };

                Action<double, double> update = (_, __) =>
                {
                    if (active) Advance(StatefulSourceHelpers.Time(context, timeUnit));
                };

                void Advance(double now)
                {
                    var safety = 0;
                    while (active && safety++ < 4)
                    {
                        if (phase == 0)
                        {
                            var progress = attack <= 0f ? 1d : (now - startTime) / attack;
                            if (progress < 1d)
                            {
                                state.Set(Mathf.LerpUnclamped(attackStart, peak, attackCurve == null ? (float)progress : attackCurve.Evaluate(Mathf.Clamp01((float)progress))));
                                return;
                            }
                            state.Set(peak);
                            phase = 1;
                            holdEnd = startTime + attack + hold;
                        }
                        if (phase == 1)
                        {
                            if (now < holdEnd) return;
                            phase = 2;
                            releaseStart = holdEnd;
                        }
                        if (phase == 2)
                        {
                            var progress = release <= 0f ? 1d : (now - releaseStart) / release;
                            if (progress >= 1d)
                            {
                                state.Set(end);
                                active = false;
                                return;
                            }
                            state.Set(Mathf.LerpUnclamped(peak, end, releaseCurve == null ? (float)progress : releaseCurve.Evaluate(Mathf.Clamp01((float)progress))));
                            return;
                        }
                    }
                }

                subscriptions.Add(SourceStream.Subscribe(triggerStream, _ => begin(), observer.OnErrorResume));
                context.FrameClock.Updated += update;
                subscriptions.Add(Disposable.Create(() => context.FrameClock.Updated -= update));
                return subscriptions;
            });
        }
    }
}
