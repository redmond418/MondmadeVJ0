using System;
using System.Collections.Generic;
using UnityEngine;

namespace MondmadeVJ0
{
    public sealed class RouteHost : MonoBehaviour
    {
        [SerializeReference] private List<IRoute> routes = new List<IRoute>();
        private readonly List<IRoute> boundRoutes = new List<IRoute>();
        private MondmadeVJ0Runtime runtime;
        private RuntimeStateStore stateStore;

        public IReadOnlyList<IRoute> Routes => routes;
        public int Count => routes?.Count ?? 0;

        public bool AddRoute(IRoute route)
        {
            if (route == null) throw new ArgumentNullException(nameof(route));
            if (routes == null) routes = new List<IRoute>();
            if (routes.Exists(item => item != null && item.Id == route.Id)) return false;
            routes.Add(route);
            if (runtime != null && route.IsEnabled)
            {
                try
                {
                    route.Bind(runtime, this, stateStore);
                    boundRoutes.Add(route);
                }
                catch (Exception exception)
                {
                    runtime.Services.Report("ROUTE_BIND", $"Route '{route.Name}' failed to bind.", route.Id, exception: exception);
                }
            }
            return true;
        }

        public bool RemoveRoute(string routeId)
        {
            if (routes == null) return false;
            var route = routes.Find(item => item != null && item.Id == routeId);
            if (route == null) return false;
            route.Unbind();
            boundRoutes.Remove(route);
            stateStore?.RemoveRoute(route.Id);
            return routes.Remove(route);
        }

        public void SetRouteEnabled(string routeId, bool enabled)
        {
            var route = routes?.Find(item => item != null && item.Id == routeId);
            if (route == null || route.IsEnabled == enabled) return;
            route.SetEnabled(enabled);
            route.Unbind();
            boundRoutes.Remove(route);
            if (enabled && runtime != null)
            {
                try
                {
                    route.Bind(runtime, this, stateStore);
                    boundRoutes.Add(route);
                }
                catch (Exception exception)
                {
                    runtime.Services.Report("ROUTE_BIND", $"Route '{route.Name}' failed to bind.", route.Id, exception: exception);
                }
            }
        }

        internal void Bind(MondmadeVJ0Runtime runtime, RuntimeStateStore stateStore)
        {
            Unbind();
            this.runtime = runtime;
            this.stateStore = stateStore;
            if (routes == null) return;
            for (var i = 0; i < routes.Count; i++)
            {
                var route = routes[i];
                if (route == null || !route.IsEnabled) continue;
                try
                {
                    route.Bind(runtime, this, stateStore);
                    boundRoutes.Add(route);
                }
                catch (Exception ex) { runtime.Services.Report("ROUTE_BIND", $"Route '{route.Name}' failed to bind.", route.Id, exception: ex); }
            }
        }

        internal void RetryFaultedRoutes(MondmadeVJ0Runtime runtime, RuntimeStateStore stateStore)
        {
            foreach (var route in routes)
            {
                if (route == null || !route.IsEnabled || route.Status != "Faulted") continue;
                route.Unbind();
                try { route.Bind(runtime, this, stateStore); boundRoutes.Add(route); }
                catch (Exception ex) { runtime.Services.Report("ROUTE_RETRY", $"Route '{route.Name}' retry failed.", route.Id, exception: ex); }
            }
        }

        internal void Unbind()
        {
            for (var i = boundRoutes.Count - 1; i >= 0; i--) boundRoutes[i]?.Unbind();
            boundRoutes.Clear();
        }

        private void OnDestroy()
        {
            Unbind();
            if (routes != null && stateStore != null)
                foreach (var route in routes)
                    if (route != null) stateStore.RemoveRoute(route.Id);
            runtime = null;
            stateStore = null;
        }
    }
}
