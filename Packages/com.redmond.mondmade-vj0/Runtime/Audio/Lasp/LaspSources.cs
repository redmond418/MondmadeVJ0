using System;
using Lasp;
using R3;
using Unity.Collections;
using UnityEngine;

namespace MondmadeVJ0
{
    public enum LaspBand { Low, Mid, High, CustomHz }

    internal static class LaspSourceHelpers
    {
        public static InputStream Open(string deviceId, int channel)
        {
            var stream = string.IsNullOrWhiteSpace(deviceId) ? AudioSystem.GetDefaultInputStream() : AudioSystem.GetInputStream(deviceId);
            if (stream == null || !stream.IsValid) return null;
            ValidateChannel(channel, stream.ChannelCount);
            return stream;
        }

        public static void ValidateChannel(int channel, int channelCount)
        {
            if (channelCount < 0 || channel < 0 || channel >= channelCount)
                throw new InvalidOperationException($"Lasp channel {channel} is outside the input stream's {channelCount} channels.");
        }

        public static float NormalizeDb(float db, float minimumDb, float maximumDb)
        {
            if (!IsFinite(minimumDb) || !IsFinite(maximumDb) || maximumDb <= minimumDb)
                throw new InvalidOperationException("Lasp dB range must be finite and increasing.");
            if (!IsFinite(db)) return 0f;
            return Mathf.Clamp01(Mathf.InverseLerp(minimumDb, maximumDb, db));
        }

        public static float NormalizeAmplitude(float amplitude, float minimumDb, float maximumDb)
        {
            if (!IsFinite(amplitude) || amplitude <= 0f) return NormalizeDb(float.NegativeInfinity, minimumDb, maximumDb);
            return NormalizeDb(20f * Mathf.Log10(amplitude), minimumDb, maximumDb);
        }

        public static float ReadPeak(InputStream stream, int channel)
        {
            if (stream == null || !stream.IsValid) return 0f;
            try
            {
                var samples = stream.GetChannelDataSlice(channel);
                var peak = 0f;
                for (var i = 0; i < samples.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
                return peak;
            }
            catch (InvalidOperationException) { return 0f; }
        }

        public static float ReadPeak(ReadOnlySpan<float> samples)
        {
            var peak = 0f;
            for (var i = 0; i < samples.Length; i++)
                if (IsFinite(samples[i])) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
            return peak;
        }

        public static float ReadSpectrumBin(ReadOnlySpan<float> spectrum, int binIndex)
        {
            if (spectrum.Length == 0) return 0f;
            var value = spectrum[Mathf.Clamp(binIndex, 0, spectrum.Length - 1)];
            return IsFinite(value) ? Mathf.Clamp01(value) : 0f;
        }

        public static float AverageSpectrumBand(ReadOnlySpan<float> spectrum, int sampleRate, int fftSize, float lowHz, float highHz)
        {
            if (sampleRate <= 0 || fftSize <= 0 || !IsFinite(lowHz) || !IsFinite(highHz) || lowHz < 0f || highHz <= lowHz)
                throw new InvalidOperationException("Spectrum sample rate, FFT size, and frequency range must be valid.");
            if (spectrum.Length == 0) return 0f;

            var nyquist = sampleRate * 0.5f;
            var lower = Mathf.Min(lowHz, nyquist);
            var upper = Mathf.Min(highHz, nyquist);
            if (upper <= lower) return 0f;

            var total = 0d;
            var count = 0;
            for (var i = 0; i < spectrum.Length; i++)
            {
                var frequency = (double)i * sampleRate / fftSize;
                if (frequency < lower || frequency >= upper) continue;
                var value = spectrum[i];
                if (!IsFinite(value)) continue;
                total += Mathf.Clamp01(value);
                count++;
            }
            return count == 0 ? 0f : Mathf.Clamp01((float)(total / count));
        }

        public static void GetBandRange(LaspBand band, float customLowHz, float customHighHz, out float lowHz, out float highHz)
        {
            switch (band)
            {
                case LaspBand.Low: lowHz = 20f; highHz = 200f; return;
                case LaspBand.Mid: lowHz = 200f; highHz = 2000f; return;
                case LaspBand.High: lowHz = 2000f; highHz = 12000f; return;
                case LaspBand.CustomHz:
                    if (!IsFinite(customLowHz) || !IsFinite(customHighHz) || customLowHz <= 0f || customHighHz <= customLowHz)
                        throw new InvalidOperationException("Custom Lasp band must have a finite, positive, increasing frequency range.");
                    lowHz = customLowHz;
                    highHz = customHighHz;
                    return;
                default: throw new InvalidOperationException($"Unsupported Lasp band value: {band}.");
            }
        }

        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static IFrameClock RequireClock(RouteContext context) => context.FrameClock ?? throw new InvalidOperationException("Lasp sources require the Runtime FrameClock.");
    }

