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

}
