using System;
using UnityEngine;

namespace MondmadeVJ0
{
    [Serializable] public sealed class DebugFloatOutput : RouteNode, IValueOutput<float> { public void Initialize(RouteContext c) { } public void Write(float value) => Debug.Log($"[MondmadeVJ0] {value}"); }
    [Serializable] public sealed class DebugIntOutput : RouteNode, IValueOutput<int> { public void Initialize(RouteContext c) { } public void Write(int value) => Debug.Log($"[MondmadeVJ0] {value}"); }
    [Serializable] public sealed class DebugBoolOutput : RouteNode, IValueOutput<bool> { public void Initialize(RouteContext c) { } public void Write(bool value) => Debug.Log($"[MondmadeVJ0] {value}"); }
    [Serializable] public sealed class DebugVector2Output : RouteNode, IValueOutput<Vector2> { public void Initialize(RouteContext c) { } public void Write(Vector2 value) => Debug.Log($"[MondmadeVJ0] {value}"); }
    [Serializable] public sealed class DebugVector3Output : RouteNode, IValueOutput<Vector3> { public void Initialize(RouteContext c) { } public void Write(Vector3 value) => Debug.Log($"[MondmadeVJ0] {value}"); }
    [Serializable] public sealed class DebugVector4Output : RouteNode, IValueOutput<Vector4> { public void Initialize(RouteContext c) { } public void Write(Vector4 value) => Debug.Log($"[MondmadeVJ0] {value}"); }
    [Serializable] public sealed class DebugColorOutput : RouteNode, IValueOutput<Color> { public void Initialize(RouteContext c) { } public void Write(Color value) => Debug.Log($"[MondmadeVJ0] {value}"); }
    [Serializable] public sealed class DebugTriggerOutput : RouteNode, ITriggerOutput { public void Initialize(RouteContext c) { } public void Invoke() => Debug.Log("[MondmadeVJ0] Trigger"); }

    [Serializable] public sealed class SignalFloatOutput : RouteNode, IValueOutput<float>, ISharedSignalOutput { [SerializeField] private SharedFloatSignal signal; public SharedSignalBase SignalOutput => signal; public void Initialize(RouteContext c) { Require(signal); } public void Write(float value) => signal.SetValue(value); private static void Require(UnityEngine.Object o) { if (o == null) throw new InvalidOperationException("Shared signal is not assigned."); } }
    [Serializable] public sealed class SignalIntOutput : RouteNode, IValueOutput<int>, ISharedSignalOutput { [SerializeField] private SharedIntSignal signal; public SharedSignalBase SignalOutput => signal; public void Initialize(RouteContext c) { Require(signal); } public void Write(int value) => signal.SetValue(value); private static void Require(UnityEngine.Object o) { if (o == null) throw new InvalidOperationException("Shared signal is not assigned."); } }
    [Serializable] public sealed class SignalBoolOutput : RouteNode, IValueOutput<bool>, ISharedSignalOutput { [SerializeField] private SharedBoolSignal signal; public SharedSignalBase SignalOutput => signal; public void Initialize(RouteContext c) { Require(signal); } public void Write(bool value) => signal.SetValue(value); private static void Require(UnityEngine.Object o) { if (o == null) throw new InvalidOperationException("Shared signal is not assigned."); } }
    [Serializable] public sealed class SignalVector2Output : RouteNode, IValueOutput<Vector2>, ISharedSignalOutput { [SerializeField] private SharedVector2Signal signal; public SharedSignalBase SignalOutput => signal; public void Initialize(RouteContext c) { Require(signal); } public void Write(Vector2 value) => signal.SetValue(value); private static void Require(UnityEngine.Object o) { if (o == null) throw new InvalidOperationException("Shared signal is not assigned."); } }
    [Serializable] public sealed class SignalVector3Output : RouteNode, IValueOutput<Vector3>, ISharedSignalOutput { [SerializeField] private SharedVector3Signal signal; public SharedSignalBase SignalOutput => signal; public void Initialize(RouteContext c) { Require(signal); } public void Write(Vector3 value) => signal.SetValue(value); private static void Require(UnityEngine.Object o) { if (o == null) throw new InvalidOperationException("Shared signal is not assigned."); } }
    [Serializable] public sealed class SignalVector4Output : RouteNode, IValueOutput<Vector4>, ISharedSignalOutput { [SerializeField] private SharedVector4Signal signal; public SharedSignalBase SignalOutput => signal; public void Initialize(RouteContext c) { Require(signal); } public void Write(Vector4 value) => signal.SetValue(value); private static void Require(UnityEngine.Object o) { if (o == null) throw new InvalidOperationException("Shared signal is not assigned."); } }
    [Serializable] public sealed class SignalColorOutput : RouteNode, IValueOutput<Color>, ISharedSignalOutput { [SerializeField] private SharedColorSignal signal; public SharedSignalBase SignalOutput => signal; public void Initialize(RouteContext c) { Require(signal); } public void Write(Color value) => signal.SetValue(value); private static void Require(UnityEngine.Object o) { if (o == null) throw new InvalidOperationException("Shared signal is not assigned."); } }
    [Serializable] public sealed class SignalTriggerOutput : RouteNode, ITriggerOutput, ISharedSignalOutput { [SerializeField] private SharedTriggerSignal signal; public SharedSignalBase SignalOutput => signal; public void Initialize(RouteContext c) { if (signal == null) throw new InvalidOperationException("Shared trigger signal is not assigned."); } public void Invoke() => signal.Fire(); }

