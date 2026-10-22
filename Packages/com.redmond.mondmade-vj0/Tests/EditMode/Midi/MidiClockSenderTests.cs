using System;
using NUnit.Framework;
using System.Diagnostics;

namespace MondmadeVJ0.Tests
{
    public sealed class MidiClockSenderTests
    {
        [TestCase(1100, 1)]
        [TestCase(1199, 1)]
        [TestCase(1200, 2)]
        [TestCase(1250, 2)]
        [TestCase(1300, 3)]
        public void SchedulerChoosesMostRecentDuePulseWithoutBursting(long now, long expected)
        {
            Assert.That(MidiClockSender.GetLatestDuePulseIndex(1000, now, 100d, 1), Is.EqualTo(expected));
        }

        [Test]
        public void SchedulerKeepsNextPulseWhenItsDeadlineHasNotPassed()
        {
            var next = MidiClockSender.GetLatestDuePulseIndex(1000, 1250, 100d, 2);
            Assert.That(next, Is.EqualTo(2));

            var later = MidiClockSender.GetLatestDuePulseIndex(1000, 1301, 100d, next);
            Assert.That(later, Is.EqualTo(3));
            Assert.That(later - next, Is.EqualTo(1), "At most one due pulse is sent per scheduler pass.");
        }

        [Test]
        public void SchedulerDoesNotMoveBeforeMinimumIndexWhenCalledEarly()
        {
            Assert.That(MidiClockSender.GetLatestDuePulseIndex(1000, 900, 100d, 5), Is.EqualTo(5));
        }

        [Test]
        public void SharedOutputQueuePrioritizesClockAndCoalescesLedPayloads()
        {
            var queue = new MidiOutputWorkQueue();
            var metrics = new MidiClockSendMetrics();
            var mutableLed = new byte[] { 0x96, 56, 21 };
            Assert.That(queue.EnqueueLed("pad", mutableLed), Is.True);
            mutableLed[2] = 0;
            Assert.That(queue.EnqueueLed("pad", new byte[] { 0x96, 56, 0 }), Is.True);
            Assert.That(queue.EnqueueClock(100, 10, 1, metrics), Is.True);
            Assert.That(queue.EnqueueClock(120, 10, 1, metrics), Is.True);
            Assert.That(queue.EnqueueControl(new byte[] { 0xFA }), Is.True);

            Assert.That(queue.Count, Is.EqualTo(3), "Repeated changes to one LED replace its pending payload.");
            Assert.That(metrics.MissedPulses, Is.EqualTo(1), "Replacing an unsent Clock pulse must count as a missed pulse, not create a burst.");
            Assert.That(queue.TryDequeue(out var start), Is.True);
            Assert.That(start.Kind, Is.EqualTo(MidiOutputPacketKind.Control));
            Assert.That(start.Message[0], Is.EqualTo(0xFA));
            Assert.That(queue.TryDequeue(out var clock), Is.True);
            Assert.That(clock.Kind, Is.EqualTo(MidiOutputPacketKind.Clock), "Clock must be sent before queued LED feedback.");
            Assert.That(clock.DeadlineTicks, Is.EqualTo(120), "The newest due Clock deadline replaces the stale pending pulse.");
            Assert.That(queue.TryDequeue(out var led), Is.True);
            Assert.That(led.Kind, Is.EqualTo(MidiOutputPacketKind.Led));
            Assert.That(led.Message[2], Is.EqualTo(0), "The latest desired LED state wins while it is queued.");
        }

        [Test]
        public void SharedOutputQueueDiscardsStaleRealtimeMessagesButKeepsDesiredLeds()
        {
            var queue = new MidiOutputWorkQueue();
            var metrics = new MidiClockSendMetrics();
            queue.EnqueueControl(new byte[] { 0xFA });
            queue.EnqueueClock(100, 10, 1, metrics);
            queue.EnqueueLed("pad", new byte[] { 0x96, 56, 21 });

            queue.DiscardRealtime();

            Assert.That(metrics.MissedPulses, Is.EqualTo(1));
            Assert.That(queue.DroppedMessages, Is.EqualTo(1), "Unsent transport controls are visible in output queue drop metrics.");
            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(queue.TryDequeue(out var led), Is.True);
            Assert.That(led.Kind, Is.EqualTo(MidiOutputPacketKind.Led));
            Assert.That(led.Message[2], Is.EqualTo(21));
        }

        [Test]
        public void ClockMetricsMeasureCumulativeDriftSeparatelyFromMaximumLateness()
        {
            var metrics = new MidiClockSendMetrics();
            var frequency = Stopwatch.Frequency;
            metrics.RecordSent(100, 110, 1);
            metrics.RecordSent(200, 210, 1);
            metrics.RecordSent(300, 320, 1);

            Assert.That(metrics.MaximumDeadlineDriftMilliseconds, Is.EqualTo(10_000d / frequency).Within(0.000001d));
            Assert.That(metrics.MaximumLatenessMilliseconds, Is.EqualTo(Math.Ceiling(20_000_000d / frequency) / 1000d).Within(0.000001d));
            Assert.That(metrics.SentPulses, Is.EqualTo(3));
        }

        [Test]
        public void SharedOutputQueueIsBoundedAndReportsRejectedNewMessages()
        {
            var queue = new MidiOutputWorkQueue();
            for (var i = 0; i < 1024; i++)
                Assert.That(queue.EnqueueLed("pad_" + i, new byte[] { 0x96, (byte)(i % 128), 0 }), Is.True);

            Assert.That(queue.EnqueueLed("overflow", new byte[] { 0x96, 0, 1 }), Is.False);
            Assert.That(queue.Count, Is.EqualTo(1024));
            Assert.That(queue.DroppedMessages, Is.EqualTo(1));
        }

        [Test]
        public void ClockMetricsReportTheP99InvocationLatenessFromBoundedHistogram()
        {
            var metrics = new MidiClockSendMetrics();
            var ticksPerMillisecond = Stopwatch.Frequency / 1000L;
            for (var i = 0; i < 98; i++) metrics.RecordSent(i * 10L, i * 10L + ticksPerMillisecond, 1);
            metrics.RecordSent(980, 980 + ticksPerMillisecond * 5, 1);
            metrics.RecordSent(990, 990 + ticksPerMillisecond * 5, 1);

            Assert.That(metrics.P99LatenessMilliseconds, Is.EqualTo(5d).Within(0.001d));
        }
    }
}
