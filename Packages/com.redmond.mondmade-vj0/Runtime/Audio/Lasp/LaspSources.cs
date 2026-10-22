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
            if (stream == null || !stream.IsValid) throw new InvalidOperationException("Lasp input stream is unavailable.");
            if (channel < 0 || channel >= stream.ChannelCount) throw new InvalidOperationException($"Lasp channel {channel} is outside the input stream's {stream.ChannelCount} channels.");
            return stream;
        }

        public static float NormalizeDb(float db, float minimumDb, float maximumDb)
        {
            if (!IsFinite(minimumDb) || !IsFinite(maximumDb) || maximumDb <= minimumDb)
                throw new InvalidOperationException("Lasp dB range must be finite and increasing.");
            if (!IsFinite(db)) return 0f;
            return Mathf.Clamp01(Mathf.InverseLerp(minimumDb, maximumDb, db));
        }

        public static float ReadPeak(InputStream stream, int channel)
        {
            var samples = stream.GetChannelDataSlice(channel);
            var peak = 0f;
            for (var i = 0; i < samples.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
            return Mathf.Clamp01(peak);
        }

        public static float ReadBand(InputStream stream, int channel, float lowHz, float highHz)
        {
            var samples = stream.GetChannelDataSlice(channel);
            var sampleRate = stream.SampleRate;
            if (samples.Length == 0 || sampleRate <= 0) return 0f;
            var nyquist = sampleRate * 0.5f;
            lowHz = Mathf.Clamp(lowHz, 1f, nyquist - 1f);
            highHz = Mathf.Clamp(highHz, lowHz + 1f, nyquist);
            var center = (lowHz + highHz) * 0.5f;
            var width = Math.Max(1, Math.Min(16, Mathf.CeilToInt((highHz - lowHz) / Mathf.Max(1f, (highHz - lowHz) / 16f))));
            var total = 0d;
            for (var band = 0; band < width; band++)
            {
                var frequency = lowHz + (band + 0.5f) * (highHz - lowHz) / width;
                var omega = 2d * Math.PI * frequency / sampleRate;
                var coefficient = 2d * Math.Cos(omega);
                double previous = 0d;
                double previous2 = 0d;
                for (var i = 0; i < samples.Length; i++)
                {
                    var current = samples[i] + coefficient * previous - previous2;
                    previous2 = previous;
                    previous = current;
                }
                var power = previous * previous + previous2 * previous2 - coefficient * previous * previous2;
                total += Math.Max(0d, power);
            }
            var rms = Math.Sqrt(total / width) * (2d / samples.Length);
            return Mathf.Clamp01((float)rms);
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
            var stream = LaspSourceHelpers.Open(deviceId, channel);
            var clock = LaspSourceHelpers.RequireClock(context);
            LaspSourceHelpers.NormalizeDb(0f, minimumDb, maximumDb);
            return Observable.Create<float>(observer =>
            {
                float Read() => LaspSourceHelpers.NormalizeDb(stream.GetChannelLevel(channel), minimumDb, maximumDb);
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
        [SerializeField] private string deviceId;
        [SerializeField, Min(0)] private int channel;
        [SerializeField] private LaspBand band;
        [SerializeField, Min(1f)] private float customLowHz = 200f;
        [SerializeField, Min(2f)] private float customHighHz = 2000f;
        [SerializeField] private float minimumDb = -60f;
        [SerializeField] private float maximumDb;
        public Observable<float> Build(RouteContext context)
        {
            var stream = LaspSourceHelpers.Open(deviceId, channel);
            var clock = LaspSourceHelpers.RequireClock(context);
            LaspSourceHelpers.NormalizeDb(0f, minimumDb, maximumDb);
            if (band == LaspBand.CustomHz && (!LaspSourceHelpers.IsFinite(customLowHz) || !LaspSourceHelpers.IsFinite(customHighHz) || customLowHz <= 0f || customHighHz <= customLowHz || customHighHz >= stream.SampleRate * 0.5f))
                throw new InvalidOperationException("Custom Lasp band must have an increasing range below the input Nyquist frequency.");
            return Observable.Create<float>(observer =>
            {
                float Read()
                {
                    if (band == LaspBand.CustomHz) return LaspSourceHelpers.NormalizeDb(20f * Mathf.Log10(Mathf.Max(1e-6f, LaspSourceHelpers.ReadBand(stream, channel, customLowHz, customHighHz))), minimumDb, maximumDb);
                    var type = band == LaspBand.Low ? FilterType.LowPass : band == LaspBand.Mid ? FilterType.BandPass : FilterType.HighPass;
                    return LaspSourceHelpers.NormalizeDb(stream.GetChannelLevel(channel, type), minimumDb, maximumDb);
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
        [SerializeField, Min(0f)] private float decayPerSecond = 1.5f;
        public Observable<float> Build(RouteContext context)
        {
            var stream = LaspSourceHelpers.Open(deviceId, channel);
            var clock = LaspSourceHelpers.RequireClock(context);
            if (!LaspSourceHelpers.IsFinite(decayPerSecond) || decayPerSecond < 0f) throw new InvalidOperationException("Peak decay must be finite and non-negative.");
            return Observable.Create<float>(observer =>
            {
                var peak = 0f;
                observer.OnNext(peak);
                Action<double, double> update = (delta, _) =>
                {
                    peak = Mathf.Max(LaspSourceHelpers.ReadPeak(stream, channel), peak - decayPerSecond * (float)Math.Max(0d, delta));
                    observer.OnNext(peak);
                };
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
        [SerializeField] private bool logarithmic = true;
        public Observable<float> Build(RouteContext context)
        {
            if (target == null) throw new InvalidOperationException("Lasp SpectrumAnalyzer target is not assigned.");
            var clock = LaspSourceHelpers.RequireClock(context);
            return Observable.Create<float>(observer =>
            {
                float Read()
                {
                    NativeArray<float> values = logarithmic ? target.logSpectrumArray : target.spectrumArray;
                    return !values.IsCreated || values.Length == 0 ? 0f : values[Mathf.Clamp(binIndex, 0, values.Length - 1)];
                }
                observer.OnNext(Read());
                Action<double, double> update = (_, __) => observer.OnNext(Read());
                clock.Updated += update;
                return Disposable.Create(() => clock.Updated -= update);
            });
        }
    }

    [Serializable]
    public sealed class AudioThresholdTriggerSource : RouteNode, ITriggerSource
    {
        [SerializeField] private string deviceId;
        [SerializeField, Min(0)] private int channel;
        [SerializeField] private FilterType filter;
        [SerializeField] private float minimumDb = -60f;
        [SerializeField] private float maximumDb;
        [SerializeField, Range(0f, 1f)] private float threshold = 0.75f;
        [SerializeField, Range(0f, 1f)] private float hysteresis = 0.1f;
        [SerializeField, Min(0f)] private float cooldownSeconds = 0.1f;
        public Observable<Unit> Build(RouteContext context)
        {
            var stream = LaspSourceHelpers.Open(deviceId, channel);
            var clock = LaspSourceHelpers.RequireClock(context);
            LaspSourceHelpers.NormalizeDb(0f, minimumDb, maximumDb);
            ValidateThreshold(threshold, hysteresis, cooldownSeconds);
            return Observable.Create<Unit>(observer =>
            {
                var armed = LaspSourceHelpers.NormalizeDb(stream.GetChannelLevel(channel, filter), minimumDb, maximumDb) < threshold;
                var lastTrigger = double.NegativeInfinity;
                Action<double, double> update = (_, now) =>
                {
                    var level = LaspSourceHelpers.NormalizeDb(stream.GetChannelLevel(channel, filter), minimumDb, maximumDb);
                    if (!armed && level < threshold - hysteresis) armed = true;
                    else if (armed && level >= threshold && now - lastTrigger >= cooldownSeconds)
                    { armed = false; lastTrigger = now; observer.OnNext(Unit.Default); }
                };
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
        [SerializeField] private string deviceId;
        [SerializeField, Min(0)] private int channel;
        [SerializeField, Range(0f, 1f)] private float threshold = 0.8f;
        [SerializeField, Min(0f)] private float cooldownSeconds = 0.1f;
        public Observable<Unit> Build(RouteContext context)
        {
            var stream = LaspSourceHelpers.Open(deviceId, channel);
            var clock = LaspSourceHelpers.RequireClock(context);
            AudioThresholdTriggerSource.ValidateThreshold(threshold, 0f, cooldownSeconds);
            return Observable.Create<Unit>(observer =>
            {
                var previous = 0f;
                var localPeak = 0f;
                var lastTrigger = double.NegativeInfinity;
                Action<double, double> update = (_, now) =>
                {
                    var level = LaspSourceHelpers.ReadPeak(stream, channel);
                    if (level >= previous) localPeak = Mathf.Max(localPeak, level);
                    else if (localPeak >= threshold && now - lastTrigger >= cooldownSeconds)
                    { lastTrigger = now; observer.OnNext(Unit.Default); localPeak = level; }
                    previous = level;
                };
                clock.Updated += update;
                return Disposable.Create(() => clock.Updated -= update);
            });
        }
    }
}
