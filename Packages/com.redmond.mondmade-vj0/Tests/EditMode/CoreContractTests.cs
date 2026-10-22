using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using R3;
using UnityEngine;

namespace MondmadeVJ0.Tests
{
    public sealed class CoreContractTests
    {
        private GameObject runtimeObject;
        private GameObject hostObject;
        private MondmadeVJ0Runtime runtime;
        private RouteHost host;
        private RuntimeStateStore state;
        private ManualFrameClock testFrameClock;

        [SetUp]
        public void SetUp()
        {
            runtimeObject = new GameObject("Mondmade VJ0 test runtime");
            runtime = runtimeObject.AddComponent<MondmadeVJ0Runtime>();
            var services = new RuntimeServices { Runtime = runtime };
            testFrameClock = new ManualFrameClock();
            services.Register<IFrameClock>(testFrameClock);
            services.Register(new FrameEndScheduler());
            runtime.Services = services;
            hostObject = new GameObject("Mondmade VJ0 test host");
            host = hostObject.AddComponent<RouteHost>();
            state = new RuntimeStateStore();
        }

        [TearDown]
        public void TearDown()
        {
            state?.Clear();
            if (hostObject != null) UnityEngine.Object.DestroyImmediate(hostObject);
            if (runtimeObject != null) UnityEngine.Object.DestroyImmediate(runtimeObject);
        }

        [TestCase(0f)]
        [TestCase(-12.5f)]
        [TestCase(1.25f)]
        public void ConstantFloatEmitsCurrentValueOnce(float value)
        {
            var values = Collect(new ConstantFloatSource { Value = value }.Build(Context()));
            CollectionAssert.AreEqual(new[] { value }, values);
        }

        [TestCase(0)]
        [TestCase(-4)]
        [TestCase(1024)]
        public void ConstantIntEmitsCurrentValueOnce(int value)
        {
            var values = Collect(new ConstantIntSource { Value = value }.Build(Context()));
            CollectionAssert.AreEqual(new[] { value }, values);
        }

        [Test]
        public void ConstantSourcesPublishInitialValuesForEveryValueType()
        {
            CollectionAssert.AreEqual(new[] { false }, Collect(new ConstantBoolSource().Build(Context())));
            CollectionAssert.AreEqual(new[] { Vector2.zero }, Collect(new ConstantVector2Source().Build(Context())));
            CollectionAssert.AreEqual(new[] { Vector3.zero }, Collect(new ConstantVector3Source().Build(Context())));
            CollectionAssert.AreEqual(new[] { Vector4.zero }, Collect(new ConstantVector4Source().Build(Context())));
            CollectionAssert.AreEqual(new[] { Color.white }, Collect(new ConstantColorSource().Build(Context())));
        }

        [Test]
        public void SharedValueSignalImmediatelyPublishesItsCurrentValue()
        {
            var signalObject = new GameObject("signal");
            try
            {
                var signal = signalObject.AddComponent<SharedFloatSignal>();
                signal.SetValue(0.7f);
                var values = Collect(signal.Stream);
                CollectionAssert.AreEqual(new[] { 0.7f }, values);
            }
            finally { UnityEngine.Object.DestroyImmediate(signalObject); }
        }

