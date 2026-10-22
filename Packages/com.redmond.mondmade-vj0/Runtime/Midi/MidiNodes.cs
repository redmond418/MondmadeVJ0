using System;
using R3;
using UnityEngine;

namespace MondmadeVJ0
{
    public interface IMidiControlTypeConstraint
    {
        bool AcceptsControlType(string fieldName, MidiControlType type);
    }

    internal static class MidiNodeHelpers
    {
        public static MidiHub RequireHub(RouteContext context) => context.Services.Get<MidiHub>();
        public static void RequireReference(MidiHub hub, MidiControlReference reference, RouteNode node)
        {
            if (!hub.ValidateReference(reference)) throw new InvalidOperationException($"{node.GetType().Name} references an unknown MIDI control '{reference}'.");
        }
    }

    [Serializable]
    public abstract class MidiValueSource<T> : RouteNode, IValueSource<T>, IMidiControlTypeConstraint
    {
        [SerializeField] protected MidiControlReference control;
        protected abstract T Select(MidiControlState state);
        protected virtual bool AcceptsControlType(MidiControlType type) => true;
        public bool AcceptsControlType(string fieldName, MidiControlType type) => fieldName == nameof(control) && AcceptsControlType(type);
        public Observable<T> Build(RouteContext context)
        {
            var hub = MidiNodeHelpers.RequireHub(context);
            MidiNodeHelpers.RequireReference(hub, control, this);
            hub.TryGetControlDefinition(control, out var definition);
            if (!AcceptsControlType(definition.type))
                throw new InvalidOperationException($"{GetType().Name} requires a compatible MIDI control type, but '{control.ControlId}' is {definition.type}.");
            return hub.Observe(control).Select(Select);
        }
    }

    [Serializable] public sealed class MidiAbsoluteFloatSource : MidiValueSource<float> { protected override float Select(MidiControlState state) => state.Value; protected override bool AcceptsControlType(MidiControlType type) => type is MidiControlType.AbsoluteFader or MidiControlType.AbsoluteKnob or MidiControlType.PitchBend or MidiControlType.Aftertouch; }
    [Serializable] public sealed class MidiRelativeFloatSource : MidiValueSource<float> { protected override float Select(MidiControlState state) => state.Value; protected override bool AcceptsControlType(MidiControlType type) => type == MidiControlType.RelativeEncoder; }
    [Serializable] public sealed class MidiVelocityFloatSource : MidiValueSource<float> { protected override float Select(MidiControlState state) => state.Velocity; protected override bool AcceptsControlType(MidiControlType type) => type == MidiControlType.VelocityButton; }
    [Serializable] public sealed class MidiButtonBoolSource : MidiValueSource<bool> { protected override bool Select(MidiControlState state) => state.Pressed; protected override bool AcceptsControlType(MidiControlType type) => type is MidiControlType.Button or MidiControlType.VelocityButton; }

    [Serializable]
    public sealed class MidiXYVector2Source : RouteNode, IValueSource<Vector2>, IMidiControlTypeConstraint
    {
        [SerializeField] private MidiControlReference xControl;
        [SerializeField] private MidiControlReference yControl;

        public bool AcceptsControlType(string fieldName, MidiControlType type)
            => (fieldName == nameof(xControl) || fieldName == nameof(yControl)) && type == MidiControlType.XY;

        public Observable<Vector2> Build(RouteContext context)
        {
            var hub = MidiNodeHelpers.RequireHub(context);
            MidiNodeHelpers.RequireReference(hub, xControl, this);
            MidiNodeHelpers.RequireReference(hub, yControl, this);
            if (xControl.SurfaceInstanceId != yControl.SurfaceInstanceId)
                throw new InvalidOperationException("MidiXYVector2Source axes must belong to the same surface instance.");
            if (xControl.ControlId == yControl.ControlId)
                throw new InvalidOperationException("MidiXYVector2Source requires distinct X and Y controls.");
            hub.TryGetControlDefinition(xControl, out var xDefinition);
            hub.TryGetControlDefinition(yControl, out var yDefinition);
            if (xDefinition.type != MidiControlType.XY || yDefinition.type != MidiControlType.XY)
                throw new InvalidOperationException("MidiXYVector2Source requires two controls with Control Type XY.");

            return Observable.Create<Vector2>(observer =>
            {
                var current = new Vector2(hub.GetState(xControl).Value, hub.GetState(yControl).Value);
                observer.OnNext(current);
                var subscriptions = new CompositeDisposable();
                subscriptions.Add(MidiStream.Subscribe(hub.ObserveChanges(xControl), change =>
                {
                    current.x = change.State.Value;
                    observer.OnNext(current);
                }, observer.OnErrorResume));
                subscriptions.Add(MidiStream.Subscribe(hub.ObserveChanges(yControl), change =>
                {
                    current.y = change.State.Value;
                    observer.OnNext(current);
                }, observer.OnErrorResume));
                return subscriptions;
            });
        }
    }

