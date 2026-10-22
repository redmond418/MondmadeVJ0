using System;
using System.Collections.Generic;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace MondmadeVJ0
{
    public sealed class RuntimeServices
    {
        private readonly Dictionary<Type, object> services = new Dictionary<Type, object>();
        private readonly List<RuntimeDiagnostic> diagnostics = new List<RuntimeDiagnostic>(4096);
        private readonly Stopwatch lifetime = Stopwatch.StartNew();
        private readonly Dictionary<string, DiagnosticThrottle> throttles = new Dictionary<string, DiagnosticThrottle>();
        private int diagnosticStart;
        public long DroppedDiagnosticCount { get; private set; }

        public IFrameClock FrameClock { get; internal set; }
        public IMusicalTransport Transport { get; internal set; }
        public MondmadeVJ0Runtime Runtime { get; internal set; }
        public IReadOnlyList<RuntimeDiagnostic> Diagnostics => diagnostics;

        public void Register<T>(T service) where T : class
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            var type = typeof(T);
            if (services.TryGetValue(type, out var existing) && !ReferenceEquals(existing, service))
                throw new InvalidOperationException($"Service {type.FullName} is already registered.");
            services[type] = service;
            if (service is IMusicalTransport transport) Transport = transport;
            if (service is IFrameClock clock) FrameClock = clock;
        }

        public bool TryGet<T>(out T service) where T : class
        {
            if (services.TryGetValue(typeof(T), out var value))
            {
                service = (T)value;
                return true;
            }
            service = null;
            return false;
        }

        public T Get<T>() where T : class
        {
            if (TryGet<T>(out var service)) return service;
            throw new InvalidOperationException($"Required service {typeof(T).FullName} is not registered.");
        }

        public void ClearServices()
        {
            services.Clear();
            FrameClock = null;
            Transport = null;
            Runtime = null;
        }

        public void Report(string code, string message, string routeId = null, string nodeId = null, Exception exception = null)
        {
            var diagnostic = new RuntimeDiagnostic(DateTime.UtcNow, code, message, routeId, nodeId, exception?.ToString());
            if (diagnostics.Count == 4096)
            {
                diagnostics[diagnosticStart] = diagnostic;
                diagnosticStart = (diagnosticStart + 1) % diagnostics.Count;
                DroppedDiagnosticCount++;
            }
            else diagnostics.Add(diagnostic);

            var key = $"{code}/{routeId}/{nodeId}/{message}";
            if (!throttles.TryGetValue(key, out var throttle))
            {
                throttle = new DiagnosticThrottle { LastLogTime = lifetime.Elapsed.TotalSeconds };
                throttles.Add(key, throttle);
                LogDiagnostic(diagnostic, 1);
                return;
            }

            throttle.Count++;
            var elapsed = lifetime.Elapsed.TotalSeconds - throttle.LastLogTime;
            if (elapsed >= 5d)
            {
                LogDiagnostic(diagnostic, throttle.Count);
                throttle.Count = 1;
                throttle.LastLogTime = lifetime.Elapsed.TotalSeconds;
            }
        }

        private static void LogDiagnostic(RuntimeDiagnostic diagnostic, int repeatCount)
        {
            if (Application.isEditor || Debug.isDebugBuild)
                UnityEngine.Debug.LogWarning($"[MondmadeVJ0:{diagnostic.Code}] {diagnostic.Message}" +
                    (repeatCount > 1 ? $" (repeated {repeatCount} times in the last interval)" : string.Empty));
        }

        public IReadOnlyList<RuntimeDiagnostic> GetDiagnostics()
        {
            if (diagnosticStart == 0) return diagnostics.ToArray();
            var ordered = new RuntimeDiagnostic[diagnostics.Count];
            var tail = diagnostics.Count - diagnosticStart;
            diagnostics.CopyTo(diagnosticStart, ordered, 0, tail);
            diagnostics.CopyTo(0, ordered, tail, diagnosticStart);
            return ordered;
        }

        public IDisposable Measure() => new StopWatchScope(Stopwatch.StartNew());

        private sealed class StopWatchScope : IDisposable
        {
            private Stopwatch watch;
            public StopWatchScope(Stopwatch watch) => this.watch = watch;
            public void Dispose() { watch?.Stop(); watch = null; }
        }

        private sealed class DiagnosticThrottle
        {
            public double LastLogTime;
            public int Count = 1;
        }
    }

    [Serializable]
    public readonly struct RuntimeDiagnostic
    {
        public readonly DateTime TimeUtc;
        public readonly string Code;
        public readonly string Message;
        public readonly string RouteId;
        public readonly string NodeId;
        public readonly string Exception;

        public RuntimeDiagnostic(DateTime timeUtc, string code, string message, string routeId, string nodeId, string exception)
        {
            TimeUtc = timeUtc;
            Code = code;
            Message = message;
            RouteId = routeId;
            NodeId = nodeId;
            Exception = exception;
        }
    }
}
