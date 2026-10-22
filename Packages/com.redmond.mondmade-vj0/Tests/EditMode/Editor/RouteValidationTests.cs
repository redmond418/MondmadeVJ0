using System.Reflection;
using MondmadeVJ0.Editor;
using NUnit.Framework;
using UnityEngine;

namespace MondmadeVJ0.Tests
{
    public sealed class RouteValidationTests
    {
        private GameObject signalAObject;
        private GameObject signalBObject;
        private GameObject hostAObject;
        private GameObject hostBObject;
        private SharedFloatSignal signalA;
        private SharedFloatSignal signalB;
        private RouteHost hostA;
        private RouteHost hostB;

        [SetUp]
        public void SetUp()
        {
            signalAObject = new GameObject("Signal A");
            signalBObject = new GameObject("Signal B");
            signalA = signalAObject.AddComponent<SharedFloatSignal>();
            signalB = signalBObject.AddComponent<SharedFloatSignal>();
            hostAObject = new GameObject("Route host A");
            hostBObject = new GameObject("Route host B");
            hostA = hostAObject.AddComponent<RouteHost>();
            hostB = hostBObject.AddComponent<RouteHost>();
        }

        [TearDown]
        public void TearDown()
        {
            if (hostAObject != null) Object.DestroyImmediate(hostAObject);
            if (hostBObject != null) Object.DestroyImmediate(hostBObject);
            if (signalAObject != null) Object.DestroyImmediate(signalAObject);
            if (signalBObject != null) Object.DestroyImmediate(signalBObject);
        }

        [Test]
        public void SharedSignalCycleAcrossRouteHostsIsReported()
        {
            hostA.AddRoute(CreateSignalRoute("A to B", signalA, signalB));
            hostB.AddRoute(CreateSignalRoute("B to A", signalB, signalA));

            var issues = MondmadeVJ0Validation.Validate(hostA);

            Assert.That(issues.Exists(issue => issue.Contains("feedback cycle")), Is.True);
        }

        [Test]
        public void MissingCompositeInputIsReportedBeforePlay()
        {
            var source = new FloatBinarySource();
            SetField(source, "left", new ConstantFloatSource { Value = 1f });
            var route = new FloatRoute();
            route.Configure("incomplete composite", source);
            hostA.AddRoute(route);

            var issues = MondmadeVJ0Validation.Validate(hostA);

            Assert.That(issues.Exists(issue => issue.Contains("connected node 'right' is missing")), Is.True);
        }

        [Test]
        public void MissingOutputIsReportedBeforePlay()
        {
            var route = new FloatRoute();
            route.Configure("output required", new ConstantFloatSource());
            hostA.AddRoute(route);

            var issues = MondmadeVJ0Validation.Validate(hostA);

            Assert.That(issues.Exists(issue => issue.Contains("output required") && issue.Contains("no Output is assigned")), Is.True);
        }

        [Test]
        public void ManagedReferenceClonePreservesUnityObjectReferences()
        {
            var target = new GameObject("Clone target");
            try
            {
                var renderer = target.AddComponent<MeshRenderer>();
                var output = new RendererEnabledBoolOutput();
                SetField(output, "target", renderer);
                var cloneMethod = typeof(RouteHostEditor).GetMethod("CloneManagedReference", BindingFlags.Static | BindingFlags.NonPublic);
                var clone = (RendererEnabledBoolOutput)cloneMethod.Invoke(null, new object[] { output });
                var copiedTarget = (Renderer)typeof(RendererEnabledBoolOutput).GetField("target", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(clone);

                Assert.That(copiedTarget, Is.SameAs(renderer));
            }
            finally { Object.DestroyImmediate(target); }
        }

        [Test]
        public void SmoothDampUnboundedMaxSpeedIsValid()
        {
            var route = new FloatRoute();
            route.Configure("Unbounded SmoothDamp", new ConstantFloatSource());
            route.Modifiers.Add(new SmoothDampFloatModifier());
            route.Outputs.Add(new DebugFloatOutput());
            hostA.AddRoute(route);

            var issues = MondmadeVJ0Validation.Validate(hostA);

            Assert.That(issues, Is.Empty);
        }

        private static FloatRoute CreateSignalRoute(string name, SharedFloatSignal input, SharedFloatSignal outputSignal)
        {
            var route = new FloatRoute();
            route.Configure(name, new SignalFloatSource { Signal = input });
            var output = new SignalFloatOutput();
            SetField(output, "signal", outputSignal);
            route.Outputs.Add(output);
            return route;
        }

        private static void SetField(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
