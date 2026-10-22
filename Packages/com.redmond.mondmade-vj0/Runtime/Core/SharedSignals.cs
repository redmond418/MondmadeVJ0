using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

namespace MondmadeVJ0
{
    public abstract class SharedSignalBase : MonoBehaviour { }
    public interface ISharedSignalOutput { SharedSignalBase SignalOutput { get; } }

    public abstract class SharedValueSignal<T> : SharedSignalBase
    {
        [SerializeField] private T initialValue;
        [NonSerialized] private BehaviorSubject<T> stream;
        [NonSerialized] private bool ready;

        public T Value => Current.Value;
        public Observable<T> Stream => Current;
        private BehaviorSubject<T> Current
        {
            get
            {
                if (!ready)
                {
                    stream = new BehaviorSubject<T>(initialValue);
                    ready = true;
                }
                return stream;
            }
        }

        public void SetValue(T value)
        {
            if (EqualityComparer<T>.Default.Equals(Value, value)) return;
            Current.OnNext(value);
        }

        private void OnDestroy()
        {
            if (ready) stream.Dispose();
            ready = false;
            stream = null;
        }
    }

    public sealed class SharedFloatSignal : SharedValueSignal<float> { }
    public sealed class SharedIntSignal : SharedValueSignal<int> { }
    public sealed class SharedBoolSignal : SharedValueSignal<bool> { }
    public sealed class SharedVector2Signal : SharedValueSignal<Vector2> { }
    public sealed class SharedVector3Signal : SharedValueSignal<Vector3> { }
    public sealed class SharedVector4Signal : SharedValueSignal<Vector4> { }
    public sealed class SharedColorSignal : SharedValueSignal<Color> { }

    public sealed class SharedTriggerSignal : SharedSignalBase
    {
        [NonSerialized] private Subject<Unit> stream = new Subject<Unit>();
        public Observable<Unit> Stream => stream;
        public void Fire() => stream.OnNext(Unit.Default);
        private void OnDestroy() { stream.Dispose(); stream = null; }
    }
}
