using NUnit.Framework;

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
    }
}
