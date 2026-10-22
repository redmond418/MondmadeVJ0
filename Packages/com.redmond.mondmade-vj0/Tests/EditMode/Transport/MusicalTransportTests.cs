using System.Collections.Generic;
using System.Reflection;
using System.Reflection;
using NUnit.Framework;
using R3;
using UnityEngine;

namespace MondmadeVJ0.Tests
{
    public sealed class MusicalTransportTests
    {
        private GameObject transportObject;
        private MusicalTransport transport;
        private RuntimeServices services;

        [SetUp]
        public void SetUp()
        {
            transportObject = new GameObject("MusicalTransport test");
            transport = transportObject.AddComponent<MusicalTransport>();
            services = new RuntimeServices();
            transport.Register(services);
            transport.Initialize(services);
        }

        [TearDown]
        public void TearDown()
        {
            transport?.Shutdown(services);
            if (transportObject != null) Object.DestroyImmediate(transportObject);
        }

        [Test]
        public void TempoChangeKeepsBeatPositionContinuousAndStopFreezesIt()
        {
            transport.Tick(services, 0.5d);
            Assert.That(transport.BeatPosition, Is.EqualTo(1d).Within(1e-9d));

            transport.SetBaseBpm(60d);
            transport.Tick(services, 0.5d);
            Assert.That(transport.BeatPosition, Is.EqualTo(1.5d).Within(1e-9d));

            transport.SetRunning(false);
            transport.Tick(services, 5d);
            Assert.That(transport.BeatPosition, Is.EqualTo(1.5d).Within(1e-9d));
        }

        [Test]
        public void LongVirtualRunKeepsPositionAndDerivedPhasesContinuousAcrossTempoChanges()
        {
            transport.SetTimeSignature(3, 4);
            const int FramesPerSegment = 12000;
            const double FrameSeconds = 1d / 60d;

            for (var frame = 0; frame < FramesPerSegment; frame++) transport.Tick(services, FrameSeconds);
            Assert.That(transport.BeatPosition, Is.EqualTo(400d).Within(1e-8d));
            var beforeTempoChange = transport.BeatPosition;
            transport.SetBaseBpm(90d);
            Assert.That(transport.BeatPosition, Is.EqualTo(beforeTempoChange).Within(1e-12d));
            transport.SetMultiplier(1.5d);
            Assert.That(transport.BeatPosition, Is.EqualTo(beforeTempoChange).Within(1e-12d));

            for (var frame = 0; frame < FramesPerSegment; frame++) transport.Tick(services, FrameSeconds);
            Assert.That(transport.BeatPosition, Is.EqualTo(850d).Within(1e-8d));
            beforeTempoChange = transport.BeatPosition;
            transport.SetBaseBpm(80d);
            transport.SetMultiplier(0.5d);
            Assert.That(transport.BeatPosition, Is.EqualTo(beforeTempoChange).Within(1e-12d));

            for (var frame = 0; frame < FramesPerSegment; frame++) transport.Tick(services, FrameSeconds);

            var expected = 400d + 450d + 400d / 3d;
            Assert.That(transport.BeatPosition, Is.EqualTo(expected).Within(1e-8d));
            Assert.That(transport.BeatPhase, Is.EqualTo(expected - System.Math.Floor(expected)).Within(1e-8d));
            Assert.That(transport.BarPhase, Is.EqualTo(7d / 9d).Within(1e-8d));
            var tripletPosition = transport.BeatPosition / (1d / 6d);
            var expectedTripletPhase = tripletPosition - System.Math.Floor(tripletPosition);
            var tripletPhase = TriggerModifierClock.GridPhase(QuantizeGrid.QuarterTriplet, transport);
            Assert.That(tripletPhase, Is.EqualTo(expectedTripletPhase).Within(1e-12d));
            Assert.That(System.Math.Min(tripletPhase, 1d - tripletPhase), Is.LessThan(1e-8d), "Accumulated floating point error may place a boundary just below zero phase.");
        }

