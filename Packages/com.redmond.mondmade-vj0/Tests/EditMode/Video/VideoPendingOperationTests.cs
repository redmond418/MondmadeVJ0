using System;
using System.Collections.Generic;
using System.Linq;
using MondmadeVJ0;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Video;

namespace MondmadeVJ0.Tests.Video
{
    public sealed class VideoPendingOperationTests
    {
        private readonly List<IDisposable> disposables = new List<IDisposable>();
        private GameObject runtimeObject;
        private GameObject playerObject;
        private MondmadeVJ0Runtime runtime;
        private VideoPlayer player;
        private ManualFrameClock clock;
        private RouteContext context;

        [SetUp]
        public void SetUp()
        {
            runtimeObject = new GameObject("Video Output Runtime");
            runtimeObject.SetActive(false);
            runtime = runtimeObject.AddComponent<MondmadeVJ0Runtime>();
            clock = new ManualFrameClock();
            var services = new RuntimeServices { Runtime = runtime, FrameClock = clock };
            services.Register<IFrameClock>(clock);
            services.Register(new FrameEndScheduler());
            runtime.Services = services;
            context = new RouteContext(runtime, null, new RuntimeStateStore(), new BindingScope(), Guid.NewGuid().ToString("N"));
            playerObject = new GameObject("Video Output Player");
            player = playerObject.AddComponent<VideoPlayer>();
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = disposables.Count - 1; i >= 0; i--) disposables[i]?.Dispose();
            context?.Scope.Dispose();
            if (playerObject != null) UnityEngine.Object.DestroyImmediate(playerObject);
            if (runtimeObject != null) UnityEngine.Object.DestroyImmediate(runtimeObject);
            disposables.Clear();
        }

        [Test]
        public void OnlyTheLatestPendingCommandRunsAfterPreparation()
        {
            var prepareCalls = 0;
            var applied = string.Empty;
            var operation = NewOperation(_ => prepareCalls++);

            operation.Submit(() => applied = "first");
            operation.Submit(() => applied = "latest");

            Assert.That(prepareCalls, Is.EqualTo(1));
            Assert.That(operation.IsPreparing, Is.True);
            Assert.That(applied, Is.Empty);
            operation.HandlePrepared(player);
            Assert.That(applied, Is.EqualTo("latest"));
            Assert.That(operation.IsPreparing, Is.False);
        }

        [Test]
        public void PreparationTimeoutFaultsAfterTenUnscaledSeconds()
        {
            var operation = NewOperation(_ => { });
            string faultCode = null;
            Exception faultException = null;
            operation.Faulted += (code, message, exception) => { faultCode = code; faultException = exception; };
            operation.Submit(() => { });

            clock.Advance(VideoPendingOperation.PrepareTimeoutSeconds - 0.01d);
            Assert.That(operation.IsFaulted, Is.False);
            clock.Advance(0.02d);

            Assert.That(operation.IsFaulted, Is.True);
            Assert.That(faultCode, Is.EqualTo("VIDEO_PREPARE_TIMEOUT"));
            Assert.That(faultException, Is.TypeOf<TimeoutException>());
        }

        [Test]
        public void PrepareErrorFaultsAndDropsThePendingCommand()
        {
            var applied = false;
            var operation = NewOperation(_ => { });
            string faultCode = null;
            string faultMessage = null;
            operation.Faulted += (code, message, exception) => { faultCode = code; faultMessage = message; };
            operation.Submit(() => applied = true);

            operation.HandleError(player, "Test media failure");
            operation.HandlePrepared(player);

            Assert.That(operation.IsFaulted, Is.True);
            Assert.That(faultCode, Is.EqualTo("VIDEO_PREPARE_FAILED"));
            Assert.That(faultMessage, Is.EqualTo("Test media failure"));
            Assert.That(applied, Is.False);
        }

