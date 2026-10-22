using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

namespace MondmadeVJ0
{
    [Serializable]
    public abstract class ValueRoute<T> : IRoute
    {
        [SerializeField] private string id = Guid.NewGuid().ToString("N");
        [SerializeField] private string routeName = "Route";
        [SerializeField] private bool enabled = true;
        [SerializeReference] private IValueSource<T> source;
        [SerializeReference] private List<IValueModifier<T>> modifiers = new List<IValueModifier<T>>();
        [SerializeReference] private List<IValueOutput<T>> outputs = new List<IValueOutput<T>>();

        [NonSerialized] private BindingScope scope;
        [NonSerialized] private bool[] outputFaults;
        [NonSerialized] private string[] outputStatuses;
        [NonSerialized] private string status = "Unbound";
        [NonSerialized] private T currentValue;
        [NonSerialized] private bool hasCurrentValue;
        [NonSerialized] private RouteTraceSelection traceSelection;

        public string Id => id;
        public string Name => routeName;
        public bool IsEnabled => enabled;
        public string Status => status;
        public Type ValueType => typeof(T);
        public T CurrentValue => currentValue;
        public bool HasCurrentValue => hasCurrentValue;
        public IValueSource<T> Source { get => source; set => source = value; }
        public IList<IValueModifier<T>> Modifiers => modifiers;
        public IList<IValueOutput<T>> Outputs => outputs;
        public string GetOutputStatus(int index) => outputStatuses != null && index >= 0 && index < outputStatuses.Length ? outputStatuses[index] : "Unbound";

        public void Configure(string name, IValueSource<T> routeSource)
        {
            routeName = name;
            source = routeSource;
        }

        public void SetEnabled(bool value) => enabled = value;
        public void RegenerateId() => id = Guid.NewGuid().ToString("N");

        public void Bind(MondmadeVJ0Runtime runtime, RouteHost host, RuntimeStateStore stateStore)
        {
            Unbind();
            if (!enabled) { status = "Disabled"; return; }
            scope = new BindingScope();
            try
            {
                if (source == null) throw new InvalidOperationException($"Route '{routeName}' has no Source.");
                var context = new RouteContext(runtime, host, stateStore, scope, id);
                servicesFromContext = runtime.Services;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                traceSelection = runtime.Services.GetRouteTraceSelection(id);
#else
                traceSelection = null;
#endif
                var outputCount = outputs?.Count ?? 0;
                outputFaults = new bool[outputCount];
                outputStatuses = new string[outputCount];
                for (var i = 0; i < outputCount; i++)
                {
                    try
                    {
                        if (outputs[i] == null) throw new InvalidOperationException($"Output {i} is missing.");
                        outputs[i].Initialize(context);
                        outputStatuses[i] = "Ready";
                    }
                    catch (Exception ex)
                    {
                        outputFaults[i] = true;
                        outputStatuses[i] = "Faulted";
                        runtime.Services.Report("OUTPUT_INIT", $"Output {i} failed to initialize on route '{routeName}'.", id, (outputs[i] as RouteNode)?.NodeId, ex);
                    }
                }

                var stream = source.Build(context) ?? throw new InvalidOperationException("Source returned a null stream.");
                var sourceNode = source as RouteNode;
                stream = runtime.Services.TraceRoute(stream, traceSelection, routeName, RuntimeTraceStage.SourceRaw,
                    -1, sourceNode?.NodeId, sourceNode?.GetType().Name ?? source.GetType().Name);
                if (modifiers != null)
                {
                    for (var i = 0; i < modifiers.Count; i++)
                    {
                        var modifier = modifiers[i] ?? throw new InvalidOperationException($"Modifier {i} is missing.");
                        stream = modifier.Apply(stream, context) ?? throw new InvalidOperationException($"Modifier {i} returned a null stream.");
                        var modifierNode = modifier as RouteNode;
                        stream = runtime.Services.TraceRoute(stream, traceSelection, routeName, RuntimeTraceStage.Modifier,
                            i, modifierNode?.NodeId, modifierNode?.GetType().Name ?? modifier.GetType().Name);
                    }
                }
                status = "Active";
                scope.Add(stream.Subscribe(
                    WriteValue,
                    exception => Fault(runtime, "ROUTE_STREAM", exception),
                    result => { if (result.IsFailure) Fault(runtime, "ROUTE_STREAM", result.Exception); }));
            }
            catch (Exception ex)
            {
                Fault(runtime, "ROUTE_BIND", ex);
            }
        }

        private static string ExceptionNodeId(object node) => (node as RouteNode)?.NodeId;

