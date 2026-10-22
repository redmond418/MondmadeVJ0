using System;
using R3;
using UnityEngine;

namespace MondmadeVJ0
{
    [Serializable] public sealed class ConstantFloatSource : RouteNode, IValueSource<float> { [SerializeField] public float Value; public Observable<float> Build(RouteContext c) => Observable.Return(Value); }
    [Serializable] public sealed class ConstantIntSource : RouteNode, IValueSource<int> { [SerializeField] public int Value; public Observable<int> Build(RouteContext c) => Observable.Return(Value); }
    [Serializable] public sealed class ConstantBoolSource : RouteNode, IValueSource<bool> { [SerializeField] public bool Value; public Observable<bool> Build(RouteContext c) => Observable.Return(Value); }
    [Serializable] public sealed class ConstantVector2Source : RouteNode, IValueSource<Vector2> { [SerializeField] public Vector2 Value; public Observable<Vector2> Build(RouteContext c) => Observable.Return(Value); }
    [Serializable] public sealed class ConstantVector3Source : RouteNode, IValueSource<Vector3> { [SerializeField] public Vector3 Value; public Observable<Vector3> Build(RouteContext c) => Observable.Return(Value); }
    [Serializable] public sealed class ConstantVector4Source : RouteNode, IValueSource<Vector4> { [SerializeField] public Vector4 Value; public Observable<Vector4> Build(RouteContext c) => Observable.Return(Value); }
    [Serializable] public sealed class ConstantColorSource : RouteNode, IValueSource<Color> { [SerializeField] public Color Value = Color.white; public Observable<Color> Build(RouteContext c) => Observable.Return(Value); }

    [Serializable] public sealed class SignalFloatSource : RouteNode, IValueSource<float> { [SerializeField] public SharedFloatSignal Signal; public Observable<float> Build(RouteContext c) => Signal != null ? Signal.Stream : throw new InvalidOperationException("SharedFloatSignal is not assigned."); }
    [Serializable] public sealed class SignalIntSource : RouteNode, IValueSource<int> { [SerializeField] public SharedIntSignal Signal; public Observable<int> Build(RouteContext c) => Signal != null ? Signal.Stream : throw new InvalidOperationException("SharedIntSignal is not assigned."); }
    [Serializable] public sealed class SignalBoolSource : RouteNode, IValueSource<bool> { [SerializeField] public SharedBoolSignal Signal; public Observable<bool> Build(RouteContext c) => Signal != null ? Signal.Stream : throw new InvalidOperationException("SharedBoolSignal is not assigned."); }
    [Serializable] public sealed class SignalVector2Source : RouteNode, IValueSource<Vector2> { [SerializeField] public SharedVector2Signal Signal; public Observable<Vector2> Build(RouteContext c) => Signal != null ? Signal.Stream : throw new InvalidOperationException("SharedVector2Signal is not assigned."); }
    [Serializable] public sealed class SignalVector3Source : RouteNode, IValueSource<Vector3> { [SerializeField] public SharedVector3Signal Signal; public Observable<Vector3> Build(RouteContext c) => Signal != null ? Signal.Stream : throw new InvalidOperationException("SharedVector3Signal is not assigned."); }
    [Serializable] public sealed class SignalVector4Source : RouteNode, IValueSource<Vector4> { [SerializeField] public SharedVector4Signal Signal; public Observable<Vector4> Build(RouteContext c) => Signal != null ? Signal.Stream : throw new InvalidOperationException("SharedVector4Signal is not assigned."); }
    [Serializable] public sealed class SignalColorSource : RouteNode, IValueSource<Color> { [SerializeField] public SharedColorSignal Signal; public Observable<Color> Build(RouteContext c) => Signal != null ? Signal.Stream : throw new InvalidOperationException("SharedColorSignal is not assigned."); }

    [Serializable]
    public sealed class SignalTriggerSource : RouteNode, ITriggerSource
    {
        [SerializeField] private SharedTriggerSignal signal;
        public SharedTriggerSignal Signal => signal;
        public Observable<Unit> Build(RouteContext context) => signal != null ? signal.Stream : throw new InvalidOperationException("SharedTriggerSignal is not assigned.");
    }
}