        [Test]
        public void MeterChangeAppliesAtNextBarAndEmitsBeatAndBarBoundaries()
        {
            var beats = new List<long>();
            var bars = new List<long>();
            transport.Beat += beats.Add;
            transport.Bar += bars.Add;
            transport.SetTimeSignature(3, 4);
            Assert.That(transport.BarLengthBeats, Is.EqualTo(3d));

            transport.Tick(services, 1.55d);

            CollectionAssert.AreEqual(new long[] { 1, 2, 3 }, beats);
            CollectionAssert.AreEqual(new long[] { 1 }, bars);
            Assert.That(transport.BarIndex, Is.EqualTo(1));
        }

        [Test]
        public void TapTempoRejectsOneOutlierAndAcceptsTwoMatchingOutliers()
        {
            transport.Tap(0d);
            transport.Tap(0.5d);
            transport.Tap(1d);
            transport.Tap(1.5d);
            Assert.That(transport.BaseBpm, Is.EqualTo(120d).Within(1e-6d));

            transport.Tap(2.2d);
            Assert.That(transport.BaseBpm, Is.EqualTo(120d).Within(1e-6d));
            transport.Tap(2.9d);
            Assert.That(transport.BaseBpm, Is.EqualTo(60d / 0.7d).Within(1e-4d));
        }

        [Test]
        public void TapTimeoutStartsANewTempoHistory()
        {
            transport.Tap(0d);
            transport.Tap(0.5d);
            transport.Tap(1d);
            transport.Tap(1.5d);
            transport.Tap(4d);
            transport.Tap(4.75d);
            Assert.That(transport.BaseBpm, Is.EqualTo(80d).Within(1e-6d), "The timeout must discard the old 120 BPM intervals.");
            transport.Tap(5.5d);
            Assert.That(transport.BaseBpm, Is.EqualTo(80d).Within(1e-6d));
        }

        [TestCase(2d, 30d)]
        [TestCase(0.2d, 300d)]
        public void TapTempoAcceptsBpmLimits(double interval, double expectedBpm)
        {
            transport.Tap(0d);
            transport.Tap(interval);
            Assert.That(transport.BaseBpm, Is.EqualTo(expectedBpm).Within(1e-6d));
        }

        [TestCase(2.01d)]
        [TestCase(0.199d)]
        public void TapTempoRejectsIntervalsOutsideBpmLimits(double interval)
        {
            transport.Tap(0d);
            transport.Tap(interval);
            Assert.That(transport.BaseBpm, Is.EqualTo(120d).Within(1e-6d));
        }

        [Test]
        public void MidiStartStopSongPositionAndContinuePreserveProtocolState()
        {
            transport.SetClockSource(TransportClockSource.External);
            var now = Time.realtimeSinceStartupAsDouble;
            transport.OnStart(now);
            transport.OnClockPulse(now + 0.5d, 0d);
            Assert.That(transport.IsRunning, Is.True);
            Assert.That(transport.BeatPosition, Is.EqualTo(1d / 24d).Within(1e-8d));

            transport.OnStop(now + 0.51d);
            Assert.That(transport.IsRunning, Is.False);
            transport.OnSongPositionPointer(40, now + 0.6d);
            Assert.That(transport.BeatPosition, Is.EqualTo(10d).Within(1e-9d));
            transport.OnContinue(now + 0.7d);
            transport.OnClockPulse(now + 0.75d, 0d);
            Assert.That(transport.IsRunning, Is.True);
            Assert.That(transport.BeatPosition, Is.EqualTo(10d + 1d / 24d).Within(1e-8d));
        }

        [Test]
        public void TransportRegistersExternalOscClockContract()
        {
            Assert.That(services.Get<IExternalTransportClock>(), Is.SameAs(transport));
        }

