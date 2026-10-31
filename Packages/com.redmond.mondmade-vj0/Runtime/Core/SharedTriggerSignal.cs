using System;
using R3;

namespace MondmadeVJ0
{
    public sealed class SharedTriggerSignal : SharedSignalBase
    {
        [NonSerialized] private Subject<Unit> stream = new Subject<Unit>();
        public Observable<Unit> Stream => stream;
        public void Fire() => stream.OnNext(Unit.Default);
        private void OnDestroy() { stream.Dispose(); stream = null; }
    }
}
