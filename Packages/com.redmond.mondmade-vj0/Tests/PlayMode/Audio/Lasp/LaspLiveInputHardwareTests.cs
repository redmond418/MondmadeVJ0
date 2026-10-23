using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lasp;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MondmadeVJ0.Tests.Audio
{
    public sealed class LaspLiveInputHardwareTests
    {
        private const float CaptureSeconds = 8f;
        private const float ArmDelaySeconds = 2f;
        private const float MinimumAudibleNormalizedLevel = 0.05f;

        [Explicit("Opens the Background Music input when available, otherwise the default input, for 8 seconds. Play audio or speak briefly when capture starts; audio is not stored.")]
        [UnityTest]
        public IEnumerator LevelSourceReadsLivePreferredInputThroughRuntimeWithoutRecordingAudio()
        {
            var inputDevices = AudioSystem.InputDevices.Where(device => device.IsValid).ToArray();
            var selectedDevice = inputDevices.FirstOrDefault(device =>
                device.Name.IndexOf("Background Music", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!selectedDevice.IsValid) selectedDevice = AudioSystem.DefaultDevice;
            if (!selectedDevice.IsValid)
                Assert.Inconclusive("Lasp did not find a usable audio input device.");

            var runtimeObject = new GameObject("A20 live audio input probe");
            runtimeObject.SetActive(false);
            var host = runtimeObject.AddComponent<RouteHost>();
            var runtime = runtimeObject.AddComponent<MondmadeVJ0Runtime>();
            var route = new FloatRoute();
            var source = new LaspLevelFloatSource();
            SetField(source, "deviceId", selectedDevice.ID);
            route.Configure("A20 live audio level", source);
            host.AddRoute(route);
            SetField(runtime, "routeHosts", new[] { host });

            var deviceList = string.Join(", ", inputDevices.Select(device => $"{device.Name} ({device.ChannelCount}ch)"));
            Debug.Log($"A20 audio probe armed: selected input '{selectedDevice.Name}' ({selectedDevice.ChannelCount} channels); available inputs: {deviceList}. Live level capture starts in {ArmDelaySeconds:0} seconds and lasts {CaptureSeconds:0} seconds. No audio is stored.");
            yield return new WaitForSecondsRealtime(ArmDelaySeconds);

            var normalizedLevels = new List<float>(600);
            Debug.Log($"A20 live input capture started on '{selectedDevice.Name}'. No waveform or audio file is being recorded.");
            runtimeObject.SetActive(true);

            var stopAt = Time.realtimeSinceStartupAsDouble + CaptureSeconds;
            while (Time.realtimeSinceStartupAsDouble < stopAt)
            {
                if (route.HasCurrentValue) normalizedLevels.Add(route.CurrentValue);
                yield return null;
            }

            runtimeObject.SetActive(false);
            UnityEngine.Object.Destroy(runtimeObject);
            yield return null;

            Assert.That(route.Status, Is.EqualTo("Unbound"), "Disabling the runtime must unsubscribe the live audio source.");
            Assert.That(normalizedLevels.Count, Is.GreaterThan(60), "The Runtime FrameClock should deliver live level values throughout the capture window.");
            var maximum = 0f;
            foreach (var level in normalizedLevels)
            {
                Assert.That(float.IsNaN(level) || float.IsInfinity(level), Is.False, "Audio level values must remain finite.");
                Assert.That(level, Is.InRange(0f, 1f), "Lasp Level values must be normalized to 0–1.");
                maximum = Mathf.Max(maximum, level);
            }
            Debug.Log($"A20 live input summary: {normalizedLevels.Count} normalized samples, peak level {maximum:0.000}. No audio was stored.");
            Assert.That(maximum, Is.GreaterThanOrEqualTo(MinimumAudibleNormalizedLevel),
                $"No audible signal reached the Level Source during the {CaptureSeconds:0}-second capture (peak normalized level {maximum:0.000}). Speak or clap briefly during the next probe.");
            Debug.Log($"A20 live input probe passed: {normalizedLevels.Count} normalized samples, peak level {maximum:0.000}. No audio was stored.");
        }

        private static void SetField(object target, string name, object value)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field == null) continue;
                field.SetValue(target, value);
                return;
            }
            throw new MissingFieldException(target.GetType().FullName, name);
        }
    }
}