        [Test]
        public void TimelineRevisionChangesOnDiscontinuities()
        {
            var revision = transport.TimelineRevision;
            transport.Seek(2d);
            Assert.That(transport.TimelineRevision, Is.EqualTo(++revision));

            transport.ResetPhase();
            Assert.That(transport.TimelineRevision, Is.EqualTo(++revision));

            transport.SetRunning(false);
            Assert.That(transport.TimelineRevision, Is.EqualTo(++revision));

            transport.SetClockSource(TransportClockSource.External);
            Assert.That(transport.TimelineRevision, Is.EqualTo(++revision));
        }

        [Test]
        public void BarGridPhaseAndBoundariesFollowMeterAndSeekOrigin()
        {
            transport.SetTimeSignature(3, 4);
            transport.Seek(4d);

            Assert.That(transport.BarLengthBeats, Is.EqualTo(3d));
            Assert.That(transport.BarPhase, Is.EqualTo(1d / 3d).Within(1e-9d));
            Assert.That(TriggerModifierClock.GridPhase(QuantizeGrid.Bar, transport), Is.EqualTo(1d / 3d).Within(1e-9d));
            Assert.That(TriggerModifierClock.NextQuantizeBoundary(QuantizeGrid.Bar, transport, 4d, 1e-7d), Is.EqualTo(6d).Within(1e-9d));
            Assert.That(TriggerModifierClock.NextSubdivisionBoundary(QuantizeGrid.Bar, transport, 4d, 1e-7d), Is.EqualTo(6d).Within(1e-9d));
        }

        [TestCase(QuantizeGrid.Bar, 4d)]
        [TestCase(QuantizeGrid.Beat, 1d)]
        [TestCase(QuantizeGrid.HalfBeat, 0.5d)]
        [TestCase(QuantizeGrid.QuarterBeat, 0.25d)]
        [TestCase(QuantizeGrid.EighthBeat, 0.125d)]
        [TestCase(QuantizeGrid.SixteenthBeat, 0.0625d)]
        [TestCase(QuantizeGrid.HalfTriplet, 1d / 3d)]
        [TestCase(QuantizeGrid.QuarterTriplet, 1d / 6d)]
        [TestCase(QuantizeGrid.EighthTriplet, 1d / 12d)]
        [TestCase(QuantizeGrid.SixteenthTriplet, 1d / 24d)]
        public void EveryGridUsesCurrentBoundaryAndNextBoundary(QuantizeGrid grid, double step)
        {
            Assert.That(TriggerModifierClock.GridBeats(grid, transport), Is.EqualTo(step).Within(1e-12d));

            transport.Seek(step);
            Assert.That(TriggerModifierClock.GridPhase(grid, transport), Is.EqualTo(0d).Within(1e-9d));
            Assert.That(TriggerModifierClock.NextQuantizeBoundary(grid, transport, step, 1e-7d), Is.EqualTo(step).Within(1e-9d));
            Assert.That(TriggerModifierClock.NextSubdivisionBoundary(grid, transport, step, 1e-7d), Is.EqualTo(step * 2d).Within(1e-9d));

            transport.Seek(step * 1.25d);
            Assert.That(TriggerModifierClock.GridPhase(grid, transport), Is.EqualTo(0.25d).Within(1e-9d));
            Assert.That(TriggerModifierClock.NextQuantizeBoundary(grid, transport, transport.BeatPosition, 1e-7d), Is.EqualTo(step * 2d).Within(1e-9d));
            Assert.That(TriggerModifierClock.NextSubdivisionBoundary(grid, transport, transport.BeatPosition, 1e-7d), Is.EqualTo(step * 2d).Within(1e-9d));
        }

