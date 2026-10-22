using System;
using System.Collections.Generic;
using UnityEngine;

namespace MondmadeVJ0
{
    public sealed class BindingScope : IDisposable
    {
        private readonly List<IDisposable> disposables = new List<IDisposable>();
        private bool disposed;

        public T Add<T>(T disposable) where T : IDisposable
        {
            if (disposable == null) return disposable;
            if (disposed) disposable.Dispose();
            else disposables.Add(disposable);
            return disposable;
        }

        public void Add(Action dispose) => Add(new ActionDisposable(dispose));

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (var i = disposables.Count - 1; i >= 0; i--)
            {
                try { disposables[i]?.Dispose(); }
                catch (Exception ex) { UnityEngine.Debug.LogException(ex); }
            }
            disposables.Clear();
        }

        private sealed class ActionDisposable : IDisposable
        {
            private Action action;
            public ActionDisposable(Action action) => this.action = action;
            public void Dispose() { var current = action; action = null; current?.Invoke(); }
        }
    }

    public sealed class RuntimeStateStore
    {
        private readonly Dictionary<string, object> values = new Dictionary<string, object>();

        public T GetOrCreate<T>(string routeId, string nodeId, Func<T> factory)
            => GetOrCreate(routeId, nodeId, string.Empty, factory);

        public T GetOrCreate<T>(string routeId, string nodeId, string slot, Func<T> factory)
        {
            var key = $"{routeId}/{nodeId}/{slot ?? string.Empty}";
            if (values.TryGetValue(key, out var value)) return (T)value;
            var created = factory();
            values.Add(key, created);
            return created;
        }

        public void RemoveRoute(string routeId)
        {
            var prefix = routeId + "/";
            var remove = new List<string>();
            foreach (var key in values.Keys) if (key.StartsWith(prefix, StringComparison.Ordinal)) remove.Add(key);
            for (var i = 0; i < remove.Count; i++) RemoveValue(remove[i]);
        }

        public void Clear()
        {
            foreach (var key in new List<string>(values.Keys)) RemoveValue(key);
        }

        private void RemoveValue(string key)
        {
            if (!values.TryGetValue(key, out var value)) return;
            values.Remove(key);
            if (value is IDisposable disposable)
            {
                try { disposable.Dispose(); }
                catch (Exception exception) { UnityEngine.Debug.LogException(exception); }
            }
        }
    }

    public sealed class RouteContext
    {
        public readonly MondmadeVJ0Runtime Runtime;
        public readonly RouteHost Host;
        public readonly RuntimeServices Services;
        public readonly RuntimeStateStore State;
        public readonly BindingScope Scope;
        public readonly string RouteId;
        public readonly IFrameClock FrameClock;
        public readonly IMusicalTransport Transport;

        internal RouteContext(MondmadeVJ0Runtime runtime, RouteHost host, RuntimeStateStore state, BindingScope scope, string routeId)
        {
            Runtime = runtime;
            Host = host;
            Services = runtime.Services;
            State = state;
            Scope = scope;
            RouteId = routeId;
            FrameClock = Services.FrameClock;
            Transport = Services.Transport;
        }

        public T GetNodeState<T>(RouteNode node, Func<T> create) => State.GetOrCreate(RouteId, node.NodeId, create);
        public T GetNodeState<T>(RouteNode node, string slot, Func<T> create) => State.GetOrCreate(RouteId, node.NodeId, slot, create);

        public T GetService<T>() where T : class => Services.Get<T>();

        public void Report(string code, string message, RouteNode node = null, Exception exception = null) =>
            Services.Report(code, message, RouteId, node?.NodeId, exception);
    }
}
