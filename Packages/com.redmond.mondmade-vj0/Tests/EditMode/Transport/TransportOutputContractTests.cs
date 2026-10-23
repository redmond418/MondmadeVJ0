using System;
using System.Reflection;
using MondmadeVJ0;
using NUnit.Framework;
using UnityEngine;

namespace MondmadeVJ0.Tests.Transport
{
    public sealed class TransportOutputContractTests
    {
        private GameObject runtimeObject;
        private GameObject transportObject;
        private MondmadeVJ0Runtime runtime;
        private MusicalTransport transport;
        private RuntimeServices services;
        private ManualFrameClock clock;
        private BindingScope scope;
        private RouteContext context;

        [SetUp]
        public void SetUp()
        {
            runtimeObject = new GameObject("Transport Output Runtime");
            runtimeObject.SetActive(false);
            runtime = runtimeObject.AddComponent<MondmadeVJ0Runtime>();
            clock = new ManualFrameClock();
            services = new RuntimeServices { Runtime = runtime };
            services.Register<IFrameClock>(clock);
            services.Register(clock);
            services.Register(new FrameEndScheduler());

            transportObject = new GameObject("Transport Output Module");
            transport = transportObject.AddComponent<MusicalTransport>();
            transport.Register(services);
            transport.Initialize(services);
            runtime.Services = services;
            scope = new BindingScope();
            context = new RouteContext(runtime, null, new RuntimeStateStore(), scope, "transport-output-test");
        }

        [TearDown]
        public void TearDown()
        {
            scope?.Dispose();
            transport?.Shutdown(services);
            if (transportObject != null) UnityEngine.Object.DestroyImmediate(transportObject);
            if (runtimeObject != null) UnityEngine.Object.DestroyImmediate(runtimeObject);
        }

        [Test]
        public void TempoOutputsWriteBaseBpmAndMultiplier()
        {
            var bpm = new TransportBpmFloatOutput();
            bpm.Initialize(context);
            bpm.Write(96f);
            Assert.That(transport.BaseBpm, Is.EqualTo(96d));

            var multiplier = new TransportMultiplierFloatOutput();
            multiplier.Initialize(context);
            multiplier.Write(1.25f);
            Assert.That(transport.Multiplier, Is.EqualTo(1.25d).Within(1e-7d));
            Assert.That(transport.EffectiveBpm, Is.EqualTo(120d).Within(1e-6d));
        }

        [Test]
        public void EveryTransportOutputRejectsAnUnregisteredTransportService()
        {
            var registeredTransport = services.Transport;
            services.Transport = null;
            var missingServiceContext = new RouteContext(runtime, null, new RuntimeStateStore(), scope, "transport-output-missing-service-test");

            try
            {
                AssertMissingTransport(new TransportTapTriggerOutput(), missingServiceContext);
                AssertMissingTransport(new TransportResetPhaseTriggerOutput(), missingServiceContext);
                AssertMissingTransport(new TransportNudgeForwardTriggerOutput(), missingServiceContext);
                AssertMissingTransport(new TransportNudgeBackwardTriggerOutput(), missingServiceContext);
                AssertMissingTransport(new TransportBpmFloatOutput(), missingServiceContext);
                AssertMissingTransport(new TransportMultiplierFloatOutput(), missingServiceContext);
            }
            finally
            {
                services.Transport = registeredTransport;
            }
        }

        [Test]
        public void TriggerOutputsNudgeResetAndTapUsingUnscaledFrameTime()
        {
            transport.Seek(1d);
            var forward = new TransportNudgeForwardTriggerOutput();
            var backward = new TransportNudgeBackwardTriggerOutput();
            forward.Initialize(context);
            backward.Initialize(context);

            forward.Invoke();
            Assert.That(transport.BeatPosition, Is.EqualTo(1d + 1d / 32d).Within(1e-9d));
            backward.Invoke();
            Assert.That(transport.BeatPosition, Is.EqualTo(1d).Within(1e-9d));

            var tap = new TransportTapTriggerOutput();
            tap.Initialize(context);
            clock.UnscaledTime = 0.5d;
            tap.Invoke();
            clock.UnscaledTime = 1d;
            tap.Invoke();
            Assert.That(transport.BaseBpm, Is.EqualTo(120d).Within(1e-6d));

            var reset = new TransportResetPhaseTriggerOutput();
            reset.Initialize(context);
            reset.Invoke();
            Assert.That(transport.BeatPosition, Is.EqualTo(0d));
        }

        private static void AssertMissingTransport(RouteNode output, RouteContext context)
        {
            var initialize = output.GetType().GetMethod("Initialize", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(initialize, Is.Not.Null, output.GetType().Name);
            var exception = Assert.Throws<TargetInvocationException>(() => initialize.Invoke(output, new object[] { context }), output.GetType().Name);
            Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>(), output.GetType().Name);
            Assert.That(exception.InnerException.Message, Does.Contain("Transport service is not registered"), output.GetType().Name);
        }

        private sealed class ManualFrameClock : IFrameClock
        {
            public long FrameIndex { get; private set; }
            public double Time { get; private set; }
            public double UnscaledTime { get; set; }
            public double DspTime => 0d;
            public double DeltaTime { get; private set; }
            public double UnscaledDeltaTime { get; private set; }
            public event Action<double, double> Updated;
        }
    }
}
