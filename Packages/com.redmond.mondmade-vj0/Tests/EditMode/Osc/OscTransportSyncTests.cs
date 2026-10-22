using NUnit.Framework;
using UnityEngine;

namespace MondmadeVJ0.Tests
{
    public sealed class OscTransportSyncTests
    {
        private GameObject hubObject;
        private GameObject adapterObject;
        private OscHub hub;
        private OscTransportSyncAdapter adapter;
        private RuntimeServices services;
        private RecordingExternalClock clock;

        [SetUp]
        public void SetUp()
        {
            hubObject = new GameObject("OSC hub test");
            hub = hubObject.AddComponent<OscHub>();
            adapterObject = new GameObject("OSC transport adapter test");
            adapter = adapterObject.AddComponent<OscTransportSyncAdapter>();
            services = new RuntimeServices();
            clock = new RecordingExternalClock();
            hub.Register(services);
            services.Register<IExternalTransportClock>(clock);
            adapter.Initialize(services);
        }

        [TearDown]
        public void TearDown()
        {
            adapter?.Shutdown(services);
            if (adapterObject != null) Object.DestroyImmediate(adapterObject);
            if (hubObject != null) Object.DestroyImmediate(hubObject);
        }

        [Test]
        public void FourFloatPacketUpdatesExternalBeatAndRunningState()
        {
            hub.DispatchPacket(Packet('f', 'f', 'f', 'f', 128f, 512f, 0.25f, 1f));

            Assert.That(adapter.AcceptedPackets, Is.EqualTo(1));
            Assert.That(adapter.RejectedPackets, Is.Zero);
            Assert.That(clock.CallCount, Is.EqualTo(1));
            Assert.That(clock.Bpm, Is.EqualTo(128d));
            Assert.That(clock.Beat, Is.EqualTo(512.25d));
            Assert.That(clock.Running, Is.True);
        }

        [TestCase(301f, 0f, 0f, 1f)]
        [TestCase(120f, 16777216f, 0f, 1f)]
        [TestCase(120f, 4f, 1f, 1f)]
        [TestCase(120f, 4f, 0f, 0.5f)]
        public void OutOfRangePacketIsRejected(float bpm, float whole, float fractional, float running)
        {
            hub.DispatchPacket(Packet('f', 'f', 'f', 'f', bpm, whole, fractional, running));

            Assert.That(adapter.AcceptedPackets, Is.Zero);
            Assert.That(adapter.RejectedPackets, Is.EqualTo(1));
            Assert.That(clock.CallCount, Is.Zero);
        }

        [Test]
        public void WrongOscTypesAreRejected()
        {
            hub.DispatchPacket(new OscPacket(OscTransportSyncAdapter.DefaultAddress, 4, 'f', 'i', 'f', 'f', 120f, 2f, 0f, 1f, 0, 2, 0, 0));

            Assert.That(adapter.RejectedPackets, Is.EqualTo(1));
            Assert.That(clock.CallCount, Is.Zero);
        }

        [Test]
        public void OtherAddressesAreIgnored()
        {
            hub.DispatchPacket(new OscPacket("/unrelated", 4, 'f', 'f', 'f', 'f', 120f, 2f, 0f, 1f, 0, 0, 0, 0));

            Assert.That(adapter.AcceptedPackets, Is.Zero);
            Assert.That(adapter.RejectedPackets, Is.Zero);
            Assert.That(clock.CallCount, Is.Zero);
        }

        private static OscPacket Packet(char tag0, char tag1, char tag2, char tag3, float a, float b, float c, float d) =>
            new OscPacket(OscTransportSyncAdapter.DefaultAddress, 4, tag0, tag1, tag2, tag3, a, b, c, d, 0, 0, 0, 0);

        private sealed class RecordingExternalClock : IExternalTransportClock
        {
            public int CallCount { get; private set; }
            public double Bpm { get; private set; }
            public double Beat { get; private set; }
            public bool Running { get; private set; }
            public void SetExternalClock(double bpm, double beatPosition, bool running, double monotonicTimestamp)
            { CallCount++; Bpm = bpm; Beat = beatPosition; Running = running; }
        }
    }
}
