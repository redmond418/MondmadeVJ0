using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MondmadeVJ0
{
    public enum InputActionOwnership { AutomaticLease, PlayerInputManaged }

    public sealed class InputActionLeaseManager
    {
        private readonly Dictionary<InputAction, LeaseState> states = new Dictionary<InputAction, LeaseState>();

        public IDisposable Acquire(InputAction action, InputActionOwnership ownership)
        {
            if (action == null) throw new InvalidOperationException("InputAction is not assigned.");
            if (!states.TryGetValue(action, out var state))
            {
                state = new LeaseState { Ownership = ownership, WasEnabled = action.enabled };
                states.Add(action, state);
                if (ownership == InputActionOwnership.AutomaticLease && !action.enabled)
                {
                    action.Enable();
                    state.EnabledByUs = true;
                }
            }
            else if (state.Ownership != ownership)
            {
                throw new InvalidOperationException("The same InputAction cannot mix automatic and PlayerInput-managed leases.");
            }
            state.Count++;
            return new Lease(this, action);
        }

        private void Release(InputAction action)
        {
            if (!states.TryGetValue(action, out var state)) return;
            if (--state.Count > 0) return;
            states.Remove(action);
            if (state.EnabledByUs && action.enabled) action.Disable();
        }

        public void ReleaseAll()
        {
            foreach (var pair in states)
                if (pair.Value.EnabledByUs && pair.Key.enabled) pair.Key.Disable();
            states.Clear();
        }

        private sealed class LeaseState { public int Count; public bool EnabledByUs; public bool WasEnabled; public InputActionOwnership Ownership; }
        private sealed class Lease : IDisposable
        {
            private InputActionLeaseManager owner; private InputAction action;
            public Lease(InputActionLeaseManager owner, InputAction action) { this.owner = owner; this.action = action; }
            public void Dispose() { var manager = owner; owner = null; var value = action; action = null; manager?.Release(value); }
        }
    }

    public sealed class InputSystemModule : MonoBehaviour, IRuntimeModule
    {
        private InputActionLeaseManager leases;
        public string ModuleName => nameof(InputSystemModule);
        public void Register(RuntimeServices services) { leases = new InputActionLeaseManager(); services.Register(leases); }
        public void Initialize(RuntimeServices services) { }
        public void DrainInputQueue(RuntimeServices services, int maxEvents) { }
        public void Tick(RuntimeServices services, double deltaTime) { }
        public void FlushFrame(RuntimeServices services) { }
        public void Shutdown(RuntimeServices services) { leases?.ReleaseAll(); leases = null; }
    }

    [Serializable]
    public abstract class InputActionValueSource<T> : RouteNode, IValueSource<T>
    {
        [SerializeField] protected InputActionReference action;
        [SerializeField] protected InputActionOwnership ownership;
        protected abstract T Read(InputAction inputAction);

        public Observable<T> Build(RouteContext context)
        {
            var inputAction = action == null ? null : action.action;
            if (inputAction == null) throw new InvalidOperationException(GetType().Name + " has no InputAction.");
            var leases = context.Services.Get<InputActionLeaseManager>();
            return Observable.Create<T>(observer =>
            {
                var lease = leases.Acquire(inputAction, ownership);
                Action<InputAction.CallbackContext> performed = callback => observer.OnNext(ReadAction(callback, inputAction));
                Action<InputAction.CallbackContext> canceled = callback => observer.OnNext(ReadAction(callback, inputAction));
                inputAction.performed += performed;
                inputAction.canceled += canceled;
                observer.OnNext(Read(inputAction));
                return Disposable.Create(() =>
                {
                    inputAction.performed -= performed;
                    inputAction.canceled -= canceled;
                    lease.Dispose();
                });
            });
        }

        protected virtual T ReadAction(InputAction.CallbackContext callback, InputAction inputAction) => Read(inputAction);
    }

    [Serializable] public sealed class InputActionFloatSource : InputActionValueSource<float> { protected override float Read(InputAction action) => action.ReadValue<float>(); }
    [Serializable] public sealed class InputActionBoolSource : InputActionValueSource<bool> { protected override bool Read(InputAction action) => action.IsPressed(); }
    [Serializable] public sealed class InputActionVector2Source : InputActionValueSource<Vector2> { protected override Vector2 Read(InputAction action) => action.ReadValue<Vector2>(); }
    [Serializable] public sealed class InputActionVector3Source : InputActionValueSource<Vector3> { protected override Vector3 Read(InputAction action) => action.ReadValue<Vector3>(); }

    [Serializable]
    public abstract class InputActionTriggerBase : RouteNode
    {
        [SerializeField] protected InputActionReference action;
        [SerializeField] protected InputActionOwnership ownership;
        protected IDisposable Subscribe(RouteContext context, bool onRelease, Action fire)
        {
            var inputAction = action == null ? null : action.action;
            if (inputAction == null) throw new InvalidOperationException(GetType().Name + " has no InputAction.");
            var lease = context.Services.Get<InputActionLeaseManager>().Acquire(inputAction, ownership);
            Action<InputAction.CallbackContext> callback = _ => fire();
            if (onRelease) inputAction.canceled += callback; else inputAction.performed += callback;
            return Disposable.Create(() => { if (onRelease) inputAction.canceled -= callback; else inputAction.performed -= callback; lease.Dispose(); });
        }
        protected Observable<Unit> Build(RouteContext context, bool release)
        {
            return Observable.Create<Unit>(observer => Subscribe(context, release, () => observer.OnNext(Unit.Default)));
        }
    }
    [Serializable] public sealed class InputActionTriggerSource : InputActionTriggerBase, ITriggerSource { public Observable<Unit> Build(RouteContext context) => Build(context, false); }
    [Serializable] public sealed class InputActionReleaseTriggerSource : InputActionTriggerBase, ITriggerSource { public Observable<Unit> Build(RouteContext context) => Build(context, true); }

    [Serializable]
    public sealed class InputActionLastPressedIndexSource : RouteNode, IValueSource<int>
    {
        [SerializeField] private InputActionReference[] actions = Array.Empty<InputActionReference>();
        [SerializeField] private InputActionOwnership ownership;
        public Observable<int> Build(RouteContext context)
        {
            if (actions == null || actions.Length == 0) throw new InvalidOperationException("LastPressedIndex requires at least one Action.");
            var leaseManager = context.Services.Get<InputActionLeaseManager>();
            return Observable.Create<int>(observer =>
            {
                var d = new CompositeDisposable();
                observer.OnNext(-1);
                for (var i = 0; i < actions.Length; i++)
                {
                    var index = i;
                    var action = actions[i] == null ? null : actions[i].action;
                    if (action == null) throw new InvalidOperationException($"LastPressedIndex Action {i} is missing.");
                    var lease = leaseManager.Acquire(action, ownership);
                    Action<InputAction.CallbackContext> performed = _ => observer.OnNext(index);
                    action.performed += performed;
                    d.Add(Disposable.Create(() => { action.performed -= performed; lease.Dispose(); }));
                }
                return d;
            });
        }
    }
}
