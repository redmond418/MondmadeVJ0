using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using R3;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

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

        [Test]
        public void SelectedRouteMonitorCapturesRawModifierFinalAndOutputValues()
        {
            var services = runtime.Services;
            var source = new TestFloatSource();
            var route = new FloatRoute();
            route.Configure("Monitor pipeline", source);
            var modifier = new MultiplyFloatModifier();
            SetField(modifier, "factor", 2f);
            route.Modifiers.Add(modifier);
            var output = new RecordingFloatOutput();
            route.Outputs.Add(output);
            Assert.That(services.SetRouteTraceSelected(route.Id, true), Is.True);

            route.Bind(runtime, host, state);
            source.Emit(0.25f);
            var history = new List<RuntimeRouteTrace>();
            services.CopyRouteHistory(history, route.Id);

            CollectionAssert.AreEqual(new[]
            {
                RuntimeTraceStage.SourceRaw, RuntimeTraceStage.Modifier, RuntimeTraceStage.Final, RuntimeTraceStage.Output,
            }, history.ConvertAll(item => item.Stage));
            CollectionAssert.AreEqual(new[] { 0.25f, 0.5f, 0.5f, 0.5f }, history.ConvertAll(item => (float)item.Value));
            CollectionAssert.AreEqual(new[] { 0.5f }, output.Values);
            Assert.That(services.RouteProcessingCount, Is.EqualTo(2), "The modifier and output/write stages are timed separately.");
            route.Unbind();
        }

        [Test]
        public void A23DisabledMonitorAddsNoPerEmissionAllocation()
        {
            const int iterations = 200000;
            var services = runtime.Services;
            var selection = services.GetRouteTraceSelection("a23-disabled-cost");
            services.SetRouteTraceSelected(selection.RouteId, true);
            var direct = new Subject<int>();
            var traced = new Subject<int>();
            var sink = 0;
            using (direct.Subscribe(value => sink = value))
            using (services.TraceRoute(traced, selection, "disabled-cost", RuntimeTraceStage.SourceRaw, -1, null, "TestSource").Subscribe(value => sink = value))
            {
                services.MonitorCaptureEnabled = false;
                for (var i = 0; i < 2000; i++) { direct.OnNext(i); traced.OnNext(i); }
                var directAllocatedStart = GC.GetAllocatedBytesForCurrentThread();
                var directStart = Stopwatch.GetTimestamp();
                for (var i = 0; i < iterations; i++) direct.OnNext(i);
                var directTicks = Stopwatch.GetTimestamp() - directStart;
                var directAllocated = GC.GetAllocatedBytesForCurrentThread() - directAllocatedStart;
                var tracedAllocatedStart = GC.GetAllocatedBytesForCurrentThread();
                var tracedStart = Stopwatch.GetTimestamp();
                for (var i = 0; i < iterations; i++) traced.OnNext(i);
                var tracedTicks = Stopwatch.GetTimestamp() - tracedStart;
                var tracedAllocated = GC.GetAllocatedBytesForCurrentThread() - tracedAllocatedStart;
                var directNs = directTicks * 1e9d / Stopwatch.Frequency / iterations;
                var tracedNs = tracedTicks * 1e9d / Stopwatch.Frequency / iterations;
                UnityEngine.Debug.Log($"A23_DISABLED_CAPTURE,direct_ns_per_event={directNs:F2},instrumented_ns_per_event={tracedNs:F2},overhead_ns_per_event={tracedNs - directNs:F2},direct_allocated={directAllocated},instrumented_allocated={tracedAllocated},iterations={iterations},platform={Application.platform},cpu={SystemInfo.processorType}");
                Assert.That(tracedAllocated, Is.LessThanOrEqualTo(directAllocated + 128), "Disabled capture should not allocate per event.");
                Assert.That(sink, Is.EqualTo(iterations - 1));
                Assert.That(services.RouteTraceCount, Is.EqualTo(0));
            }
            direct.Dispose();
            traced.Dispose();
            services.MonitorCaptureEnabled = true;
        }

        [Test]
        public void MonitorHistoriesStayBoundedAndCaptureCanBeDisabled()
        {
            var services = runtime.Services;
            const int samples = RuntimeServices.MonitorHistoryCapacity + 3;
            for (var i = 0; i < samples; i++)
                services.RecordRouteValue("route", "bounded", RuntimeTraceStage.Final, -1, null, null, i, null);

            var history = new List<RuntimeRouteTrace>();
            services.CopyRouteHistory(history, "route");
            Assert.That(history.Count, Is.EqualTo(RuntimeServices.MonitorHistoryCapacity));
            Assert.That((int)history[0].Value, Is.EqualTo(3));
            Assert.That((int)history[history.Count - 1].Value, Is.EqualTo(samples - 1));
            Assert.That(services.DroppedRouteHistoryCount, Is.EqualTo(3));

            services.MonitorCaptureEnabled = false;
            services.RecordRouteValue("route", "bounded", RuntimeTraceStage.Final, -1, null, null, samples, null);
            services.CopyRouteHistory(history, "route");
            Assert.That(history.Count, Is.EqualTo(RuntimeServices.MonitorHistoryCapacity));
            Assert.That((int)history[history.Count - 1].Value, Is.EqualTo(samples - 1));
        }

        [Test]
        public void RouteTraceSelectionIsLimitedToThirtyTwoRoutes()
        {
            var services = runtime.Services;
            for (var i = 0; i < RuntimeServices.MonitorRouteSelectionLimit; i++)
                Assert.That(services.SetRouteTraceSelected("route-" + i, true), Is.True);
            Assert.That(services.SelectedRouteCount, Is.EqualTo(RuntimeServices.MonitorRouteSelectionLimit));
            Assert.That(services.SetRouteTraceSelected("overflow", true), Is.False);
            Assert.That(services.SetRouteTraceSelected("route-0", false), Is.True);
            Assert.That(services.SetRouteTraceSelected("overflow", true), Is.True);
        }

        [Test]
        public void MonitorStoresLogicalAndRawMidiAsWellAsTapAndClockHistory()
        {
            var services = runtime.Services;
            services.RecordMidiRaw("surface", "APC mini mk2", 1, 2, 74, 0.75f);
            services.RecordMidiLogical("surface", "APC mini mk2", "fader", "Fader 1", 2, 0.75f, 0f, 0f, false, false, false);
            services.RecordMidiLedQueued("surface", "APC mini mk2", "pad_0_0", 0, 56, 4f, 0, 255, 0, 1f, 0);
            services.RecordTransportEvent(RuntimeTransportEventKind.Tap, 1d, 120d, 2d, "Tap accepted");
            services.RecordTransportEvent(RuntimeTransportEventKind.MidiClockPulse, 1.01d, 120d, 2.04d);

            var midi = new List<RuntimeMidiTrace>();
            var transport = new List<RuntimeTransportTrace>();
            services.CopyMidiHistory(midi);
            services.CopyTransportHistory(transport);
            CollectionAssert.AreEqual(new[]
            {
                RuntimeMidiEventKind.RawInput, RuntimeMidiEventKind.LogicalInput, RuntimeMidiEventKind.LedOutputQueued,
            }, midi.ConvertAll(item => item.Kind));
            Assert.That(midi[0].AddressType, Is.EqualTo(1));
            Assert.That(midi[0].SurfaceName, Is.EqualTo("APC mini mk2"));
            Assert.That(midi[1].Label, Is.EqualTo("Fader 1"));
            Assert.That(midi[2].Green, Is.EqualTo(255));
            CollectionAssert.AreEqual(new[]
            {
                RuntimeTransportEventKind.Tap, RuntimeTransportEventKind.MidiClockPulse,
            }, transport.ConvertAll(item => item.Kind));
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
        public void DelayUsesElapsedFrameTimeAndDoesNotEmitAfterDisposal()
        {
            var context = Context();
            var source = new TestTriggerSource();
            var modifier = new DelayTriggerModifier();
            SetField(modifier, "delay", 0.1f);
            var emitted = 0;
            var subscription = modifier.Apply(source.Build(context), context).Subscribe(_ => emitted++);
            try
            {
                source.Fire();
                Assert.That(emitted, Is.Zero);
                testFrameClock.Advance(0.25d);
                Assert.That(emitted, Is.EqualTo(1), "A delayed trigger should release on the first frame after its deadline.");

                source.Fire();
                subscription.Dispose();
                testFrameClock.Advance(1d);
                Assert.That(emitted, Is.EqualTo(1), "Disposing a delayed stream must clear pending work.");
            }
            finally
            {
                subscription.Dispose();
                context.Scope.Dispose();
            }
        }

        [Test]
        public void SmoothDampUsesUnscaledFrameTimeDuringPauseAndStopsAfterDisposal()
        {
            var context = Context();
            var source = new TestFloatSource();
            var modifier = new SmoothDampFloatModifier();
            SetField(modifier, "smoothTime", 0.1f);
            var values = new List<float>();
            var subscription = modifier.Apply(source.Build(context), context).Subscribe(values.Add);
            try
            {
                source.Emit(0f);
                source.Emit(1f);
                Assert.That(values, Is.EqualTo(new[] { 0f }));

                testFrameClock.Advance(0d, 0.1d);

                Assert.That(values.Count, Is.GreaterThan(1), "Unscaled smoothing must continue when scaled game time is paused.");
                Assert.That(values[values.Count - 1], Is.GreaterThan(0f).And.LessThan(1f));
                var countAfterDispose = values.Count;
                subscription.Dispose();
                testFrameClock.Advance(0d, 1d);
                Assert.That(values, Has.Count.EqualTo(countAfterDispose), "A disposed temporal modifier must unsubscribe from FrameClock.");
            }
            finally
            {
                subscription.Dispose();
                context.Scope.Dispose();
            }
        }

        [Test]
        public void SecondsEnvelopeAdvancesAcrossLargeUnscaledFrameAndStopsAfterDisposal()
        {
            var context = Context();
            var trigger = new TestTriggerSource();
            var envelope = new TriggerEnvelopeFloatSource();
            SetField(envelope, "trigger", trigger);
            SetField(envelope, "start", 0f);
            SetField(envelope, "peak", 1f);
            SetField(envelope, "end", 0f);
            SetField(envelope, "attack", 0.1f);
            SetField(envelope, "hold", 0.05f);
            SetField(envelope, "release", 0.2f);
            SetField(envelope, "timeUnit", EnvelopeTimeUnit.Seconds);
            var values = new List<float>();
            var subscription = envelope.Build(context).Subscribe(values.Add);
            try
            {
                trigger.Fire();
                testFrameClock.Advance(0d, 0.08d);
                Assert.That(values[values.Count - 1], Is.EqualTo(0.8f).Within(0.02f), "The attack should follow elapsed unscaled seconds while scaled time is paused.");

                testFrameClock.Advance(0d, 0.28d);
                Assert.That(values[values.Count - 1], Is.EqualTo(0f).Within(0.0001f), "A low-FPS frame must advance through attack, hold and release to the final value.");
                var countAfterDispose = values.Count;
                subscription.Dispose();
                testFrameClock.Advance(0d, 1d);
                Assert.That(values, Has.Count.EqualTo(countAfterDispose), "A disposed envelope must stop producing frame updates.");
            }
            finally
            {
                subscription.Dispose();
                context.Scope.Dispose();
            }
        }

        [Test]
        public void DebounceMovesItsDeadlineWhenAnotherTriggerArrives()
        {
            var context = Context();
            var source = new TestTriggerSource();
            var modifier = new DebounceTriggerModifier();
            SetField(modifier, "seconds", 0.05f);
            var emitted = 0;
            using (modifier.Apply(source.Build(context), context).Subscribe(_ => emitted++))
            {
                source.Fire();
                testFrameClock.Advance(0.03d);
                source.Fire();
                testFrameClock.Advance(0.03d);
                Assert.That(emitted, Is.Zero, "The first deadline must be replaced by the most recent trigger.");
                testFrameClock.Advance(0.03d);
                Assert.That(emitted, Is.EqualTo(1));
            }
            context.Scope.Dispose();
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
        public void MapValueSourceAppliesInputModifiersBeforeConverting()
        {
            var source = new FloatToVector3Source { Input = new ConstantFloatSource { Value = 0.5f } };
            var curve = new CurveFloatModifier();
            SetField(curve, "curve", new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.5f, 0.8f),
                new Keyframe(1f, 1f)));
            source.InputModifiers.Add(curve);
            var context = Context();

            try
            {
                var value = Collect(source.Build(context)).Single();
                Assert.That(value.x, Is.EqualTo(0.8f).Within(0.0001f));
                Assert.That(value.y, Is.EqualTo(0.8f).Within(0.0001f));
                Assert.That(value.z, Is.EqualTo(0.8f).Within(0.0001f));
            }
            finally { context.Scope.Dispose(); }
        }

        [Test]
        public void StatefulStepAndEnvelopeEvaluateOneTransitionForThreeOutputs()
        {
            var stepTrigger = new TestTriggerSource();
            var step = new StepIntSource();
            SetField(step, "trigger", stepTrigger);
            var stepOutputs = new[] { new RecordingIntOutput(), new RecordingIntOutput(), new RecordingIntOutput() };
            var stepRoute = new IntRoute();
            stepRoute.Configure("single Step transition", step);
            foreach (var output in stepOutputs) stepRoute.Outputs.Add(output);
            stepRoute.Bind(runtime, host, state);
            try
            {
                Assert.That(stepTrigger.BuildCount, Is.EqualTo(1));
                stepTrigger.Fire();
                stepTrigger.Fire();
                foreach (var output in stepOutputs) CollectionAssert.AreEqual(new[] { 0, 1, 2 }, output.Values);
            }
            finally { stepRoute.Unbind(); }

            var envelopeTrigger = new TestTriggerSource();
            var envelope = new TriggerEnvelopeFloatSource();
            SetField(envelope, "trigger", envelopeTrigger);
            SetField(envelope, "attack", 0.1f);
            SetField(envelope, "hold", 0f);
            SetField(envelope, "release", 0.2f);
            var envelopeOutputs = new[] { new RecordingFloatOutput(), new RecordingFloatOutput(), new RecordingFloatOutput() };
            var envelopeRoute = new FloatRoute();
            envelopeRoute.Configure("single Envelope transition", envelope);
            foreach (var output in envelopeOutputs) envelopeRoute.Outputs.Add(output);
            envelopeRoute.Bind(runtime, host, state);
            try
            {
                Assert.That(envelopeTrigger.BuildCount, Is.EqualTo(1));
                envelopeTrigger.Fire();
                testFrameClock.Advance(0d, 0.125d);
                Assert.That(envelopeRoute.CurrentValue, Is.GreaterThan(0f));
                Assert.That(envelopeOutputs[0].Values, Is.Not.Empty);
                CollectionAssert.AreEqual(envelopeOutputs[0].Values, envelopeOutputs[1].Values);
                CollectionAssert.AreEqual(envelopeOutputs[0].Values, envelopeOutputs[2].Values);
            }
            finally { envelopeRoute.Unbind(); }
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
        public void RouteBindFailureIsNamedAndDoesNotStopHealthyRoutes()
        {
            var failing = new FloatRoute();
            failing.Configure("broken source route", new ThrowingBuildSource());
            failing.Outputs.Add(new RecordingFloatOutput());
            failing.Bind(runtime, host, state);

            var healthyOutput = new RecordingFloatOutput();
            var healthy = new FloatRoute();
            healthy.Configure("healthy route", new CountingSource(2.5f));
            healthy.Outputs.Add(healthyOutput);
            healthy.Bind(runtime, host, state);
            try
            {
                Assert.That(failing.Status, Is.EqualTo("Faulted"));
                Assert.That(healthy.Status, Is.EqualTo("Active"));
                CollectionAssert.AreEqual(new[] { 2.5f }, healthyOutput.Values);
                var diagnostic = runtime.Services.GetDiagnostics().Single(item => item.Code == "ROUTE_BIND" && item.RouteId == failing.Id);
                Assert.That(diagnostic.Message, Does.Contain("broken source route"));
                Assert.That(diagnostic.Exception, Does.Contain("synthetic Source failure"));
            }
            finally
            {
                failing.Unbind();
                healthy.Unbind();
            }
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

        [Test]
        public void NonFiniteModifierInputRetainsLastFiniteValueAndReportsDiagnostic()
        {
            var context = Context();
            var source = new TestFloatSource();
            var modifier = new AddFloatModifier();
            var values = new List<float>();
            var subscription = modifier.Apply(source.Build(context), context).Subscribe(values.Add);
            try
            {
                source.Emit(0.25f);
                source.Emit(float.NaN);
                source.Emit(0.75f);

                CollectionAssert.AreEqual(new[] { 0.25f, 0.75f }, values);
                Assert.That(runtime.Services.GetDiagnostics().Any(item => item.Code == "INVALID_VALUE"), Is.True);
            }
            finally
            {
                subscription.Dispose();
                context.Scope.Dispose();
            }
        }

        [Test]
        public void NegativeModuloAndOutOfRangeLookupUseDocumentedBoundaries()
        {
            var modulo = new ModuloIntModifier();
            SetField(modulo, "modulus", 5);
            CollectionAssert.AreEqual(new[] { 4 }, Collect(modulo.Apply(Observable.Return(-1), Context())));

            var lookup = new LookupIntModifier();
            SetField(lookup, "values", new[] { 2, 4, 8 });
            CollectionAssert.AreEqual(new[] { 2 }, Collect(lookup.Apply(Observable.Return(-10), Context())));
            CollectionAssert.AreEqual(new[] { 8 }, Collect(lookup.Apply(Observable.Return(10), Context())));
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

        private sealed class ThrowingBuildSource : IValueSource<float>
        {
            public Observable<float> Build(RouteContext context) => throw new InvalidOperationException("synthetic Source failure");
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

        private sealed class RecordingIntOutput : IValueOutput<int>
        {
            public readonly List<int> Values = new List<int>();
            public void Initialize(RouteContext context) { }
            public void Write(int value) => Values.Add(value);
        }

        private sealed class ThrowingFloatOutput : IValueOutput<float>
        {
            public void Initialize(RouteContext context) { }
            public void Write(float value) => throw new InvalidOperationException("expected test exception");
        }

        private sealed class TestTriggerSource : ITriggerSource
        {
            private readonly Subject<Unit> subject = new Subject<Unit>();
            public int BuildCount { get; private set; }
            public Observable<Unit> Build(RouteContext context) { BuildCount++; return subject; }
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
                => Advance(deltaTime, deltaTime);

            public void Advance(double deltaTime, double unscaledDeltaTime)
            {
                FrameIndex++;
                DeltaTime = Math.Max(0d, deltaTime);
                UnscaledDeltaTime = Math.Max(0d, unscaledDeltaTime);
                Time += DeltaTime;
                UnscaledTime += UnscaledDeltaTime;
                DspTime += UnscaledDeltaTime;
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
