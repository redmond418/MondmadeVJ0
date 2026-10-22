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
        public void DuplicateStableIdsAndInvalidRangesAreReportedBeforePlay()
        {
            var first = new FloatRoute();
            first.Configure("first route", new ConstantFloatSource());
            first.Outputs.Add(new DebugFloatOutput());
            hostA.AddRoute(first);

            var second = new FloatRoute();
            var clamp = new ClampFloatModifier();
            SetField(clamp, "minimum", 2f);
            SetField(clamp, "maximum", 1f);
            second.Configure("invalid range route", new ConstantFloatSource());
            second.Modifiers.Add(clamp);
            second.Outputs.Add(new DebugFloatOutput());
            hostA.AddRoute(second);
            SetField(second, "id", first.Id);

            var issues = MondmadeVJ0Validation.Validate(hostA);

            Assert.That(issues.Exists(issue => issue.Contains("duplicate route id")), Is.True);
            Assert.That(issues.Exists(issue => issue.Contains("invalid range route") && issue.Contains("minimum must not exceed maximum")), Is.True);
        }

        [Test]
        public void MissingUnityTargetIsReportedWithRouteAndNodeContext()
        {
            var route = new BoolRoute();
            route.Configure("missing renderer target", new ConstantBoolSource());
            route.Outputs.Add(new RendererEnabledBoolOutput());
            hostA.AddRoute(route);

            var issues = MondmadeVJ0Validation.Validate(hostA);

            Assert.That(issues.Exists(issue => issue.Contains("missing renderer target") && issue.Contains("required reference 'target' is missing")), Is.True);
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
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field == null) continue;
                field.SetValue(target, value);
                return;
            }
            throw new System.MissingFieldException(target.GetType().FullName, name);
        }
    }
}
