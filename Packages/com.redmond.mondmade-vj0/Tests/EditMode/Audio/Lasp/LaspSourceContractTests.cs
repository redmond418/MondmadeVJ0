using NUnit.Framework;

namespace MondmadeVJ0.Tests.Audio
{
    public sealed class LaspSourceContractTests
    {
        [TestCase(1f, 1f)]
        [TestCase(0.1f, 2f / 3f)]
        [TestCase(0.001f, 0f)]
        [TestCase(0f, 0f)]
        [TestCase(float.NaN, 0f)]
        public void PeakAmplitudeUsesTheConfiguredDbfsRange(float amplitude, float expected)
            => Assert.That(LaspSourceHelpers.NormalizeAmplitude(amplitude, -60f, 0f), Is.EqualTo(expected).Within(0.0001f));

        [Test]
        public void PeakReadsAbsoluteMaximumFromTheLatestBuffer()
        {
            var samples = new[] { 0.1f, -0.45f, 0.2f, float.NaN, 0.3f };

            Assert.That(LaspSourceHelpers.ReadPeak(samples), Is.EqualTo(0.45f).Within(0.0001f));
        }

        [Test]
        public void SpectrumBinClampsToAvailableRangeAndZeroesInvalidData()
        {
            var spectrum = new[] { -0.2f, 0.25f, 1.2f, float.NaN };

            Assert.That(LaspSourceHelpers.ReadSpectrumBin(spectrum, -1), Is.Zero);
            Assert.That(LaspSourceHelpers.ReadSpectrumBin(spectrum, 1), Is.EqualTo(0.25f));
            Assert.That(LaspSourceHelpers.ReadSpectrumBin(spectrum, 2), Is.EqualTo(1f));
            Assert.That(LaspSourceHelpers.ReadSpectrumBin(spectrum, 3), Is.Zero);
            Assert.That(LaspSourceHelpers.ReadSpectrumBin(System.ReadOnlySpan<float>.Empty, 0), Is.Zero);
        }

        [Test]
        public void SpectrumBandAveragesBinsBySampleRateAndFftSize()
        {
            var spectrum = new[] { 0.1f, 0.2f, 0.4f, 0.8f };

            Assert.That(LaspSourceHelpers.AverageSpectrumBand(spectrum, 48000, 8, 5000f, 13000f), Is.EqualTo(0.3f).Within(0.0001f));
            Assert.That(LaspSourceHelpers.AverageSpectrumBand(spectrum, 48000, 8, 12000f, 30000f), Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(LaspSourceHelpers.AverageSpectrumBand(spectrum, 48000, 8, 100f, 200f), Is.Zero);
        }

        [Test]
        public void DefaultBandsAndCustomBandsUseHzAndClipAtNyquist()
        {
            LaspSourceHelpers.GetBandRange(LaspBand.Low, 0f, 0f, out var low, out var high);
            Assert.That(low, Is.EqualTo(20f));
            Assert.That(high, Is.EqualTo(200f));
            LaspSourceHelpers.GetBandRange(LaspBand.Mid, 0f, 0f, out low, out high);
            Assert.That(low, Is.EqualTo(200f));
            Assert.That(high, Is.EqualTo(2000f));
            LaspSourceHelpers.GetBandRange(LaspBand.High, 0f, 0f, out low, out high);
            Assert.That(low, Is.EqualTo(2000f));
            Assert.That(high, Is.EqualTo(12000f));
            LaspSourceHelpers.GetBandRange(LaspBand.CustomHz, 100f, 20000f, out low, out high);
            Assert.That(LaspSourceHelpers.AverageSpectrumBand(new[] { 0.1f, 0.2f, 0.4f, 0.8f }, 48000, 8, low, high), Is.EqualTo(0.46666667f).Within(0.0001f));
        }

        [Test]
        public void InvalidSpectrumGeometryAndBandRangesAreRejected()
        {
            Assert.Throws<System.InvalidOperationException>(() => LaspSourceHelpers.AverageSpectrumBand(new[] { 1f }, 0, 2, 0f, 1f));
            Assert.Throws<System.InvalidOperationException>(() => LaspSourceHelpers.GetBandRange(LaspBand.CustomHz, 200f, 200f, out _, out _));
            Assert.Throws<System.InvalidOperationException>(() => LaspSourceHelpers.ValidateChannel(1, 1));
        }

        [Test]
        public void ThresholdTriggerInitializesAboveThresholdWithoutFiringAndUsesHysteresis()
        {
            var state = new AudioThresholdTriggerState(0.75f, 0.1f, 0.08f, 0.9f);

            Assert.That(state.Observe(0.8f, 0.01), Is.False);
            Assert.That(state.Observe(0.65f, 0.02), Is.False);
            Assert.That(state.Observe(0.74f, 0.03), Is.False);
            Assert.That(state.Observe(0.75f, 0.04), Is.True);
            Assert.That(state.Observe(0.9f, 0.2), Is.False);
            Assert.That(state.Observe(0.65f, 0.21), Is.False);
            Assert.That(state.Observe(0.8f, 0.22), Is.True);
        }

        [Test]
        public void PeakTriggerFiresOnFirstDownSlopeAndWaitsForResetAndCooldown()
        {
            var state = new AudioPeakTriggerState(0.8f, 0.1f, 0.08f, 0f);

            Assert.That(state.Observe(0.7f, 0.00), Is.False);
            Assert.That(state.Observe(0.9f, 0.01), Is.False);
            Assert.That(state.Observe(0.85f, 0.02), Is.True);
            Assert.That(state.Observe(0.82f, 0.03), Is.False);
            Assert.That(state.Observe(0.69f, 0.04), Is.False);
            Assert.That(state.Observe(0.9f, 0.05), Is.False);
            Assert.That(state.Observe(0.88f, 0.06), Is.False);
            Assert.That(state.Observe(0.69f, 0.07), Is.False);
            Assert.That(state.Observe(0.9f, 0.11), Is.False);
            Assert.That(state.Observe(0.89f, 0.12), Is.True);
        }

        [Test]
        public void PeakTriggerStartingAboveThresholdWaitsForResetBeforeArming()
        {
            var state = new AudioPeakTriggerState(0.8f, 0.1f, 0.08f, 0.95f);

            Assert.That(state.Observe(0.9f, 0.1), Is.False);
            Assert.That(state.Observe(0.5f, 0.2), Is.False);
            Assert.That(state.Observe(0.85f, 0.3), Is.False);
            Assert.That(state.Observe(0.8f, 0.4), Is.True);
        }

        [Test]
        public void AudioTriggerDefaultsMatchTheDocumentedCooldownAndHysteresis()
        {
            Assert.That(AudioThresholdTriggerSource.DefaultCooldownSeconds, Is.EqualTo(0.08f));
            Assert.That(AudioThresholdTriggerSource.DefaultHysteresis, Is.EqualTo(0.1f));
            Assert.That(AudioPeakTriggerSource.DefaultThreshold, Is.EqualTo(0.8f));
        }
    }
}