        private void WriteValue(T value)
        {
            if (status == "Faulted") return;
            var measure = ServicesFromContext?.MonitorCaptureEnabled == true;
            var started = measure ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            currentValue = value;
            hasCurrentValue = true;
            if (measure && traceSelection != null && traceSelection.IsSelected)
                ServicesFromContext.RecordRouteValue(id, routeName, RuntimeTraceStage.Final, -1, null, null, value, null);
            for (var i = 0; outputs != null && i < outputs.Count; i++)
            {
                if (outputFaults != null && i < outputFaults.Length && outputFaults[i]) continue;
                try
                {
                    outputs[i]?.Write(value);
                    if (measure && traceSelection != null && traceSelection.IsSelected)
                        ServicesFromContext.RecordRouteValue(id, routeName, RuntimeTraceStage.Output, i,
                            ExceptionNodeId(outputs[i]), outputs[i]?.GetType().Name, value, outputStatuses != null && i < outputStatuses.Length ? outputStatuses[i] : "Ready");
                }
                catch (Exception ex)
                {
                    if (outputFaults != null && i < outputFaults.Length) outputFaults[i] = true;
                    if (outputStatuses != null && i < outputStatuses.Length) outputStatuses[i] = "Faulted";
                    ServicesFromContext?.Report("OUTPUT_WRITE", $"Output {i} failed on route '{routeName}'.", id, ExceptionNodeId(outputs[i]), ex);
                    if (measure && traceSelection != null && traceSelection.IsSelected)
                        ServicesFromContext.RecordRouteValue(id, routeName, RuntimeTraceStage.Output, i,
                            ExceptionNodeId(outputs[i]), outputs[i]?.GetType().Name, value, "Faulted");
                }
            }
            if (measure) ServicesFromContext.RecordRouteProcessing(System.Diagnostics.Stopwatch.GetTimestamp() - started);
        }

        [NonSerialized] private RuntimeServices servicesFromContext;
        private RuntimeServices ServicesFromContext => servicesFromContext;

        private void Fault(MondmadeVJ0Runtime runtime, string code, Exception exception)
        {
            status = "Faulted";
            runtime.Services.Report(code, $"Route '{routeName}' stopped after a pipeline failure.", id, exception: exception);
            scope?.Dispose();
        }

        public void Unbind()
        {
            scope?.Dispose();
            scope = null;
            outputFaults = null;
            outputStatuses = null;
            servicesFromContext = null;
            traceSelection = null;
            if (status != "Faulted") status = enabled ? "Unbound" : "Disabled";
        }
    }

    [Serializable] public sealed class FloatRoute : ValueRoute<float> { }
    [Serializable] public sealed class IntRoute : ValueRoute<int> { }
    [Serializable] public sealed class BoolRoute : ValueRoute<bool> { }
    [Serializable] public sealed class Vector2Route : ValueRoute<Vector2> { }
    [Serializable] public sealed class Vector3Route : ValueRoute<Vector3> { }
    [Serializable] public sealed class Vector4Route : ValueRoute<Vector4> { }
    [Serializable] public sealed class ColorRoute : ValueRoute<Color> { }

    [Serializable]
    public sealed class TriggerRoute : IRoute
    {
        [SerializeField] private string id = Guid.NewGuid().ToString("N");
        [SerializeField] private string routeName = "Trigger Route";
        [SerializeField] private bool enabled = true;
        [SerializeReference] private ITriggerSource source;
        [SerializeReference] private List<ITriggerModifier> modifiers = new List<ITriggerModifier>();
        [SerializeReference] private List<ITriggerOutput> outputs = new List<ITriggerOutput>();
        [NonSerialized] private BindingScope scope;
        [NonSerialized] private bool[] outputFaults;
        [NonSerialized] private string[] outputStatuses;
        [NonSerialized] private string status = "Unbound";
        [NonSerialized] private RouteTraceSelection traceSelection;

        public string Id => id;
        public string Name => routeName;
        public bool IsEnabled => enabled;
        public string Status => status;
        public Type ValueType => typeof(Unit);
        public ITriggerSource Source { get => source; set => source = value; }
        public IList<ITriggerModifier> Modifiers => modifiers;
        public IList<ITriggerOutput> Outputs => outputs;
        public string GetOutputStatus(int index) => outputStatuses != null && index >= 0 && index < outputStatuses.Length ? outputStatuses[index] : "Unbound";