    [Serializable]
    public sealed class LaspLevelFloatSource : RouteNode, IValueSource<float>
    {
        [SerializeField] private string deviceId;
        [SerializeField, Min(0)] private int channel;
        [SerializeField] private float minimumDb = -60f;
        [SerializeField] private float maximumDb;
        public Observable<float> Build(RouteContext context)
        {
            var clock = LaspSourceHelpers.RequireClock(context);
            LaspSourceHelpers.NormalizeDb(0f, minimumDb, maximumDb);
            var stream = LaspSourceHelpers.Open(deviceId, channel);
            return Observable.Create<float>(observer =>
            {
                float Read()
                {
                    if (stream == null || !stream.IsValid) stream = LaspSourceHelpers.Open(deviceId, channel);
                    if (stream == null) return 0f;
                    try { return LaspSourceHelpers.NormalizeDb(stream.GetChannelLevel(channel), minimumDb, maximumDb); }
                    catch (InvalidOperationException) { stream = null; return 0f; }
                }
                observer.OnNext(Read());
                Action<double, double> update = (_, __) => observer.OnNext(Read());
                clock.Updated += update;
                return Disposable.Create(() => clock.Updated -= update);
            });
        }
    }

    [Serializable]
    public sealed class LaspBandFloatSource : RouteNode, IValueSource<float>
    {
        [SerializeField] private SpectrumAnalyzer target;
        [SerializeField] private LaspBand band;
        [SerializeField, Min(1f)] private float customLowHz = 200f;
        [SerializeField, Min(2f)] private float customHighHz = 2000f;
        public Observable<float> Build(RouteContext context)
        {
            if (target == null) throw new InvalidOperationException("Lasp SpectrumAnalyzer target is not assigned.");
            var clock = LaspSourceHelpers.RequireClock(context);
            LaspSourceHelpers.GetBandRange(band, customLowHz, customHighHz, out var lowHz, out var highHz);
            var stream = LaspSourceHelpers.Open(target.useDefaultDevice ? null : target.deviceID, target.channel);
            return Observable.Create<float>(observer =>
            {
                float Read()
                {
                    if (!target.isActiveAndEnabled) return 0f;
                    if (stream == null || !stream.IsValid)
                        stream = LaspSourceHelpers.Open(target.useDefaultDevice ? null : target.deviceID, target.channel);
                    if (stream == null || !stream.IsValid || stream.SampleRate <= 0) return 0f;
                    return LaspSourceHelpers.AverageSpectrumBand(target.spectrumSpan, stream.SampleRate, target.resolution * 2, lowHz, highHz);
                }
                observer.OnNext(Read());
                Action<double, double> update = (_, __) => observer.OnNext(Read());
                clock.Updated += update;
                return Disposable.Create(() => clock.Updated -= update);
            });
        }
    }

