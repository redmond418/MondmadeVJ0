using System.Collections.Generic;
using NUnit.Framework;
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
    }
}