        [Test]
        public void QuantizeDropsPendingTriggersAfterForwardSeekAndContinuesOnNewTimeline()
        {
            var frameClock = new ManualFrameClock();
            services.Register<IFrameClock>(frameClock);
            var runtimeObject = new GameObject("Quantize test runtime");
            runtimeObject.SetActive(false);
            var runtime = runtimeObject.AddComponent<MondmadeVJ0Runtime>();
            runtime.Services = services;
            var scope = new BindingScope();
            var source = new Subject<Unit>();
            var modifier = new QuantizeTriggerModifier();
            typeof(QuantizeTriggerModifier).GetField("grid", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(modifier, QuantizeGrid.Beat);
            var context = new RouteContext(runtime, null, new RuntimeStateStore(), scope, "quantize-test");
            var emitted = 0;
            var output = modifier.Apply(source, context).Subscribe(_ => emitted++);
            scope.Add(output);

            transport.Seek(0.25d);
            source.OnNext(Unit.Default);
            transport.Seek(2d);
            frameClock.Step();
            Assert.That(emitted, Is.Zero, "A forward seek must discard a trigger scheduled on the old timeline.");

            transport.Seek(0.25d);
            source.OnNext(Unit.Default);
            transport.Tick(services, 0.375d);
            frameClock.Step();
            Assert.That(emitted, Is.EqualTo(1), "A fresh trigger must still emit at its next Beat boundary.");

            scope.Dispose();
            source.Dispose();
            transport.Shutdown(services);
            Object.DestroyImmediate(runtimeObject);
        }

        [Test]
        public void BeatDelayWaitsForTransportAndDiscardsQueueOnStop()
        {
            var frameClock = new ManualFrameClock();
            services.Register<IFrameClock>(frameClock);
            var runtimeObject = new GameObject("Beat delay test runtime");
            runtimeObject.SetActive(false);
            var runtime = runtimeObject.AddComponent<MondmadeVJ0Runtime>();
            runtime.Services = services;
            var scope = new BindingScope();
            var source = new Subject<Unit>();
            var modifier = new DelayTriggerModifier();
            typeof(DelayTriggerModifier).GetField("timeUnit", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(modifier, TriggerTimeUnit.Beats);
            typeof(DelayTriggerModifier).GetField("delay", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(modifier, 0.5f);
            var context = new RouteContext(runtime, null, new RuntimeStateStore(), scope, "beat-delay-test");
            var emitted = 0;
            var output = modifier.Apply(source, context).Subscribe(_ => emitted++);
            scope.Add(output);

            transport.Seek(0.25d);
            source.OnNext(Unit.Default);
            transport.Tick(services, 0.125d);
            frameClock.Step();
            Assert.That(emitted, Is.Zero);
            transport.Tick(services, 0.125d);
            frameClock.Step();
            Assert.That(emitted, Is.EqualTo(1));

            source.OnNext(Unit.Default);
            transport.SetRunning(false);
            frameClock.Step();
            transport.SetRunning(true);
            transport.Tick(services, 2d);
            frameClock.Step();
            Assert.That(emitted, Is.EqualTo(1), "A stopped Transport must clear pending Beat delays before it resumes.");

            scope.Dispose();
            transport.Shutdown(services);
            Object.DestroyImmediate(runtimeObject);
        }

        [Test]
        public void BarSubdivisionResetsOnSeekAndUsesNewOrigin()
        {
            transport.SetTimeSignature(3, 4);
            transport.Seek(4d);
            var frameClock = new ManualFrameClock();
            services.Register<IFrameClock>(frameClock);
            var runtimeObject = new GameObject("Subdivision test runtime");
            runtimeObject.SetActive(false);
            var runtime = runtimeObject.AddComponent<MondmadeVJ0Runtime>();
            runtime.Services = services;
            var scope = new BindingScope();
            var source = new SubdivisionTriggerSource();
            typeof(SubdivisionTriggerSource).GetField("grid", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(source, QuantizeGrid.Bar);
            var context = new RouteContext(runtime, null, new RuntimeStateStore(), scope, "subdivision-test");
            var emitted = 0;
            var subscription = source.Build(context).Subscribe(_ => emitted++);
            scope.Add(subscription);

            transport.Seek(6d);
            frameClock.Step();
            Assert.That(emitted, Is.Zero, "Seeking onto a bar boundary must not replay that boundary.");

            transport.Tick(services, 1.5d);
            frameClock.Step();
            Assert.That(transport.BeatPosition, Is.EqualTo(9d).Within(1e-9d));
            Assert.That(emitted, Is.EqualTo(1), "The next bar boundary from the new origin must emit once.");

            scope.Dispose();
            transport.Shutdown(services);
            Object.DestroyImmediate(runtimeObject);
        }

        [Test]
        public void BarSubdivisionEmitsMeterChangeBoundaryAndUsesNewBarLength()
        {
            transport.Seek(3d);
            var frameClock = new ManualFrameClock();
            services.Register<IFrameClock>(frameClock);
            var runtimeObject = new GameObject("Meter change subdivision runtime");
            runtimeObject.SetActive(false);
            var runtime = runtimeObject.AddComponent<MondmadeVJ0Runtime>();
            runtime.Services = services;
            var scope = new BindingScope();
            var source = new SubdivisionTriggerSource();
            typeof(SubdivisionTriggerSource).GetField("grid", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(source, QuantizeGrid.Bar);
            var context = new RouteContext(runtime, null, new RuntimeStateStore(), scope, "meter-subdivision-test");
            var emitted = 0;
            var subscription = source.Build(context).Subscribe(_ => emitted++);
            scope.Add(subscription);

            transport.SetTimeSignature(3, 4);
            transport.Tick(services, 0.5d);
            frameClock.Step();
            Assert.That(transport.BeatPosition, Is.EqualTo(4d).Within(1e-9d));
            Assert.That(transport.BarLengthBeats, Is.EqualTo(3d));
            Assert.That(emitted, Is.EqualTo(1), "The old bar boundary where the new meter starts is also a Bar subdivision.");

            transport.Tick(services, 1.5d);
            frameClock.Step();
            Assert.That(transport.BeatPosition, Is.EqualTo(7d).Within(1e-9d));
            Assert.That(emitted, Is.EqualTo(2), "The next subdivision must follow the new 3/4 bar length.");

            scope.Dispose();
            transport.Shutdown(services);
            Object.DestroyImmediate(runtimeObject);
        }

        [Test]
        public void MidiClockReacquisitionContinuesFromFreeRunningPosition()
        {
            transport.SetClockSource(TransportClockSource.External);
            transport.Seek(4d);
            var now = Time.realtimeSinceStartupAsDouble;
            transport.SetExternalClock(120d, 4d, true, now);
            typeof(MusicalTransport).GetField("<SyncState>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(transport, TransportSyncState.Lost);
            var freeRunningPosition = transport.BeatPosition;
            var revisionBeforeReacquire = transport.TimelineRevision;

            transport.OnClockPulse(now + 1d / 48d, 0d);

            Assert.That(transport.SyncState, Is.EqualTo(TransportSyncState.Acquiring));
            Assert.That(transport.BeatPosition, Is.GreaterThan(freeRunningPosition));
            Assert.That(transport.BeatPosition, Is.EqualTo(freeRunningPosition + 1d / 24d).Within(1e-6d));
            Assert.That(transport.TimelineRevision, Is.EqualTo(revisionBeforeReacquire), "Clock recovery must preserve the free-running timeline.");
        }

        private sealed class ManualFrameClock : IFrameClock
        {
            public long FrameIndex { get; private set; }
            public double Time => 0d;
            public double UnscaledTime => 0d;
            public double DspTime => 0d;
            public double DeltaTime => 0d;
            public double UnscaledDeltaTime => 0d;
            public event System.Action<double, double> Updated;
            public void Step() { FrameIndex++; Updated?.Invoke(0d, 0d); }
        }
    }
}