    [Serializable]
    public sealed class LaspPeakFloatSource : RouteNode, IValueSource<float>
    {
        [SerializeField] private string deviceId;
        [SerializeField, Min(0)] private int channel;
        [SerializeField] private float minimumDb = -60f;
        [SerializeField] private float maximumDb;
        public Observable<float> Build(RouteContext context)
        {
            var clock = LaspSourceHelpers.RequireClock(context);
            LaspSourceHelpers.NormalizeDb(0f, minimumDb, maximumDb);
            var stream = LaspSourceHelpers.Open(deviceId, channel);
            return Observable.Create<float>(observer =>
            {
                float Read()
                {
                    if (stream == null || !stream.IsValid) stream = LaspSourceHelpers.Open(deviceId, channel);
                    return LaspSourceHelpers.NormalizeAmplitude(LaspSourceHelpers.ReadPeak(stream, channel), minimumDb, maximumDb);
                }
                observer.OnNext(Read());
                Action<double, double> update = (_, __) => observer.OnNext(Read());
                clock.Updated += update;
                return Disposable.Create(() => clock.Updated -= update);
            });
        }
    }

    [Serializable]
    public sealed class LaspSpectrumBinFloatSource : RouteNode, IValueSource<float>
    {
        [SerializeField] private SpectrumAnalyzer target;
        [SerializeField, Min(0)] private int binIndex;
        public Observable<float> Build(RouteContext context)
        {
            if (target == null) throw new InvalidOperationException("Lasp SpectrumAnalyzer target is not assigned.");
            var clock = LaspSourceHelpers.RequireClock(context);
            var stream = LaspSourceHelpers.Open(target.useDefaultDevice ? null : target.deviceID, target.channel);
            return Observable.Create<float>(observer =>
            {
                float Read()
                {
                    if (!target.isActiveAndEnabled) return 0f;
                    if (stream == null || !stream.IsValid)
                        stream = LaspSourceHelpers.Open(target.useDefaultDevice ? null : target.deviceID, target.channel);
                    return stream == null || !stream.IsValid ? 0f : LaspSourceHelpers.ReadSpectrumBin(target.spectrumSpan, binIndex);
                }
                observer.OnNext(Read());
                Action<double, double> update = (_, __) => observer.OnNext(Read());
                clock.Updated += update;
                return Disposable.Create(() => clock.Updated -= update);
            });
        }
    }

    internal sealed class AudioThresholdTriggerState
    {
        private readonly float threshold;
        private readonly float resetThreshold;
        private readonly float cooldownSeconds;
        private bool armed;
        private double lastTrigger = double.NegativeInfinity;

        public AudioThresholdTriggerState(float threshold, float hysteresis, float cooldownSeconds, float initialLevel)
        {
            this.threshold = threshold;
            resetThreshold = threshold - hysteresis;
            this.cooldownSeconds = cooldownSeconds;
            armed = initialLevel < threshold;
        }

        public bool Observe(float level, double now)
        {
            if (!armed)
            {
                if (level <= resetThreshold) armed = true;
                return false;
            }
            if (level < threshold) return false;
            armed = false;
            if (now - lastTrigger < cooldownSeconds) return false;
            lastTrigger = now;
            return true;
        }
    }

    internal sealed class AudioPeakTriggerState
    {
        private readonly float threshold;
        private readonly float resetThreshold;
        private readonly float cooldownSeconds;
        private bool armed;
        private float previous;
        private float localPeak;
        private double lastTrigger = double.NegativeInfinity;

        public AudioPeakTriggerState(float threshold, float hysteresis, float cooldownSeconds, float initialLevel)
        {
            this.threshold = threshold;
            resetThreshold = threshold - hysteresis;
            this.cooldownSeconds = cooldownSeconds;
            armed = initialLevel < threshold;
            previous = localPeak = initialLevel;
        }

        public bool Observe(float level, double now)
        {
            if (!armed)
            {
                previous = level;
                if (level <= resetThreshold)
                {
                    armed = true;
                    localPeak = level;
                }
                return false;
            }

            var isDescending = level < previous;
            if (!isDescending)
            {
                localPeak = Mathf.Max(localPeak, level);
                previous = level;
                return false;
            }

            previous = level;
            if (localPeak < threshold)
            {
                localPeak = level;
                return false;
            }

            armed = false;
            localPeak = level;
            if (now - lastTrigger < cooldownSeconds) return false;
            lastTrigger = now;
            return true;
        }
    }

