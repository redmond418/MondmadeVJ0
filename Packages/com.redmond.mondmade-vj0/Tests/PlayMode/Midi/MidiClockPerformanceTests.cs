using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Linq;
using NUnit.Framework;
using RtMidi;
using UnityEngine;
using UnityEngine.TestTools;

namespace MondmadeVJ0.Tests
{
    public sealed class MidiClockPerformanceTests
    {
        private const int RouteCount = 1000;
        private const int LedUpdateRate = 10;
        private const double TargetFps = 60d;
        private const double DefaultPhaseDurationSeconds = 600d;
        private const string DurationOverrideEnvironmentVariable = "MONDMADE_VJ0_CLOCK_TEST_SECONDS";
        private const string DisableLedStressEnvironmentVariable = "MONDMADE_VJ0_CLOCK_TEST_DISABLE_LEDS";

        [Explicit("Runs 10-minute BPM 120 / 174 / 240 phases with APC mini mk2, 1000 active routes, and 10 Hz full LED updates.")]
        [Timeout(2000000)]
        [UnityTest]
        public IEnumerator A14MidiClockMeetsTimingTargetsUnderDocumentedLoad()
        {
            var inputPort = FindPort(input: true);
            var outputPort = FindPort(input: false);
            if (inputPort < 0 || outputPort < 0)
                Assert.Inconclusive("APC mini mk2 RtMidi input and output ports are required for the A14 load run.");

            var durationSeconds = GetPhaseDuration();
            var fullAcceptanceRun = durationSeconds >= DefaultPhaseDurationSeconds;
            var ledStressEnabled = !string.Equals(Environment.GetEnvironmentVariable(DisableLedStressEnvironmentVariable), "1", StringComparison.Ordinal);
            if (fullAcceptanceRun && !ledStressEnabled)
                Assert.Fail("A14 full acceptance requires all APC mini mk2 LEDs to be updated at 10 Hz.");
            var originalTargetFrameRate = Application.targetFrameRate;
            var originalVSync = QualitySettings.vSyncCount;
            var originalRunInBackground = Application.runInBackground;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Application.runInBackground = true;
            var failures = new List<string>();

            try
            {
                foreach (var bpm in new[] { 120d, 174d, 240d })
                {
                    WriteCounterFloatOutput.Reset();
                    var world = CreateWorld(inputPort, outputPort, bpm);
                    try
                    {
                        yield return null;
                        var hubLease = GetHubOutput(world.Hub, world.Surface.InstanceId);
                        var clockLease = typeof(MidiClockSender).GetField("output", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(world.Clock);
                        var startupDeadline = Time.realtimeSinceStartupAsDouble + 15d;
                        while ((!IsOutputOk(hubLease) || !IsOutputOk(clockLease) || !IsOutputIdle(hubLease)) && Time.realtimeSinceStartupAsDouble < startupDeadline)
                            yield return null;

                        if (!IsOutputOk(hubLease) || !IsOutputOk(clockLease) || !IsOutputIdle(hubLease))
                        {
                            failures.Add($"BPM {bpm}: the shared APC output worker did not become connected and idle.");
                            yield return null;
                            continue;
                        }
                        var sentLedMessagesAtStart = GetSentLedMessages(hubLease);
                        var sentLedControlsAtStart = GetSentLedControls(hubLease);
                        RecordFailure(ReferenceEquals(GetLeaseEndpoint(hubLease), GetLeaseEndpoint(clockLease)),
                            "LED and Clock must share the same output worker.", failures);
                        RecordFailure(world.Host.Routes.Count == RouteCount,
                            $"BPM {bpm}: expected {RouteCount} routes, found {world.Host.Routes.Count}.", failures);
                        for (var i = 0; i < world.Host.Routes.Count; i++)
                            RecordFailure(world.Host.Routes[i].Status == "Active",
                                $"BPM {bpm}: Route {i} was {world.Host.Routes[i].Status}, not Active.", failures);

                        world.Transport.SetRunning(true);
                        var phaseStart = Time.realtimeSinceStartupAsDouble;
                        var phaseEnd = phaseStart + durationSeconds;
                        var nextFrameDeadline = phaseStart;
                        var ledDeadline = phaseStart;
                        var interval = new WaitForSecondsRealtime((float)(1d / TargetFps));
                        var frames = 0;
                        var ledUpdates = 0;
                        var maximumFrameInterval = 0d;
                        var previousFrame = phaseStart;
                        var routeWritesAtStart = WriteCounterFloatOutput.Writes;

                        while (Time.realtimeSinceStartupAsDouble < phaseEnd)
                        {
                            var now = Time.realtimeSinceStartupAsDouble;
                            var frameInterval = Math.Max(0d, now - previousFrame);
                            if (frames > 0) maximumFrameInterval = Math.Max(maximumFrameInterval, frameInterval);
                            previousFrame = now;
                            frames++;
                            world.Signal.SetValue((float)frames);

                            if (ledStressEnabled && now >= ledDeadline)
                            {
                                ledUpdates++;
                                var isGreenFrame = ledUpdates % 2 == 0;
                                var color = isGreenFrame ? Color.green : Color.red;
                                var padStyle = new MidiLedStyle { color = color, brightness = 1f, effect = MidiLedEffect.Solid };
                                var buttonStyle = isGreenFrame ? padStyle : MidiLedStyle.Off;
                                for (var i = 0; i < world.LedControls.Length; i++)
                                    world.Hub.SetLed(world.LedControls[i], world.LedOwner, MidiLedLayer.Beat,
                                        i < 64 ? padStyle : buttonStyle);
                                var ledInterval = 1d / LedUpdateRate;
                                var elapsedLedIntervals = Math.Floor((now - ledDeadline) / ledInterval) + 1d;
                                ledDeadline += elapsedLedIntervals * ledInterval;
                            }

                            nextFrameDeadline += 1d / TargetFps;
                            var remaining = nextFrameDeadline - Time.realtimeSinceStartupAsDouble;
                            if (remaining > 0d)
                            {
                                interval.waitTime = (float)remaining;
                                yield return interval;
                            }
                            else yield return null;
                        }

                        var measuredElapsed = Time.realtimeSinceStartupAsDouble - phaseStart;
                        var minimumLedUpdates = ledStressEnabled ? (int)Math.Floor(measuredElapsed * LedUpdateRate * 0.98d) : 0;
                        var routeWrites = WriteCounterFloatOutput.Writes - routeWritesAtStart;
                        var routeWriteRate = routeWrites / measuredElapsed;
                        var frameRate = frames / measuredElapsed;

                        world.Transport.SetRunning(false);
                        world.Clock.FlushFrame(world.Runtime.Services);
                        var drainDeadline = Time.realtimeSinceStartupAsDouble + 2d;
                        while ((!IsOutputIdle(hubLease) || world.Clock.Status != "Stopped") && Time.realtimeSinceStartupAsDouble < drainDeadline)
                            yield return null;

                        var sentPulses = world.Clock.SentPulses;
                        var missedPulses = world.Clock.MissedPulses;
                        var seekResyncs = world.Clock.SeekResyncCount;
                        var droppedOutputMessages = world.Clock.DroppedOutputMessages;
                        var sentLedMessages = GetSentLedMessages(hubLease) - sentLedMessagesAtStart;
                        var sentLedControls = GetSentLedControls(hubLease) - sentLedControlsAtStart;
                        var maximumLedSendDuration = GetMaximumLedSendDuration(hubLease);
                        var p99 = world.Clock.P99LatenessMilliseconds;
                        var maxLateness = world.Clock.MaximumLatenessMilliseconds;
                        var cumulativeDrift = world.Clock.MaximumDeadlineDriftMilliseconds;
                        var expectedPulses = (long)Math.Floor(bpm * 24d / 60d * measuredElapsed);
                        var minimumLedControls = ledStressEnabled ? (long)Math.Floor(ledUpdates * world.LedControls.Length * 0.98d) : 0L;

                        var mode = fullAcceptanceRun ? "full" : ledStressEnabled ? "smoke" : "clock-only-smoke";
                        var result = $"A14_RESULT,mode={mode},bpm={bpm:F0},duration_s={measuredElapsed:F3},fps={frameRate:F3},max_frame_ms={maximumFrameInterval * 1000d:F3},routes={world.Host.Routes.Count},route_writes_per_s={routeWriteRate:F0},led_hz={ledUpdates / measuredElapsed:F3},led_messages={sentLedMessages},led_controls={sentLedControls},led_controls_per_s={sentLedControls / measuredElapsed:F0},led_send_max_ms={maximumLedSendDuration:F4},clock_backend={world.Clock.OutputBackend},clock_schedule_lead_ms={world.Clock.OutputScheduleLeadMilliseconds:F3},clock_metric_basis={world.Clock.OutputTimingMetricBasis},clock_backend_diagnostic={world.Clock.OutputBackendDiagnostic},clock_sent={sentPulses},clock_expected≈{expectedPulses},clock_missed={missedPulses},clock_seek_resyncs={seekResyncs},output_dropped={droppedOutputMessages},p99_ms={p99:F4},max_lateness_ms={maxLateness:F4},cumulative_drift_ms={cumulativeDrift:F4},platform={Application.platform},os={SystemInfo.operatingSystem},cpu={SystemInfo.processorType},unity={Application.unityVersion},device=APC mini mk2";
                        Debug.Log(result);
                        TestContext.WriteLine(result);

                        RecordFailure(frameRate >= 59d, $"BPM {bpm}: measured route cadence was {frameRate:F3} fps (minimum 59).", failures);
                        RecordFailure(routeWriteRate >= RouteCount * 59d, $"BPM {bpm}: route write rate was {routeWriteRate:F0}/s (minimum {RouteCount * 59}).", failures);
                        RecordFailure(ledUpdates >= minimumLedUpdates, $"BPM {bpm}: full LED updates were {ledUpdates} (minimum {minimumLedUpdates}).", failures);
                        RecordFailure(sentLedControls >= minimumLedControls,
                            $"BPM {bpm}: successfully transmitted LED control states were {sentLedControls} (minimum {minimumLedControls}).", failures);
                        RecordFailure(Math.Abs(sentPulses - expectedPulses) <= 10, $"BPM {bpm}: sent {sentPulses} Clock pulses, expected about {expectedPulses}.", failures);
                        RecordFailure(missedPulses == 0, $"BPM {bpm}: missed {missedPulses} Clock pulses.", failures);
                        RecordFailure(droppedOutputMessages == 0, $"BPM {bpm}: shared output dropped {droppedOutputMessages} messages.", failures);
                        RecordFailure(p99 <= 2d, $"BPM {bpm}: p99 Clock call lateness was {p99:F4} ms (maximum 2 ms).", failures);
                        RecordFailure(maxLateness <= 10d, $"BPM {bpm}: maximum Clock call lateness was {maxLateness:F4} ms (maximum 10 ms).", failures);
                        if (fullAcceptanceRun)
                            RecordFailure(cumulativeDrift <= 1d,
                                $"BPM {bpm}: cumulative Clock deadline drift was {cumulativeDrift:F4} ms (maximum 1 ms).", failures);
                    }
                    finally
                    {
                        world.Transport.SetRunning(false);
                        world.Runtime.enabled = false;
                        UnityEngine.Object.Destroy(world.GameObject);
                        UnityEngine.Object.Destroy(world.Signal.gameObject);
                        UnityEngine.Object.Destroy(world.Profile);
                    }
                    yield return null;
                }

                if (failures.Count > 0) Assert.Fail(string.Join(Environment.NewLine, failures));
            }
            finally
            {
                Application.targetFrameRate = originalTargetFrameRate;
                QualitySettings.vSyncCount = originalVSync;
                Application.runInBackground = originalRunInBackground;
            }
        }

        private static StressWorld CreateWorld(int inputPort, int outputPort, double bpm)
        {
            var signalObject = new GameObject($"A14 {bpm:F0} BPM shared value signal");
            var signal = signalObject.AddComponent<SharedFloatSignal>();
            var profile = MidiSurfaceProfile.CreateApcMiniMk2Profile();
            var surface = new MidiSurfaceInstance();
            surface.Configure(profile, "APC mini mk2", 0, MidiInputBackend.RtMidiRaw, inputPort, outputPort);

            var gameObject = new GameObject($"A14 {bpm:F0} BPM stress runtime");
            gameObject.SetActive(false);
            var host = gameObject.AddComponent<RouteHost>();
            var hub = gameObject.AddComponent<MidiHub>();
            var transport = gameObject.AddComponent<MusicalTransport>();
            var clock = gameObject.AddComponent<MidiClockSender>();
            var runtime = gameObject.AddComponent<MondmadeVJ0Runtime>();
            SetField(transport, "startsRunning", false);
            transport.SetBaseBpm(bpm);
            SetField(hub, "surfaces", new[] { surface });
            SetField(clock, "outputPortOrdinal", outputPort);
            SetField(runtime, "modules", new MonoBehaviour[] { transport, hub, clock });
            SetField(runtime, "routeHosts", new[] { host });

            for (var i = 0; i < RouteCount; i++)
            {
                var source = new SignalFloatSource();
                source.Signal = signal;
                var route = new FloatRoute();
                route.Configure($"A14 stress route {i + 1}", source);
                route.Outputs.Add(new WriteCounterFloatOutput());
                host.AddRoute(route);
            }

            gameObject.SetActive(true);
            return new StressWorld(gameObject, signal, profile, surface, hub, host, transport, clock, runtime);
        }

        private static int FindPort(bool input)
        {
            try
            {
                if (input)
                {
                    using (var device = MidiIn.Create())
                        for (var index = 0; index < device.PortCount; index++)
                            if (device.GetPortName(index).IndexOf("APC mini mk2", StringComparison.OrdinalIgnoreCase) >= 0) return index;
                }
                else
                {
                    using (var device = MidiOut.Create())
                        for (var index = 0; index < device.PortCount; index++)
                            if (device.GetPortName(index).IndexOf("APC mini mk2", StringComparison.OrdinalIgnoreCase) >= 0) return index;
                }
            }
            catch { }
            return -1;
        }

        private static double GetPhaseDuration()
        {
            var value = Environment.GetEnvironmentVariable(DurationOverrideEnvironmentVariable);
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) && duration > 0d
                ? Math.Min(DefaultPhaseDurationSeconds, duration)
                : DefaultPhaseDurationSeconds;
        }

