using System;
using System.Collections.Generic;
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
        public void ClockSchedulerOnlyDropsAPulseAfterItIsAtLeastOnePeriodLate()
        {
            Assert.That(MidiClockSender.IsClockPulseTooLateToQueue(1000, 1001, 10d), Is.False,
                "A small scheduler wake delay should still reach the output worker's deadline wait.");
            Assert.That(MidiClockSender.IsClockPulseTooLateToQueue(1000, 1009, 10d), Is.False);
            Assert.That(MidiClockSender.IsClockPulseTooLateToQueue(1000, 1010, 10d), Is.True,
                "A pulse one or more full periods past its deadline is dropped to avoid a catch-up burst.");
        }

        [Test]
        public void SeekDetectionAllowsForTheCurrentFrameBeatSpan()
        {
            Assert.That(MidiClockSender.GetSeekThresholdBeat(120d, 1d / 60d), Is.EqualTo(0.1d).Within(1e-9));
            Assert.That(MidiClockSender.GetSeekThresholdBeat(240d, 0.031d), Is.EqualTo(0.248d).Within(1e-9));
            Assert.That(MidiClockSender.GetSeekThresholdBeat(240d, -1d), Is.EqualTo(0.1d).Within(1e-9));
        }

        [Test]
        public void ExpectedBeatUsesTheAbsoluteEpochWhenScheduleStartsAfterBeatZero()
        {
            const double bpm = 120d;
            const double startingBeat = 3.25d;
            var startTicks = 1_000_000L;
            var ticksPerBeat = Stopwatch.Frequency * 60d / bpm;
            var epochTicks = startTicks - (long)(startingBeat * ticksPerBeat);
            var halfBeatLaterTicks = startTicks + (long)(ticksPerBeat / 2d);

            Assert.That(MidiClockSender.GetExpectedBeatAtTime(epochTicks, startTicks, bpm),
                Is.EqualTo(startingBeat).Within(1e-6d));
            Assert.That(MidiClockSender.GetExpectedBeatAtTime(epochTicks, halfBeatLaterTicks, bpm),
                Is.EqualTo(startingBeat + 0.5d).Within(1e-6d));
        }

        [Test]
        public void SeekDetectionUsesTimelineRevisionForSmallDiscontinuities()
        {
            Assert.That(MidiClockSender.HasTimelineSeeked(7, 7, 4.01d, 4d, 0.1d), Is.False,
                "Small continuous transport corrections remain below the configured seek threshold.");
            Assert.That(MidiClockSender.HasTimelineSeeked(7, 8, 4.01d, 4d, 0.1d), Is.True,
                "An explicit timeline discontinuity must resynchronize even when its beat offset is small.");
            Assert.That(MidiClockSender.HasTimelineSeeked(7, 7, 4.2d, 4d, 0.1d), Is.True,
                "A large beat divergence remains a fallback seek signal.");
        }

        [Test]
        public void SharedOutputQueuePrioritizesClockAndCoalescesLedPayloads()
        {
            var queue = new MidiOutputWorkQueue();
            var metrics = new MidiClockSendMetrics();
            queue.SetClockGeneration(1);
            var mutableLed = new byte[] { 0x96, 56, 21 };
            Assert.That(queue.EnqueueLed("pad", mutableLed), Is.True);
            mutableLed[2] = 0;
            Assert.That(queue.EnqueueLed("pad", new byte[] { 0x96, 56, 0 }), Is.True);
            Assert.That(queue.EnqueueClock(100, 10, 1, metrics), Is.True);
            Assert.That(queue.EnqueueClock(120, 10, 1, metrics), Is.True);
            Assert.That(queue.EnqueueControl(new byte[] { 0xFA }), Is.True);

            Assert.That(queue.Count, Is.EqualTo(4), "Repeated changes to one LED replace its pending payload while both future Clock deadlines are retained.");
            Assert.That(metrics.MissedPulses, Is.Zero, "Future scheduled Clock pulses are retained until their deadlines instead of replacing one another.");
            Assert.That(queue.TryDequeue(long.MaxValue, 0d, out var start), Is.True);
            Assert.That(start.Kind, Is.EqualTo(MidiOutputPacketKind.Control));
            Assert.That(start.Message[0], Is.EqualTo(0xFA));
            Assert.That(queue.TryDequeue(long.MaxValue, 0d, out var clock), Is.True);
            Assert.That(clock.Kind, Is.EqualTo(MidiOutputPacketKind.Clock), "Clock must be sent before queued LED feedback.");
            Assert.That(clock.DeadlineTicks, Is.EqualTo(100), "Clock deadlines remain ordered and the earliest pulse is sent first.");
            Assert.That(queue.TryDequeue(long.MaxValue, 0d, out var nextClock), Is.True);
            Assert.That(nextClock.Kind, Is.EqualTo(MidiOutputPacketKind.Clock));
            Assert.That(nextClock.DeadlineTicks, Is.EqualTo(120));
            Assert.That(queue.TryDequeue(long.MaxValue, 0d, out var led), Is.True);
            Assert.That(led.Kind, Is.EqualTo(MidiOutputPacketKind.Led));
            Assert.That(led.Message[2], Is.EqualTo(0), "The latest desired LED state wins while it is queued.");
        }

        [Test]
        public void SharedOutputQueueDrainsLedWorkUntilClockEntersItsSpinWindow()
        {
            var queue = new MidiOutputWorkQueue();
            var metrics = new MidiClockSendMetrics();
            queue.SetClockGeneration(1);
            Assert.That(queue.EnqueueClock(1000, 100, 1, metrics), Is.True);
            Assert.That(queue.EnqueueLed("pad", new byte[] { 0x96, 56, 21 }), Is.True);

            Assert.That(queue.TryDequeue(900, 50d, out var led), Is.True);
            Assert.That(led.Kind, Is.EqualTo(MidiOutputPacketKind.Led), "A future Clock must not block LED delivery.");
            Assert.That(queue.Count, Is.EqualTo(1), "The scheduled Clock remains queued while LED work drains.");

            Assert.That(queue.TryDequeue(950, 50d, out var clock), Is.True);
            Assert.That(clock.Kind, Is.EqualTo(MidiOutputPacketKind.Clock), "Clock becomes the highest-priority item inside its final spin window.");
        }

        [Test]
        public void SharedOutputQueueStopsStartingLedMessagesInsideTheClockGuardWindow()
        {
            var queue = new MidiOutputWorkQueue();
            var metrics = new MidiClockSendMetrics();
            queue.SetClockGeneration(1);
            Assert.That(queue.EnqueueClock(1000, 100, 1, metrics), Is.True);
            Assert.That(queue.EnqueueLed("pad", new byte[] { 0x96, 56, 21 }), Is.True);

            Assert.That(queue.TryDequeue(925, 50d, out var earlyGuard, 100d), Is.False,
                "A possibly blocking LED transfer must not start during the pre-deadline guard window.");
            Assert.That(earlyGuard, Is.EqualTo(default(MidiOutputPacket)));
            Assert.That(queue.Count, Is.EqualTo(2), "Both Clock and LED work remain queued until the urgent window.");
            Assert.That(queue.TryDequeue(950, 50d, out var clock, 100d), Is.True);
            Assert.That(clock.Kind, Is.EqualTo(MidiOutputPacketKind.Clock));
        }

        [Test]
        public void SharedOutputQueueWaitsUntilBeforeTheNextClockSpinWindow()
        {
            var queue = new MidiOutputWorkQueue();
            queue.SetClockGeneration(1);
            var deadline = Stopwatch.Frequency / 10;
            Assert.That(queue.EnqueueClock(deadline, Stopwatch.Frequency / 2, 1, new MidiClockSendMetrics()), Is.True);

            Assert.That(queue.GetClockPollWaitMilliseconds(0, Stopwatch.Frequency * 5d / 1000d), Is.EqualTo(91),
                "The worker wakes one millisecond before the final three millisecond spin window rather than polling continuously.");
            Assert.That(queue.GetClockPollWaitMilliseconds(deadline - Stopwatch.Frequency * 5L / 1000L,
                Stopwatch.Frequency * 5d / 1000d), Is.EqualTo(0),
                "The worker is already in the final spin window at the scheduled send deadline.");
            Assert.That(queue.GetClockPollWaitMilliseconds(deadline - Stopwatch.Frequency * 10L / 1000L,
                Stopwatch.Frequency * 5d / 1000d), Is.EqualTo(1),
                "The worker sleeps outside the final spin window.");
        }

        [Test]
        public void SharedOutputQueuePrequeuesFutureClocksAndPreservesDeadlineOrder()
        {
            var queue = new MidiOutputWorkQueue();
            var metrics = new MidiClockSendMetrics();
            queue.SetClockGeneration(1);
            Assert.That(queue.EnqueueClock(1000, 100, 1, metrics), Is.True);
            Assert.That(queue.EnqueueClock(1100, 100, 1, metrics), Is.True);
            Assert.That(queue.EnqueueClock(1200, 100, 1, metrics), Is.True);
            Assert.That(queue.EnqueueLed("pad", new byte[] { 0x96, 56, 21 }), Is.True);

            Assert.That(queue.TryDequeue(800, 50d, out var led), Is.True,
                "LED work can continue while the earliest prequeued Clock is outside its guard window.");
            Assert.That(led.Kind, Is.EqualTo(MidiOutputPacketKind.Led));
            Assert.That(queue.TryDequeue(900, 50d, out var guarded, 100d), Is.False,
                "Prequeued future Clock deadlines stop LED sends inside their guard window.");
            Assert.That(queue.TryDequeue(950, 50d, out var first), Is.True);
            Assert.That(first.DeadlineTicks, Is.EqualTo(1000));
            Assert.That(queue.TryDequeue(1050, 50d, out var second), Is.True);
            Assert.That(second.DeadlineTicks, Is.EqualTo(1100));
            Assert.That(queue.TryDequeue(1150, 50d, out var third), Is.True);
            Assert.That(third.DeadlineTicks, Is.EqualTo(1200));
            Assert.That(metrics.MissedPulses, Is.Zero);
        }

        [Test]
        public void ApcPadLedQueueCoalescesLatestStateAndBuildsProtocolBulkMessagesOfAtMost32Pads()
        {
            var queue = new MidiOutputWorkQueue();
            var updates = new List<ApcPadLedUpdate>();
            for (byte pad = 0; pad < 40; pad++)
                updates.Add(new ApcPadLedUpdate("pad_" + pad, pad, 255, 0, 128));
            updates.Add(new ApcPadLedUpdate("pad_0", 0, 0, 255, 0));
            Assert.That(queue.EnqueueApcPadLeds(updates), Is.True);

            Assert.That(queue.Count, Is.EqualTo(40), "Repeated Pad updates coalesce per physical control.");
            Assert.That(queue.TryDequeue(long.MaxValue, 0d, out var first), Is.True);
            Assert.That(first.Kind, Is.EqualTo(MidiOutputPacketKind.Led));
            Assert.That(first.LedControlCount, Is.EqualTo(32));
            Assert.That(first.Message.Length, Is.EqualTo(7 + 32 * 8 + 1));
            Assert.That(first.Message[0..7], Is.EqualTo(new byte[] { 0xF0, 0x47, 0x7F, 0x4F, 0x24, 0x02, 0x00 }));
            Assert.That(first.Message[7..15], Is.EqualTo(new byte[] { 0, 0, 0, 0, 1, 127, 0, 0 }),
                "The newest green state for Pad 0 wins and 8-bit RGB components are split into 7-bit MSB/LSB pairs.");
            Assert.That(first.Message[15..23], Is.EqualTo(new byte[] { 1, 1, 1, 127, 0, 0, 1, 0 }),
                "Pad 1 is encoded as the same start/end pad value followed by its MSB/LSB RGB components.");
            Assert.That(first.Message[^1], Is.EqualTo(0xF7));

            var totalControls = first.LedControlCount;
            while (queue.TryDequeue(long.MaxValue, 0d, out var next))
            {
                Assert.That(next.LedControlCount, Is.InRange(1, 32));
                totalControls += next.LedControlCount;
            }
            Assert.That(totalControls, Is.EqualTo(40));
            Assert.That(queue.Count, Is.Zero);
        }

        [Test]
        public void ApcPadLedBulkQueueDoesNotBatchPeripheralButtonMessages()
        {
            var queue = new MidiOutputWorkQueue();
            queue.EnqueueApcPadLed("pad", 56, 0, 255, 0);
            queue.EnqueueLed("track", new byte[] { 0x90, 0x64, 0x01 });

            Assert.That(queue.TryDequeue(long.MaxValue, 0d, out var bulk), Is.True);
            Assert.That(bulk.LedControlCount, Is.EqualTo(1));
            Assert.That(bulk.Message[0], Is.EqualTo(0xF0));
            Assert.That(queue.TryDequeue(long.MaxValue, 0d, out var button), Is.True);
            Assert.That(button.Message, Is.EqualTo(new byte[] { 0x90, 0x64, 0x01 }));
        }

        [Test]
        public void FailedApcPadBulkSendRequeuesDesiredStatesWithoutReplacingNewerValues()
        {
            var queue = new MidiOutputWorkQueue();
            queue.EnqueueApcPadLed("pad_0", 0, 255, 0, 0);
            queue.EnqueueApcPadLed("pad_1", 1, 0, 0, 255);
            Assert.That(queue.TryDequeue(long.MaxValue, 0d, out var failedBatch), Is.True);
            Assert.That(failedBatch.ApcPadLedUpdates, Has.Length.EqualTo(2));

            queue.EnqueueApcPadLed("pad_0", 0, 0, 255, 0);
            queue.RequeueLedUnlessNewer(failedBatch);

            Assert.That(queue.TryDequeue(long.MaxValue, 0d, out var retryBatch), Is.True);
            Assert.That(retryBatch.Kind, Is.EqualTo(MidiOutputPacketKind.Led));
            Assert.That(retryBatch.LedControlCount, Is.EqualTo(2));
            Assert.That(retryBatch.Message[7..15], Is.EqualTo(new byte[] { 1, 1, 0, 0, 0, 0, 1, 127 }),
                "Pad 1's blue state is retried after the failed send.");
            Assert.That(retryBatch.Message[15..23], Is.EqualTo(new byte[] { 0, 0, 0, 0, 1, 127, 0, 0 }),
                "Pad 0's newer green state replaces the older red state from the failed batch.");
        }

        [Test]
        public void SharedOutputQueueDiscardsStaleRealtimeMessagesButKeepsDesiredLeds()
        {
            var queue = new MidiOutputWorkQueue();
            var metrics = new MidiClockSendMetrics();
            queue.SetClockGeneration(1);
            queue.EnqueueControl(new byte[] { 0xFA });
            queue.EnqueueClock(100, 10, 1, metrics);
            queue.EnqueueLed("pad", new byte[] { 0x96, 56, 21 });

            queue.DiscardRealtime();

            Assert.That(metrics.MissedPulses, Is.EqualTo(1));
            Assert.That(queue.DroppedMessages, Is.EqualTo(1), "Unsent transport controls are visible in output queue drop metrics.");
            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(queue.TryDequeue(long.MaxValue, 0d, out var led), Is.True);
            Assert.That(led.Kind, Is.EqualTo(MidiOutputPacketKind.Led));
            Assert.That(led.Message[2], Is.EqualTo(21));
        }

        [Test]
        public void SharedOutputQueueDropsPendingClockWhenTransportScheduleGenerationChanges()
        {
            var queue = new MidiOutputWorkQueue();
            var metrics = new MidiClockSendMetrics();
            queue.SetClockGeneration(7);
            Assert.That(queue.EnqueueClock(100, 10, 7, metrics), Is.True);

            queue.SetClockGeneration(8);

            Assert.That(queue.Count, Is.Zero);
            Assert.That(metrics.MissedPulses, Is.EqualTo(1));
            Assert.That(queue.EnqueueClock(110, 10, 7, metrics), Is.False, "A stale scheduler cannot add another pulse after a seek or Stop.");
            Assert.That(queue.DroppedMessages, Is.EqualTo(1));
        }

        [Test]
        public void SharedOutputQueueDoesNotReportAnIntentionallyCancelledFutureClockAsMissed()
        {
            var queue = new MidiOutputWorkQueue();
            var metrics = new MidiClockSendMetrics();
            queue.SetClockGeneration(7);
            var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
            Assert.That(queue.EnqueueClock(deadline, Stopwatch.Frequency / 2, 7, metrics), Is.True);

            queue.SetClockGeneration(8);

            Assert.That(queue.Count, Is.Zero);
            Assert.That(metrics.MissedPulses, Is.Zero);
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
        public void ClockMetricsMeasureScheduledSubmissionAgainstItsBackendLeadTime()
        {
            var metrics = new MidiClockSendMetrics();
            var lead = Stopwatch.Frequency / 125L;
            var oneMillisecond = Stopwatch.Frequency / 1000L;
            metrics.RecordScheduled(100 * Stopwatch.Frequency, 100 * Stopwatch.Frequency - lead + oneMillisecond, 1, lead);
            metrics.RecordScheduled(101 * Stopwatch.Frequency, 101 * Stopwatch.Frequency - lead + oneMillisecond, 1, lead);
            metrics.RecordScheduled(102 * Stopwatch.Frequency, 102 * Stopwatch.Frequency - lead + 3 * oneMillisecond, 1, lead);

            Assert.That(metrics.MaximumDeadlineDriftMilliseconds, Is.EqualTo(2d).Within(0.001d));
            Assert.That(metrics.MaximumLatenessMilliseconds, Is.EqualTo(3d).Within(0.001d));
            Assert.That(metrics.SentPulses, Is.EqualTo(3));

        }

#if UNITY_EDITOR_OSX
        [TestCase("APC mini mk2 Control", "Control", true)]
        [TestCase("MIDI Device:Control", "Control", true)]
        [TestCase("Control", "APC mini mk2 Control", true)]
        [TestCase("APC mini mk2 Control", "Notes", false)]
        [TestCase("APC mini mk2Control", "Control", false)]
        [TestCase("APC mini mk2 Control 2", "Control", false)]
        public void CoreMidiNameResolutionRequiresAnExactOrSeparatedSuffixMatch(string rtMidiName, string coreMidiName, bool expected)
        {
            Assert.That(CoreMidiScheduledClockOutput.ArePortNamesCompatible(rtMidiName, coreMidiName), Is.EqualTo(expected));
        }

        [Test]
        public void CoreMidiDestinationResolutionPrefersAnExactUniqueNameAndRejectsAmbiguousMatches()
        {
            Assert.That(CoreMidiScheduledClockOutput.ResolveUniqueDestinationIndex(
                "APC mini mk2 Control", new[] { "Control", "APC mini mk2 Control" }), Is.EqualTo(1));
            Assert.That(CoreMidiScheduledClockOutput.ResolveUniqueDestinationIndex(
                "APC mini mk2 Control", new[] { "Control" }), Is.EqualTo(0));
            Assert.That(CoreMidiScheduledClockOutput.ResolveUniqueDestinationIndex(
                "Control", new[] { "APC mini mk2 Control", "MIDI Device:Control" }), Is.EqualTo(-1));
            Assert.That(CoreMidiScheduledClockOutput.ResolveUniqueDestinationIndex(
                "APC mini mk2 Control", new[] { "APC mini mk2 Control", "APC mini mk2 Control" }), Is.EqualTo(-1));
        }

        [Test]
        public void CoreMidiTimestampConversionMapsStopwatchDeltaIntoMachHostTicks()
        {
            var timestamp = CoreMidiScheduledClockOutput.ConvertDeadlineToHostTime(
                deadlineTicks: 1010, nowTicks: 1000, nowHostTime: 500, timebaseNumerator: 1,
                timebaseDenominator: 1, stopwatchFrequency: 1000);

            Assert.That(timestamp, Is.EqualTo(10_000_500UL), "Ten milliseconds become ten million nanoseconds when the test timebase is 1 ns per host tick.");
        }

        [Test]
        public void CoreMidiTimestampConversionUsesImmediateTimestampForExpiredOrInvalidTargets()
        {
            Assert.That(CoreMidiScheduledClockOutput.ConvertDeadlineToHostTime(1000, 1000, 500, 1, 1, 1000), Is.Zero);
            Assert.That(CoreMidiScheduledClockOutput.ConvertDeadlineToHostTime(1010, 1000, 500, 0, 1, 1000), Is.Zero);
        }
#endif

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