        [Test]
        public void SharedPlayerPreparationKeepsValueOutputsAndOnlyTheLatestTrigger()
        {
            var prepareCalls = 0;
            var service = new VideoPreparationService(_ => prepareCalls++);
            var valueNode = new VideoPlayerTimeFloatOutput();
            var secondValueNode = new VideoPlayerNormalizedTimeFloatOutput();
            var firstTriggerNode = new VideoPlayerPlayTriggerOutput();
            var latestTriggerNode = new VideoPlayerRestartTriggerOutput();
            var value = service.Acquire(player, context, valueNode, VideoPreparationLane.Value);
            var secondValue = service.Acquire(player, context, secondValueNode, VideoPreparationLane.Value);
            var firstTrigger = service.Acquire(player, context, firstTriggerNode, VideoPreparationLane.Trigger);
            var latestTrigger = service.Acquire(player, context, latestTriggerNode, VideoPreparationLane.Trigger);
            disposables.Add(value);
            disposables.Add(secondValue);
            disposables.Add(firstTrigger);
            disposables.Add(latestTrigger);

            var applied = new List<string>();
            value.Submit(() => applied.Add("old value"));
            value.Submit(() => applied.Add("latest value"));
            secondValue.Submit(() => applied.Add("second value"));
            firstTrigger.Submit(() => applied.Add("first trigger"));
            latestTrigger.Submit(() => applied.Add("latest trigger"));

            Assert.That(prepareCalls, Is.EqualTo(1));
            Assert.That(applied, Is.Empty);
            value.HandlePrepared(player);

            Assert.That(applied, Is.EqualTo(new[] { "latest value", "second value", "latest trigger" }));
        }

        [Test]
        public void AsynchronousOutputFailureUpdatesRouteStatusAndIsUnsubscribedOnUnbind()
        {
            var hostObject = new GameObject("Async Output Host");
            try
            {
                var host = hostObject.AddComponent<RouteHost>();
                var output = new AsyncFaultOutput();
                var route = new FloatRoute();
                route.Configure("Async video output", new ConstantFloatSource { Value = 1f });
                route.Outputs.Add(output);
                host.AddRoute(route);
                host.Bind(runtime, new RuntimeStateStore());
                Assert.That(route.GetOutputStatus(0), Is.EqualTo("Ready"));

                output.Raise("VIDEO_PREPARE_TIMEOUT", "Video preparation timed out.");
                Assert.That(route.GetOutputStatus(0), Is.EqualTo("Faulted"));
                Assert.That(runtime.Services.GetDiagnostics().Any(item => item.Code == "VIDEO_PREPARE_TIMEOUT" && item.RouteId == route.Id), Is.True);
                var diagnosticCount = runtime.Services.GetDiagnostics().Count;

                host.Unbind();
                output.Raise("VIDEO_PREPARE_TIMEOUT", "Late event after unbind.");

                Assert.That(runtime.Services.GetDiagnostics().Count, Is.EqualTo(diagnosticCount));
            }
            finally { UnityEngine.Object.DestroyImmediate(hostObject); }
        }

        private VideoPendingOperation NewOperation(Action<VideoPlayer> prepare)
        {
            var operation = new VideoPendingOperation(player, context, new VideoPlayerRestartTriggerOutput(), prepare);
            disposables.Add(operation);
            return operation;
        }

        private sealed class ManualFrameClock : IFrameClock
        {
            public long FrameIndex { get; private set; }
            public double Time { get; private set; }
            public double UnscaledTime { get; private set; }
            public double DspTime => 0d;
            public double DeltaTime { get; private set; }
            public double UnscaledDeltaTime { get; private set; }
            public event Action<double, double> Updated;

            public void Advance(double deltaTime)
            {
                FrameIndex++;
                DeltaTime = deltaTime;
                UnscaledDeltaTime = deltaTime;
                Time += deltaTime;
                UnscaledTime += deltaTime;
                Updated?.Invoke(deltaTime, UnscaledTime);
            }
        }

        private sealed class AsyncFaultOutput : RouteNode, IValueOutput<float>, IAsyncRouteOutputFaultSource
        {
            public event Action<string, string, Exception> Faulted;
            public void Initialize(RouteContext routeContext) { }
            public void Write(float value) { }
            public void Raise(string code, string message) => Faulted?.Invoke(code, message, new TimeoutException(message));
        }
    }
}