        private static object GetHubOutput(MidiHub hub, string surfaceId)
        {
            var surfaces = (IDictionary)typeof(MidiHub).GetField("surfaceRuntime", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hub);
            var surface = surfaces[surfaceId];
            return surface.GetType().GetField("Output", BindingFlags.Instance | BindingFlags.Public).GetValue(surface);
        }

        private static bool IsOutputOk(object output)
            => output != null && (bool)output.GetType().GetProperty("IsOk").GetValue(output);

        private static bool IsOutputIdle(object output)
            => output != null && (bool)output.GetType().GetProperty("IsIdle").GetValue(output);

        private static object GetLeaseEndpoint(object lease)
            => lease.GetType().GetField("port", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(lease);

        private static long GetSentLedMessages(object lease)
            => (long)lease.GetType().GetProperty("SentLedMessages").GetValue(lease);

        private static long GetSentLedControls(object lease)
            => (long)lease.GetType().GetProperty("SentLedControls").GetValue(lease);

        private static double GetMaximumLedSendDuration(object lease)
            => (double)lease.GetType().GetProperty("MaximumLedSendDurationMilliseconds").GetValue(lease);

        private static void SetField(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static void RecordFailure(bool passed, string message, ICollection<string> failures)
        {
            if (!passed) failures.Add(message);
        }

        private sealed class StressWorld
        {
            public readonly GameObject GameObject;
            public readonly SharedFloatSignal Signal;
            public readonly MidiSurfaceProfile Profile;
            public readonly MidiSurfaceInstance Surface;
            public readonly MidiHub Hub;
            public readonly RouteHost Host;
            public readonly MusicalTransport Transport;
            public readonly MidiClockSender Clock;
            public readonly MondmadeVJ0Runtime Runtime;
            public readonly string LedOwner;
            public readonly MidiControlReference[] LedControls;

            public StressWorld(GameObject gameObject, SharedFloatSignal signal, MidiSurfaceProfile profile, MidiSurfaceInstance surface,
                MidiHub hub, RouteHost host, MusicalTransport transport, MidiClockSender clock, MondmadeVJ0Runtime runtime)
            {
                GameObject = gameObject;
                Signal = signal;
                Profile = profile;
                Surface = surface;
                Hub = hub;
                Host = host;
                Transport = transport;
                Clock = clock;
                Runtime = runtime;
                LedOwner = hub.CreateLayerOwner(null);
                LedControls = profile.Controls.Where(control => control != null && control.hasLed)
                    .Select(control => new MidiControlReference(surface.InstanceId, control.id)).ToArray();
            }
        }

        private sealed class WriteCounterFloatOutput : IValueOutput<float>
        {
            private static long writes;
            public static long Writes => Interlocked.Read(ref writes);
            public static void Reset() => Interlocked.Exchange(ref writes, 0);
            public void Initialize(RouteContext context) { }
            public void Write(float value) => Interlocked.Increment(ref writes);
        }
    }
}