    [Serializable]
    public sealed class AudioThresholdTriggerSource : RouteNode, ITriggerSource
    {
        internal const float DefaultHysteresis = 0.1f;
        internal const float DefaultCooldownSeconds = 0.08f;
        [SerializeField] private string deviceId;
        [SerializeField, Min(0)] private int channel;
        [SerializeField] private FilterType filter;
        [SerializeField] private float minimumDb = -60f;
        [SerializeField] private float maximumDb;
        [SerializeField, Range(0f, 1f)] private float threshold = 0.75f;
        [SerializeField, Range(0f, 1f)] private float hysteresis = DefaultHysteresis;
        [SerializeField, Min(0f)] private float cooldownSeconds = DefaultCooldownSeconds;
        public Observable<Unit> Build(RouteContext context)
        {
            var clock = LaspSourceHelpers.RequireClock(context);
            LaspSourceHelpers.NormalizeDb(0f, minimumDb, maximumDb);
            ValidateThreshold(threshold, hysteresis, cooldownSeconds);
            var stream = LaspSourceHelpers.Open(deviceId, channel);
            return Observable.Create<Unit>(observer =>
            {
                float Read()
                {
                    if (stream == null || !stream.IsValid) stream = LaspSourceHelpers.Open(deviceId, channel);
                    if (stream == null) return 0f;
                    try { return LaspSourceHelpers.NormalizeDb(stream.GetChannelLevel(channel, filter), minimumDb, maximumDb); }
                    catch (InvalidOperationException) { stream = null; return 0f; }
                }
                var state = new AudioThresholdTriggerState(threshold, hysteresis, cooldownSeconds, Read());
                Action<double, double> update = (_, now) => { if (state.Observe(Read(), now)) observer.OnNext(Unit.Default); };
                clock.Updated += update;
                return Disposable.Create(() => clock.Updated -= update);
            });
        }
        internal static void ValidateThreshold(float value, float hysteresis, float cooldown)
        {
            if (!LaspSourceHelpers.IsFinite(value) || !LaspSourceHelpers.IsFinite(hysteresis) || !LaspSourceHelpers.IsFinite(cooldown) || value < 0f || value > 1f || hysteresis < 0f || hysteresis > value || cooldown < 0f)
                throw new InvalidOperationException("Audio threshold, hysteresis, and cooldown values are invalid.");
        }
    }

    [Serializable]
    public sealed class AudioPeakTriggerSource : RouteNode, ITriggerSource
    {
        internal const float DefaultThreshold = 0.8f;
        [SerializeField] private string deviceId;
        [SerializeField, Min(0)] private int channel;
        [SerializeField] private float minimumDb = -60f;
        [SerializeField] private float maximumDb;
        [SerializeField, Range(0f, 1f)] private float threshold = DefaultThreshold;
        [SerializeField, Range(0f, 1f)] private float hysteresis = AudioThresholdTriggerSource.DefaultHysteresis;
        [SerializeField, Min(0f)] private float cooldownSeconds = AudioThresholdTriggerSource.DefaultCooldownSeconds;
        public Observable<Unit> Build(RouteContext context)
        {
            var clock = LaspSourceHelpers.RequireClock(context);
            LaspSourceHelpers.NormalizeDb(0f, minimumDb, maximumDb);
            AudioThresholdTriggerSource.ValidateThreshold(threshold, hysteresis, cooldownSeconds);
            var stream = LaspSourceHelpers.Open(deviceId, channel);
            return Observable.Create<Unit>(observer =>
            {
                float Read()
                {
                    if (stream == null || !stream.IsValid) stream = LaspSourceHelpers.Open(deviceId, channel);
                    return LaspSourceHelpers.NormalizeAmplitude(LaspSourceHelpers.ReadPeak(stream, channel), minimumDb, maximumDb);
                }
                var state = new AudioPeakTriggerState(threshold, hysteresis, cooldownSeconds, Read());
                Action<double, double> update = (_, now) => { if (state.Observe(Read(), now)) observer.OnNext(Unit.Default); };
                clock.Updated += update;
                return Disposable.Create(() => clock.Updated -= update);
            });
        }
    }
}
