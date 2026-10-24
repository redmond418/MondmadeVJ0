using System;
using System.Collections.Generic;
using UnityEngine;

namespace MondmadeVJ0
{
    [DisallowMultipleComponent]
    public sealed class MondmadeVJ0Runtime : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour[] modules = Array.Empty<MonoBehaviour>();
        [SerializeField] private RouteHost[] routeHosts = Array.Empty<RouteHost>();

        private readonly FrameClock frameClock = new FrameClock();
        private readonly RuntimeStateStore stateStore = new RuntimeStateStore();
        private readonly List<IRuntimeModule> activeModules = new List<IRuntimeModule>();
        private bool initialized;

        public RuntimeServices Services { get; internal set; }
        public IFrameClock Clock => frameClock;
        public int FrameSubscriberCount => frameClock.SubscriberCount;
        public IReadOnlyList<MonoBehaviour> ConfiguredModules => modules;
        public IReadOnlyList<RouteHost> RouteHosts => routeHosts;

        private void OnEnable() => InitializeRuntime();

        private void InitializeRuntime()
        {
            if (initialized) return;
            initialized = true;
            Services = new RuntimeServices { Runtime = this };
            Services.Register<IFrameClock>(frameClock);
            Services.Register(frameClock);
            Services.Register(new FrameEndScheduler());
            activeModules.Clear();

            for (var i = 0; modules != null && i < modules.Length; i++)
            {
                if (!(modules[i] is IRuntimeModule module))
                {
                    if (modules[i] != null) Services.Report("MODULE_TYPE", $"{modules[i].name} does not implement IRuntimeModule.");
                    continue;
                }
                try
                {
                    module.Register(Services);
                    activeModules.Add(module);
                }
                catch (Exception ex) { Services.Report("MODULE_REGISTER", $"Failed to register {module.ModuleName}.", exception: ex); }
            }

            for (var i = 0; i < activeModules.Count; i++)
            {
                try { activeModules[i].Initialize(Services); }
                catch (Exception ex)
                {
                    var module = activeModules[i];
                    Services.Report("MODULE_INIT", $"Failed to initialize {module.ModuleName}.", exception: ex);
                    try { module.Shutdown(Services); }
                    catch (Exception shutdownException) { Services.Report("MODULE_SHUTDOWN", $"Failed to roll back {module.ModuleName}.", exception: shutdownException); }
                    activeModules.RemoveAt(i--);
                }
            }

            for (var i = 0; routeHosts != null && i < routeHosts.Length; i++)
            {
                if (routeHosts[i] == null) continue;
                try { routeHosts[i].Bind(this, stateStore); }
                catch (Exception ex) { Services.Report("HOST_BIND", $"Failed to bind RouteHost {routeHosts[i].name}.", exception: ex); }
            }
        }

        private void Update()
        {
            if (!initialized) return;
            frameClock.BeginFrame();
            for (var i = 0; i < activeModules.Count; i++) SafeModuleCall(activeModules[i], "INPUT", m => m.DrainInputQueue(Services, 4096));
            for (var i = 0; i < activeModules.Count; i++) SafeModuleCall(activeModules[i], "TICK", m => m.Tick(Services, frameClock.UnscaledDeltaTime));
            frameClock.NotifyUpdated(Services);
            try { Services.Get<FrameEndScheduler>().Flush(Services); }
            catch (Exception ex) { Services.Report("FRAME_END", "Frame-end trigger aggregation failed.", exception: ex); }
            for (var i = 0; i < activeModules.Count; i++) SafeModuleCall(activeModules[i], "FLUSH", m => m.FlushFrame(Services));
        }

        private void SafeModuleCall(IRuntimeModule module, string phase, Action<IRuntimeModule> action)
        {
            try { action(module); }
            catch (Exception ex) { Services.Report("MODULE_" + phase, $"{module.ModuleName} failed during {phase}.", exception: ex); }
        }

        public void RetryFaultedRoutes()
        {
            if (!initialized) return;
            if (routeHosts == null) return;
            foreach (var host in routeHosts) host?.RetryFaultedRoutes(this, stateStore);
        }

        public void ResetAllState()
        {
            if (routeHosts != null)
                foreach (var host in routeHosts) host?.Unbind();
            stateStore.Clear();
            if (routeHosts != null)
                for (var i = 0; i < routeHosts.Length; i++) routeHosts[i]?.Bind(this, stateStore);
        }

        private void OnDisable() => ShutdownRuntime(clearState: false);

        private void OnDestroy() => ShutdownRuntime(clearState: true);

        private void ShutdownRuntime(bool clearState)
        {
            if (!initialized) return;
            if (routeHosts != null)
                for (var i = routeHosts.Length - 1; i >= 0; i--) routeHosts[i]?.Unbind();
            for (var i = activeModules.Count - 1; i >= 0; i--)
            {
                try { activeModules[i].Shutdown(Services); }
                catch (Exception ex) { Services.Report("MODULE_SHUTDOWN", $"Failed to shut down {activeModules[i].ModuleName}.", exception: ex); }
            }
            activeModules.Clear();
            Services?.ClearServices();
            initialized = false;
            if (clearState) stateStore.Clear();
        }
    }

}