    [Serializable] public sealed class GameObjectActiveBoolOutput : RouteNode, IValueOutput<bool> { [SerializeField] private GameObject target; public void Initialize(RouteContext c) { Require(target); } public void Write(bool value) => target.SetActive(value); private static void Require(UnityEngine.Object o) { if (o == null) throw new InvalidOperationException("Target is not assigned."); } }
    [Serializable] public sealed class BehaviourEnabledBoolOutput : RouteNode, IValueOutput<bool> { [SerializeField] private Behaviour target; public void Initialize(RouteContext c) { Require(target); } public void Write(bool value) => target.enabled = value; private static void Require(UnityEngine.Object o) { if (o == null) throw new InvalidOperationException("Target is not assigned."); } }
    [Serializable] public sealed class RendererEnabledBoolOutput : RouteNode, IValueOutput<bool> { [SerializeField] private Renderer target; public void Initialize(RouteContext c) { Require(target); } public void Write(bool value) => target.enabled = value; private static void Require(UnityEngine.Object o) { if (o == null) throw new InvalidOperationException("Target is not assigned."); } }

    [Serializable]
    public sealed class ObjectGroupIndexIntOutput : RouteNode, IValueOutput<int>
    {
        [SerializeField] private GameObject[] objects = Array.Empty<GameObject>();
        public void Initialize(RouteContext context) { if (objects == null || objects.Length == 0) throw new InvalidOperationException("Object group is empty."); }
        public void Write(int value)
        {
            var index = Mathf.Clamp(value, 0, objects.Length - 1);
            for (var i = 0; i < objects.Length; i++) if (objects[i] != null) objects[i].SetActive(i == index);
        }
    }

    [Serializable]
    public sealed class TransformPositionVector3Output : RouteNode, IValueOutput<Vector3>
    {
        [SerializeField] private Transform target;
        [SerializeField] private bool worldSpace;
        public void Initialize(RouteContext context) { Require(target); }
        public void Write(Vector3 value) { if (worldSpace) target.position = value; else target.localPosition = value; }
        private static void Require(UnityEngine.Object o) { if (o == null) throw new InvalidOperationException("Transform target is not assigned."); }
    }

    [Serializable]
    public sealed class TransformRotationVector3Output : RouteNode, IValueOutput<Vector3>
    {
        [SerializeField] private Transform target;
        [SerializeField] private bool worldSpace;
        public void Initialize(RouteContext context) { Require(target); }
        public void Write(Vector3 value) { if (worldSpace) target.eulerAngles = value; else target.localEulerAngles = value; }
        private static void Require(UnityEngine.Object o) { if (o == null) throw new InvalidOperationException("Transform target is not assigned."); }
    }

    [Serializable]
    public sealed class TransformScaleVector3Output : RouteNode, IValueOutput<Vector3>
    {
        [SerializeField] private Transform target;
        public void Initialize(RouteContext context) { if (target == null) throw new InvalidOperationException("Transform target is not assigned."); }
        public void Write(Vector3 value) => target.localScale = value;
    }

    public enum TransformAxis { X, Y, Z }
    [Serializable]
    public abstract class TransformAxisFloatOutput : RouteNode, IValueOutput<float>
    {
        [SerializeField] protected Transform target;
        [SerializeField] protected TransformAxis axis;
        [SerializeField] protected bool worldSpace;
        protected abstract int Channel { get; }
        public void Initialize(RouteContext context) { if (target == null) throw new InvalidOperationException("Transform target is not assigned."); }
        public void Write(float value)
        {
            var vector = Channel == 0 ? (worldSpace ? target.position : target.localPosition) :
                         Channel == 1 ? (worldSpace ? target.eulerAngles : target.localEulerAngles) : target.localScale;
            if (axis == TransformAxis.X) vector.x = value; else if (axis == TransformAxis.Y) vector.y = value; else vector.z = value;
            if (Channel == 0) { if (worldSpace) target.position = vector; else target.localPosition = vector; }
            else if (Channel == 1) { if (worldSpace) target.eulerAngles = vector; else target.localEulerAngles = vector; }
            else target.localScale = vector;
        }
    }
    [Serializable] public sealed class TransformPositionAxisFloatOutput : TransformAxisFloatOutput { protected override int Channel => 0; }
    [Serializable] public sealed class TransformRotationAxisFloatOutput : TransformAxisFloatOutput { protected override int Channel => 1; }
    [Serializable] public sealed class TransformScaleAxisFloatOutput : TransformAxisFloatOutput { protected override int Channel => 2; }
}