        public void Configure(string name, ITriggerSource routeSource) { routeName = name; source = routeSource; }
        public void SetEnabled(bool value) => enabled = value;
        public void RegenerateId() => id = Guid.NewGuid().ToString("N");

        public void Bind(MondmadeVJ0Runtime runtime, RouteHost host, RuntimeStateStore stateStore)
        {
            Unbind();
            if (!enabled) { status = "Disabled"; return; }
            scope = new BindingScope();
            try
            {
                if (source == null) throw new InvalidOperationException($"Route '{routeName}' has no Source.");
                var context = new RouteContext(runtime, host, stateStore, scope, id);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                traceSelection = runtime.Services.GetRouteTraceSelection(id);
#else
                traceSelection = null;
#endif
                outputFaults = new bool[outputs?.Count ?? 0];
                outputStatuses = new string[outputFaults.Length];
                for (var i = 0; i < outputFaults.Length; i++)
                {
                    try
                    {
                        if (outputs[i] == null) throw new InvalidOperationException($"Output {i} is missing.");
                        outputs[i].Initialize(context);
                        outputStatuses[i] = "Ready";
                    }
                    catch (Exception ex)
                    {
                        outputFaults[i] = true;
                        outputStatuses[i] = "Faulted";
                        runtime.Services.Report("OUTPUT_INIT", $"Output {i} failed to initialize on route '{routeName}'.", id, (outputs[i] as RouteNode)?.NodeId, ex);
                    }
                }
                var stream = source.Build(context) ?? throw new InvalidOperationException("Trigger source returned a null stream.");
                var sourceNode = source as RouteNode;
                stream = runtime.Services.TraceRoute(stream, traceSelection, routeName, RuntimeTraceStage.TriggerRaw,
                    -1, sourceNode?.NodeId, sourceNode?.GetType().Name ?? source.GetType().Name);
                for (var i = 0; modifiers != null && i < modifiers.Count; i++)
                {
                    var modifier = modifiers[i] ?? throw new InvalidOperationException($"Modifier {i} is missing.");
                    stream = modifier.Apply(stream, context);
                    var modifierNode = modifier as RouteNode;
                    stream = runtime.Services.TraceRoute(stream, traceSelection, routeName, RuntimeTraceStage.TriggerModifier,
                        i, modifierNode?.NodeId, modifierNode?.GetType().Name ?? modifier.GetType().Name);
                }
                status = "Active";
                scope.Add(stream.Subscribe(
                    _ => InvokeOutputs(runtime),
                    exception => Fault(runtime, exception),
                    result => { if (result.IsFailure) Fault(runtime, result.Exception); }));
            }
            catch (Exception ex) { Fault(runtime, ex); }
        }

        private void InvokeOutputs(MondmadeVJ0Runtime runtime)
        {
            if (status == "Faulted") return;
            var measure = runtime.Services.MonitorCaptureEnabled;
            var started = measure ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            for (var i = 0; outputs != null && i < outputs.Count; i++)
            {
                if (outputFaults != null && i < outputFaults.Length && outputFaults[i]) continue;
                try { outputs[i]?.Invoke(); }
                catch (Exception ex)
                {
                    if (outputFaults != null && i < outputFaults.Length) outputFaults[i] = true;
                    if (outputStatuses != null && i < outputStatuses.Length) outputStatuses[i] = "Faulted";
                    runtime.Services.Report("OUTPUT_INVOKE", $"Output {i} failed on route '{routeName}'.", id, exception: ex);
                }
                finally
                {
                    if (runtime.Services.MonitorCaptureEnabled && traceSelection != null && traceSelection.IsSelected)
                        runtime.Services.RecordRouteValue(id, routeName, RuntimeTraceStage.TriggerOutput, i,
                            (outputs[i] as RouteNode)?.NodeId, outputs[i]?.GetType().Name, Unit.Default,
                            outputStatuses != null && i < outputStatuses.Length ? outputStatuses[i] : "Ready");
                }
            }
            if (measure) runtime.Services.RecordRouteProcessing(System.Diagnostics.Stopwatch.GetTimestamp() - started);
        }

        private void Fault(MondmadeVJ0Runtime runtime, Exception ex)
        {
            status = "Faulted";
            runtime.Services.Report("ROUTE_STREAM", $"Route '{routeName}' stopped after a trigger pipeline failure.", id, exception: ex);
            scope?.Dispose();
        }

        public void Unbind()
        {
            scope?.Dispose();
            scope = null;
            outputFaults = null;
            outputStatuses = null;
            traceSelection = null;
            if (status != "Faulted") status = enabled ? "Unbound" : "Disabled";
        }
    }
}
