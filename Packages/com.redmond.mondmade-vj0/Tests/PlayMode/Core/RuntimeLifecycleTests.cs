using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MondmadeVJ0.Tests
{
    public sealed class RuntimeLifecycleTests
    {
        [UnityTest]
        public IEnumerator DisableAndEnableRetainsStateAndDoesNotDuplicateBindings()
        {
            var signalObject = new GameObject("Lifecycle trigger");
            var signal = signalObject.AddComponent<SharedTriggerSignal>();
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var runtimeObject = new GameObject("Lifecycle runtime");
            runtimeObject.SetActive(false);
            var runtime = runtimeObject.AddComponent<MondmadeVJ0Runtime>();
            var hostObject = new GameObject("Lifecycle host");
            var host = hostObject.AddComponent<RouteHost>();
            var route = CreateToggleRoute(signal, target.GetComponent<Renderer>());
            host.AddRoute(route);
            SetField(runtime, "routeHosts", new[] { host });

            runtimeObject.SetActive(true);
            yield return null;
            Assert.That(route.HasCurrentValue, Is.True);
            Assert.That(route.CurrentValue, Is.False);
            Assert.That(target.GetComponent<Renderer>().enabled, Is.False);

            signal.Fire();
            Assert.That(route.CurrentValue, Is.True);
            Assert.That(target.GetComponent<Renderer>().enabled, Is.True);

            runtime.enabled = false;
            yield return null;
            Assert.That(route.Status, Is.EqualTo("Unbound"));
            runtime.enabled = true;
            yield return null;
            Assert.That(route.Status, Is.EqualTo("Active"));
            Assert.That(route.CurrentValue, Is.True);
            Assert.That(target.GetComponent<Renderer>().enabled, Is.True);

            signal.Fire();
            Assert.That(route.CurrentValue, Is.False);
            runtime.enabled = false;
            yield return null;
            signal.Fire();
            runtime.enabled = true;
            yield return null;
            Assert.That(route.CurrentValue, Is.False);
            Assert.That(target.GetComponent<Renderer>().enabled, Is.False);

            Object.Destroy(runtimeObject);
            Object.Destroy(hostObject);
            Object.Destroy(signalObject);
            Object.Destroy(target);
            yield return null;
        }

        private static BoolRoute CreateToggleRoute(SharedTriggerSignal signal, Renderer target)
        {
            var trigger = new SignalTriggerSource();
            SetField(trigger, "signal", signal);
            var source = new ToggleBoolSource();
            SetField(source, "trigger", trigger);
            var route = new BoolRoute();
            route.Configure("Retained Toggle", source);
            var output = new RendererEnabledBoolOutput();
            SetField(output, "target", target);
            route.Outputs.Add(output);
            return route;
        }

        private static void SetField(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
