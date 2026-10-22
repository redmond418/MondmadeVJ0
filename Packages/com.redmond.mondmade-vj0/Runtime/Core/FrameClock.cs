using System;
using UnityEngine;

namespace MondmadeVJ0
{
    public sealed class FrameClock : IFrameClock
    {
        private readonly object callbackLock = new object();
        private Action<double, double>[] callbacks = Array.Empty<Action<double, double>>();
        public long FrameIndex { get; private set; }
        public double Time { get; private set; }
        public double UnscaledTime { get; private set; }
        public double DspTime { get; private set; }
        public double DeltaTime { get; private set; }
        public double UnscaledDeltaTime { get; private set; }
        public int SubscriberCount => callbacks.Length;
        public event Action<double, double> Updated
        {
            add { lock (callbackLock) callbacks = AddCallback(callbacks, value); }
            remove { lock (callbackLock) callbacks = RemoveCallback(callbacks, value); }
        }

        internal void BeginFrame()
        {
            FrameIndex++;
            Time = UnityEngine.Time.timeAsDouble;
            UnscaledTime = UnityEngine.Time.unscaledTimeAsDouble;
            DspTime = AudioSettings.dspTime;
            DeltaTime = Math.Max(0d, UnityEngine.Time.deltaTime);
            UnscaledDeltaTime = Math.Max(0d, UnityEngine.Time.unscaledDeltaTime);
        }

        internal void NotifyUpdated(RuntimeServices services)
        {
            var snapshot = callbacks;
            var measure = services.MonitorCaptureEnabled;
            var started = measure ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            for (var i = 0; i < snapshot.Length; i++)
            {
                try { snapshot[i](UnscaledDeltaTime, UnscaledTime); }
                catch (Exception exception)
                {
                    services.Report("FRAME_CLOCK", "A FrameClock subscriber failed.", exception: exception);
                }
            }
            if (measure) services.RecordFrameCallbacks(snapshot.Length, System.Diagnostics.Stopwatch.GetTimestamp() - started);
        }

        private static Action<double, double>[] AddCallback(Action<double, double>[] current, Action<double, double> callback)
        {
            if (callback == null) return current;
            var result = new Action<double, double>[current.Length + 1];
            Array.Copy(current, result, current.Length);
            result[result.Length - 1] = callback;
            return result;
        }

        private static Action<double, double>[] RemoveCallback(Action<double, double>[] current, Action<double, double> callback)
        {
            if (callback == null) return current;
            for (var i = current.Length - 1; i >= 0; i--)
                if (current[i] == callback)
                {
                    var result = new Action<double, double>[current.Length - 1];
                    if (i > 0) Array.Copy(current, 0, result, 0, i);
                    if (i < current.Length - 1) Array.Copy(current, i + 1, result, i, current.Length - i - 1);
                    return result;
                }
            return current;
        }
    }

    public sealed class FrameEndScheduler
    {
        private readonly System.Collections.Generic.Dictionary<object, Action> pending = new System.Collections.Generic.Dictionary<object, Action>();
        private readonly System.Collections.Generic.Dictionary<object, Action> nextFrame = new System.Collections.Generic.Dictionary<object, Action>();
        private bool flushing;

        public void Schedule(object key, Action action)
        {
            if (key == null || action == null) return;
            var destination = flushing ? nextFrame : pending;
            if (!destination.ContainsKey(key)) destination.Add(key, action);
        }

        public void Flush(RuntimeServices services)
        {
            if (flushing) return;
            flushing = true;
            try
            {
                foreach (var action in pending.Values)
                {
                    try { action?.Invoke(); }
                    catch (Exception exception)
                    {
                        services.Report("FRAME_END_ACTION", "A frame-end action failed.", exception: exception);
                    }
                }
                pending.Clear();
            }
            finally
            {
                flushing = false;
                foreach (var item in nextFrame) pending[item.Key] = item.Value;
                nextFrame.Clear();
            }
        }
    }
}