    [Serializable]
    public sealed class MidiButtonGroupIndexSource : RouteNode, IValueSource<int>, IMidiControlTypeConstraint
    {
        [SerializeField] private MidiControlReference[] controls = Array.Empty<MidiControlReference>();
        [SerializeField] private int initialIndex;
        public bool AcceptsControlType(string fieldName, MidiControlType type)
            => fieldName == nameof(controls) && type is MidiControlType.Button or MidiControlType.VelocityButton;
        public Observable<int> Build(RouteContext context)
        {
            if (controls == null || controls.Length == 0) throw new InvalidOperationException("MidiButtonGroupIndexSource requires a button list.");
            var hub = MidiNodeHelpers.RequireHub(context);
            foreach (var reference in controls)
            {
                MidiNodeHelpers.RequireReference(hub, reference, this);
                hub.TryGetControlDefinition(reference, out var definition);
                if (definition.type is not (MidiControlType.Button or MidiControlType.VelocityButton))
                    throw new InvalidOperationException($"MidiButtonGroupIndexSource requires buttons, but '{reference.ControlId}' is {definition.type}.");
            }
            var initial = Mathf.Clamp(initialIndex, 0, controls.Length - 1);
            return Observable.Create<int>(observer =>
            {
                var subscriptions = new CompositeDisposable();
                var current = initial;
                observer.OnNext(current);
                for (var i = 0; i < controls.Length; i++)
                {
                    var index = i;
                        subscriptions.Add(MidiStream.Subscribe(hub.ObserveChanges(controls[index]), change =>
                    {
                        if (!change.PressedDown || current == index) return;
                        current = index;
                        observer.OnNext(current);
                    }, observer.OnErrorResume));
                }
                return subscriptions;
            });
        }
    }

    internal static class MidiStream
    {
        public static IDisposable Subscribe<T>(Observable<T> stream, Action<T> onNext, Action<Exception> onError) => stream.Subscribe(onNext, onError, result => { if (result.IsFailure) onError(result.Exception); });
    }

    [Serializable]
    public abstract class MidiButtonChangeTriggerSource : RouteNode, ITriggerSource, IMidiControlTypeConstraint
    {
        [SerializeField] protected MidiControlReference control;
        protected abstract bool Match(MidiControlChange change);
        public bool AcceptsControlType(string fieldName, MidiControlType type)
            => fieldName == nameof(control) && type is MidiControlType.Button or MidiControlType.VelocityButton;
        public Observable<Unit> Build(RouteContext context)
        {
            var hub = MidiNodeHelpers.RequireHub(context);
            MidiNodeHelpers.RequireReference(hub, control, this);
            hub.TryGetControlDefinition(control, out var definition);
            if (definition.type is not (MidiControlType.Button or MidiControlType.VelocityButton))
                throw new InvalidOperationException($"{GetType().Name} requires a button, but '{control.ControlId}' is {definition.type}.");
            return hub.ObserveChanges(control).Where(Match).Select(_ => Unit.Default);
        }
    }
    [Serializable] public sealed class MidiButtonTriggerSource : MidiButtonChangeTriggerSource { protected override bool Match(MidiControlChange change) => change.PressedDown; }
    [Serializable] public sealed class MidiButtonReleaseTriggerSource : MidiButtonChangeTriggerSource { protected override bool Match(MidiControlChange change) => change.Released; }

    [Serializable]
    public abstract class MidiLedValueOutput<T> : RouteNode, IValueOutput<T>
    {
        [SerializeField] protected MidiControlReference control;
        [SerializeField] protected MidiLedLayer layer = MidiLedLayer.State;
        [NonSerialized] protected MidiHub Hub;
        [NonSerialized] protected string Owner;
        protected abstract MidiLedStyle Style(T value);
        public void Initialize(RouteContext context)
        {
            Hub = MidiNodeHelpers.RequireHub(context);
            MidiNodeHelpers.RequireReference(Hub, control, this);
            Owner = Hub.CreateLayerOwner(context.Host);
            var owner = Owner;
            context.Scope.Add(() => Hub?.RemoveLayerOwner(owner));
        }
        public void Write(T value) => Hub?.SetLed(control, Owner, layer, Style(value));
    }