        [Test]
        public void SharedTriggerDoesNotReplayAndFiresOncePerInput()
        {
            var signalObject = new GameObject("trigger");
            try
            {
                var signal = signalObject.AddComponent<SharedTriggerSignal>();
                var values = new List<Unit>();
                using (signal.Stream.Subscribe(values.Add))
                {
                    Assert.That(values, Is.Empty);
                    signal.Fire();
                    Assert.That(values, Has.Count.EqualTo(1));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(signalObject); }
        }

        [Test]
        public void TriggerRouteDoesNotInvokeOutputOnBindOrRebind()
        {
            var source = new TestTriggerSource();
            var output = new RecordingTriggerOutput();
            var route = new TriggerRoute();
            route.Configure("trigger no replay", source);
            route.Outputs.Add(output);
            route.Bind(runtime, host, state);
            Assert.That(output.InvocationCount, Is.Zero);
            route.Unbind();
            route.Bind(runtime, host, state);
            Assert.That(output.InvocationCount, Is.Zero);

            source.Fire();
            Assert.That(output.InvocationCount, Is.EqualTo(1));
            route.Unbind();
        }

        [Test]
        public void SelectSourceIgnoresUpdatesFromTheUnselectedBranch()
        {
            var condition = new TestBoolSource();
            var whenTrue = new TestFloatSource();
            var whenFalse = new TestFloatSource();
            var source = new FloatSelectSource();
            SetField(source, "condition", condition);
            SetField(source, "whenTrue", whenTrue);
            SetField(source, "whenFalse", whenFalse);
            var values = new List<float>();

            using (source.Build(Context()).Subscribe(values.Add))
            {
                condition.Emit(true);
                whenTrue.Emit(10f);
                whenFalse.Emit(20f);
                whenFalse.Emit(21f);
                whenTrue.Emit(11f);
                condition.Emit(false);
                whenTrue.Emit(12f);
                whenFalse.Emit(22f);
            }

            CollectionAssert.AreEqual(new[] { 10f, 11f, 21f, 22f }, values);
        }

        [Test]
        public void MostRecentWaitsForEveryInitialValueThenTracksTheLastChangedInput()
        {
            var first = new TestFloatSource();
            var second = new TestFloatSource();
            var source = new MostRecentFloatSource();
            SetField(source, "inputs", new List<IValueSource<float>> { first, second });
            var values = new List<float>();

            using (source.Build(Context()).Subscribe(values.Add))
            {
                first.Emit(1f);
                Assert.That(values, Is.Empty);
                second.Emit(2f);
                second.Emit(3f);
                first.Emit(4f);
            }

            CollectionAssert.AreEqual(new[] { 1f, 3f, 4f }, values);
        }

        [Test]
        public void AnyTriggerCoalescesInputsUntilFrameEnd()
        {
            var first = new TestTriggerSource();
            var second = new TestTriggerSource();
            var source = new AnyTriggerSource();
            SetField(source, "inputs", new List<ITriggerSource> { first, second });
            var count = 0;

            using (source.Build(Context()).Subscribe(_ => count++))
            {
                first.Fire();
                second.Fire();
                Assert.That(count, Is.Zero);
                runtime.Services.Get<FrameEndScheduler>().Flush(runtime.Services);
                Assert.That(count, Is.EqualTo(1));
                second.Fire();
                runtime.Services.Get<FrameEndScheduler>().Flush(runtime.Services);
                Assert.That(count, Is.EqualTo(2));
            }
        }

        [Test]
        public void AllTriggerRequiresEachInputInsideTheConfiguredWindow()
        {
            var first = new TestTriggerSource();
            var second = new TestTriggerSource();
            var source = new AllTriggerSource();
            SetField(source, "inputs", new List<ITriggerSource> { first, second });
            SetField(source, "windowSeconds", 0.1f);
            var count = 0;

            using (source.Build(Context()).Subscribe(_ => count++))
            {
                first.Fire();
                testFrameClock.SetUnscaledTime(0.05d);
                second.Fire();
                Assert.That(count, Is.EqualTo(1));

                first.Fire();
                testFrameClock.SetUnscaledTime(0.2d);
                second.Fire();
                Assert.That(count, Is.EqualTo(1), "Inputs outside the configured window must not complete the chord.");

                first.Fire();
                testFrameClock.SetUnscaledTime(0.25d);
                second.Fire();
                Assert.That(count, Is.EqualTo(2));
            }
        }

        [Test]
        public void ValueRouteEvaluatesSourceOnceAndFansOutSameInitialValue()
        {
            var source = new CountingSource(5.25f);
            var first = new RecordingFloatOutput();
            var second = new RecordingFloatOutput();
            var route = new FloatRoute();
            route.Configure("fan out", source);
            route.Outputs.Add(first);
            route.Outputs.Add(second);
            route.Bind(runtime, host, state);
            try
            {
                Assert.That(source.BuildCount, Is.EqualTo(1));
                Assert.That(first.InitializedBeforeWrite, Is.True);
                Assert.That(second.InitializedBeforeWrite, Is.True);
                CollectionAssert.AreEqual(new[] { 5.25f }, first.Values);
                CollectionAssert.AreEqual(new[] { 5.25f }, second.Values);
                Assert.That(route.HasCurrentValue, Is.True);
                Assert.That(route.CurrentValue, Is.EqualTo(5.25f));
            }
            finally { route.Unbind(); }
        }

        [Test]
        public void RouteDisableDisposesItsBindingAndReenableCanBindAgain()
        {
            var source = new CountingSource(2f);
            var output = new RecordingFloatOutput();
            var route = new FloatRoute();
            route.Configure("lifecycle", source);
            route.Outputs.Add(output);
            route.Bind(runtime, host, state);
            route.Unbind();
            route.Bind(runtime, host, state);
            try
            {
                Assert.That(source.BuildCount, Is.EqualTo(2));
                Assert.That(output.Values, Is.EqualTo(new[] { 2f, 2f }));
                Assert.That(route.Status, Is.EqualTo("Active"));
            }
            finally { route.Unbind(); }
        }

        [Test]
        public void OutputFailureDoesNotStopOtherOutputsOnTheSameRoute()
        {
            var route = new FloatRoute();
            route.Configure("isolated outputs", new CountingSource(3.5f));
            route.Outputs.Add(new ThrowingFloatOutput());
            var healthy = new RecordingFloatOutput();
            route.Outputs.Add(healthy);
            route.Bind(runtime, host, state);
            try
            {
                Assert.That(route.Status, Is.EqualTo("Active"));
                Assert.That(route.GetOutputStatus(0), Is.EqualTo("Faulted"));
                Assert.That(route.GetOutputStatus(1), Is.EqualTo("Ready"));
                CollectionAssert.AreEqual(new[] { 3.5f }, healthy.Values);
            }
            finally { route.Unbind(); }
        }

        [Test]
        public void StatefulValueStartsWithInitialStateAndKeepsStateAcrossRebind()
        {
            var trigger = new TestTriggerSource();
            var toggle = new ToggleBoolSource();
            SetField(toggle, "trigger", trigger);
            var context = Context();
            var values = new List<bool>();
            using (toggle.Build(context).Subscribe(values.Add)) trigger.Fire();
            using (toggle.Build(context).Subscribe(values.Add)) { }
            CollectionAssert.AreEqual(new[] { false, true, true }, values);
        }

        [Test]
        public void FrameEndSchedulerCoalescesKeysAndDefersReentrantActions()
        {
            var scheduler = new FrameEndScheduler();
            var calls = new List<string>();
            scheduler.Schedule("first", () =>
            {
                calls.Add("first");
                scheduler.Schedule("next", () => calls.Add("next"));
            });
            scheduler.Schedule("first", () => calls.Add("duplicate"));

            scheduler.Flush(runtime.Services);
            CollectionAssert.AreEqual(new[] { "first" }, calls);
            scheduler.Flush(runtime.Services);
            CollectionAssert.AreEqual(new[] { "first", "next" }, calls);
        }

        [Test]
        public void FrameClockIsolatesSubscriberExceptions()
        {
            var frameClock = new FrameClock();
            var healthyCalls = 0;
            frameClock.Updated += (_, __) => throw new InvalidOperationException("expected test exception");
            frameClock.Updated += (_, __) => healthyCalls++;

            frameClock.NotifyUpdated(runtime.Services);

            Assert.That(healthyCalls, Is.EqualTo(1));
            Assert.That(runtime.Services.GetDiagnostics().Count(), Is.EqualTo(1));
        }

        [TestCase(-1, 3, 2)]
        [TestCase(4, 3, 1)]
        public void PositiveModuloNormalizesNegativeInputs(int value, int modulus, int expected)
        {
            Assert.That(SourceStream.PositiveModulo(value, modulus), Is.EqualTo(expected));
        }

        [Test]
        public void IntModifierHandlesMinimumValueWithoutOverflow()
        {
            var modifier = new AbsIntModifier();
            var values = Collect(modifier.Apply(Observable.Return(int.MinValue), Context()));
            Assert.That(values, Is.EqualTo(new[] { int.MaxValue }));
        }

        [Test]
        public void ZeroVectorNormalizationReturnsZero()
        {
            var values = Collect(new NormalizeVector3Modifier().Apply(Observable.Return(Vector3.zero), Context()));
            Assert.That(values, Is.EqualTo(new[] { Vector3.zero }));
        }

        [Test]
        public void ZeroDivisionModifierRetainsFiniteDefaultAndReportsInvalidInput()
        {
            var context = Context();
            var source = new FloatBinarySource();
            SetField(source, "left", new ConstantFloatSource { Value = 2f });
            SetField(source, "right", new ConstantFloatSource { Value = 0f });
            SetField(source, "operation", FloatBinaryOperation.Divide);
            var values = Collect(source.Build(context));
            Assert.That(values, Is.EqualTo(new[] { 0f }));
            Assert.That(runtime.Services.GetDiagnostics().Count(), Is.EqualTo(1));
        }

        private static void SetField(object target, string fieldName, object value)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field == null) continue;
                field.SetValue(target, value);
                return;
            }
            throw new MissingFieldException(target.GetType().FullName, fieldName);
        }

        private RouteContext Context() => new RouteContext(runtime, host, state, new BindingScope(), Guid.NewGuid().ToString("N"));

        private static List<T> Collect<T>(Observable<T> source)
        {
            var values = new List<T>();
            using (source.Subscribe(values.Add)) { }
            return values;
        }

        private sealed class CountingSource : IValueSource<float>
        {
            private readonly float value;
            public int BuildCount { get; private set; }
            public CountingSource(float value) => this.value = value;
            public Observable<float> Build(RouteContext context) { BuildCount++; return Observable.Return(value); }
        }

        private sealed class RecordingFloatOutput : IValueOutput<float>
        {
            public readonly List<float> Values = new List<float>();
            public bool InitializedBeforeWrite { get; private set; }
            public void Initialize(RouteContext context) => InitializedBeforeWrite = true;
            public void Write(float value)
            {
                Assert.That(InitializedBeforeWrite, Is.True);
                Values.Add(value);
            }
        }

        private sealed class ThrowingFloatOutput : IValueOutput<float>
        {
            public void Initialize(RouteContext context) { }
            public void Write(float value) => throw new InvalidOperationException("expected test exception");
        }

        private sealed class TestTriggerSource : ITriggerSource
        {
            private readonly Subject<Unit> subject = new Subject<Unit>();
            public Observable<Unit> Build(RouteContext context) => subject;
            public void Fire() => subject.OnNext(Unit.Default);
        }

        private sealed class TestFloatSource : IValueSource<float>
        {
            private readonly Subject<float> subject = new Subject<float>();
            public Observable<float> Build(RouteContext context) => subject;
            public void Emit(float value) => subject.OnNext(value);
        }

        private sealed class TestBoolSource : IValueSource<bool>
        {
            private readonly Subject<bool> subject = new Subject<bool>();
            public Observable<bool> Build(RouteContext context) => subject;
            public void Emit(bool value) => subject.OnNext(value);
        }

        private sealed class ManualFrameClock : IFrameClock
        {
            public long FrameIndex { get; private set; }
            public double Time { get; private set; }
            public double UnscaledTime { get; private set; }
            public double DspTime { get; private set; }
            public double DeltaTime { get; private set; }
            public double UnscaledDeltaTime { get; private set; }
            public event Action<double, double> Updated;

            public void SetUnscaledTime(double value) => UnscaledTime = value;
            public void Advance(double deltaTime)
            {
                FrameIndex++;
                DeltaTime = UnscaledDeltaTime = Math.Max(0d, deltaTime);
                Time += DeltaTime;
                UnscaledTime += UnscaledDeltaTime;
                DspTime += DeltaTime;
                Updated?.Invoke(UnscaledDeltaTime, UnscaledTime);
            }
        }

        private sealed class RecordingTriggerOutput : ITriggerOutput
        {
            public int InvocationCount { get; private set; }
            public void Initialize(RouteContext context) { }
            public void Invoke() => InvocationCount++;
        }
    }
}