    [Serializable]
    public sealed class MidiLedBoolOutput : MidiLedValueOutput<bool>
    {
        [SerializeField] private Color offColor = Color.black;
        [SerializeField] private Color onColor = Color.green;
        [SerializeField, Range(0f,1f)] private float brightness = 1f;
        protected override MidiLedStyle Style(bool value) => new MidiLedStyle { color = value ? onColor : offColor, brightness = value ? brightness : 0f, effect = MidiLedEffect.Solid };
    }

    [Serializable]
    public sealed class MidiLedValueFloatOutput : MidiLedValueOutput<float>
    {
        [SerializeField] private Gradient gradient = new Gradient();
        [SerializeField, Range(0f,1f)] private float brightness = 1f;
        protected override MidiLedStyle Style(float value) => new MidiLedStyle { color = gradient == null ? Color.white : gradient.Evaluate(Mathf.Clamp01(value)), brightness = brightness, effect = MidiLedEffect.Solid };
    }

    [Serializable]
    public sealed class MidiLedColorOutput : MidiLedValueOutput<Color>
    {
        [SerializeField, Range(0f,1f)] private float brightness = 1f;
        protected override MidiLedStyle Style(Color value) => new MidiLedStyle { color = value, brightness = brightness, effect = MidiLedEffect.Solid };
    }

    [Serializable]
    public sealed class MidiLedIndexedIntOutput : RouteNode, IValueOutput<int>
    {
        [SerializeField] private MidiControlReference[] controls = Array.Empty<MidiControlReference>();
        [SerializeField] private MidiLedLayer layer = MidiLedLayer.State;
        [SerializeField] private Color offColor = Color.black;
        [SerializeField] private Color onColor = Color.green;
        [NonSerialized] private MidiHub hub;
        [NonSerialized] private string owner;
        public void Initialize(RouteContext context)
        {
            if (controls == null || controls.Length == 0) throw new InvalidOperationException("MidiLedIndexedIntOutput requires a control list.");
            hub = MidiNodeHelpers.RequireHub(context);
            foreach (var reference in controls) MidiNodeHelpers.RequireReference(hub, reference, this);
            owner = hub.CreateLayerOwner(context.Host);
            var token = owner;
            context.Scope.Add(() => hub?.RemoveLayerOwner(token));
        }
        public void Write(int value)
        {
            if (hub == null) return;
            var index = Mathf.Clamp(value, 0, controls.Length - 1);
            for (var i=0;i<controls.Length;i++) hub.SetLed(controls[i], owner, layer, new MidiLedStyle { color = i == index ? onColor : offColor, brightness = i == index ? 1f : 0f, effect = MidiLedEffect.Solid });
        }
    }

    [Serializable]
    public sealed class MidiLedTriggerFlashOutput : RouteNode, ITriggerOutput
    {
        [SerializeField] private MidiControlReference control;
        [SerializeField] private Color color = Color.white;
        [SerializeField, Range(0f,1f)] private float brightness = 1f;
        [SerializeField, Min(0f)] private float durationSeconds = 0.1f;
        [NonSerialized] private MidiHub hub;
        [NonSerialized] private IFrameClock clock;
        [NonSerialized] private string owner;
        [NonSerialized] private double deadline;
        [NonSerialized] private bool active;
        [NonSerialized] private RouteContext context;
        public void Initialize(RouteContext routeContext)
        {
            context = routeContext;
            hub = MidiNodeHelpers.RequireHub(context);
            MidiNodeHelpers.RequireReference(hub, control, this);
            if (durationSeconds < 0f || float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds)) throw new InvalidOperationException("LED flash duration must be finite and non-negative.");
            clock = context.FrameClock ?? throw new InvalidOperationException("LED flash requires FrameClock.");
            owner = hub.CreateLayerOwner(context.Host);
            Action<double,double> update = (_,now) => { if(active && now >= deadline){active=false;hub.RemoveLayerOwner(owner);} };
            clock.Updated += update;
            var token = owner;
            context.Scope.Add(() => { clock.Updated -= update; hub?.RemoveLayerOwner(token); });
        }
        public void Invoke()
        {
            active = true;
            deadline = clock.UnscaledTime + durationSeconds;
            hub.SetLed(control, owner, MidiLedLayer.Interaction, new MidiLedStyle { color = color, brightness = brightness, effect = MidiLedEffect.Solid, durationSeconds = durationSeconds });
        }
    }
}
