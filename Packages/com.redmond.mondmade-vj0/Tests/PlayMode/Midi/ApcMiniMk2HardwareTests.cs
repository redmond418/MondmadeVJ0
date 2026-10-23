using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RtMidi;
using UnityEngine;
using UnityEngine.TestTools;

namespace MondmadeVJ0.Tests
{
    public sealed class ApcMiniMk2HardwareTests
    {
        [Explicit("Requires a person to press and release APC mini mk2 Pad 1 while the test is waiting.")]
        [UnityTest]
        public IEnumerator ReceivesPhysicalPadOneThroughRtMidi()
        {
            var inputPort = FindPort(true);
            var outputPort = FindPort(false);
            if (inputPort < 0 || outputPort < 0)
                Assert.Inconclusive("APC mini mk2 RtMidi input and output ports were not both available.");

            var profile = MidiSurfaceProfile.CreateApcMiniMk2Profile();
            var surface = new MidiSurfaceInstance();
            surface.Configure(profile, "APC mini mk2", 0, MidiInputBackend.RtMidiRaw, inputPort, outputPort);
            var runtimeObject = new GameObject("APC mini mk2 input probe");
            runtimeObject.SetActive(false);
            var hub = runtimeObject.AddComponent<MidiHub>();
            var runtime = runtimeObject.AddComponent<MondmadeVJ0Runtime>();
            SetField(hub, "surfaces", new[] { surface });
            SetField(runtime, "modules", new MonoBehaviour[] { hub });

            var received = false;
            MidiRawMessage observed = default;
            void Capture(MidiRawMessage message)
            {
                if (message.AddressType != MidiAddressType.Note || message.Number != 56) return;
                if (!received) observed = message;
                received = true;
            }
            var pressed = false;
            var released = false;
            var velocity = 0f;
            void CaptureControl(MidiControlChange change)
            {
                if (change.Reference.SurfaceInstanceId != surface.InstanceId || change.Reference.ControlId != "pad_0_0") return;
                if (change.PressedDown)
                {
                    pressed = true;
                    velocity = change.State.Velocity;
                }
                if (change.Released) released = true;
            }

            hub.RawInputObserved += Capture;
            hub.ControlChanged += CaptureControl;
            runtimeObject.SetActive(true);
            yield return null;

            try
            {
                Assert.That(hub.GetSurfaceStatus(surface.InstanceId), Is.EqualTo("Connected"));
                Assert.That(profile.Find("pad_0_0").type, Is.EqualTo(MidiControlType.VelocityButton));
                Debug.Log("[APC_PROBE_READY] Press and release the upper-left APC mini mk2 Pad 1 once now.");
                var deadline = Time.realtimeSinceStartup + 60f;
                while ((!received || !pressed || !released) && Time.realtimeSinceStartup < deadline)
                    yield return null;

                Assert.That(received && pressed && released, Is.True, "Press and release the APC mini mk2 upper-left Pad 1 while this test is waiting.");
                Assert.That(observed.SurfaceInstanceId, Is.EqualTo(surface.InstanceId));
                Assert.That(observed.AddressType, Is.EqualTo(MidiAddressType.Note));
                Assert.That(observed.Number, Is.EqualTo(56), "The upper-left Pad 1 sends note 56.");
                Assert.That(velocity, Is.InRange(1f / 127f, 1f), "The pressed pad must publish its normalized velocity.");
            }
            finally
            {
                hub.RawInputObserved -= Capture;
                hub.ControlChanged -= CaptureControl;
                Object.Destroy(runtimeObject);
                Object.Destroy(profile);
            }
            yield return null;
        }

        [Explicit("Requires a person to move APC mini mk2 Fader 1, then press and release Track Button 1.")]
        [Timeout(150000)]
        [UnityTest]
        public IEnumerator ReceivesPhysicalFaderAndTrackButtonWithTheirProfileTypes()
        {
            var inputPort = FindPort(true);
            var outputPort = FindPort(false);
            if (inputPort < 0 || outputPort < 0)
                Assert.Inconclusive("APC mini mk2 RtMidi input and output ports were not both available.");

            var profile = MidiSurfaceProfile.CreateApcMiniMk2Profile();
            Assert.That(profile.Find("fader_1").type, Is.EqualTo(MidiControlType.AbsoluteFader));
            Assert.That(profile.Find("track_1").type, Is.EqualTo(MidiControlType.Button));
            var surface = new MidiSurfaceInstance();
            surface.Configure(profile, "APC mini mk2", 0, MidiInputBackend.RtMidiRaw, inputPort, outputPort);
            var runtimeObject = new GameObject("APC mini mk2 fader and button probe");
            runtimeObject.SetActive(false);
            var hub = runtimeObject.AddComponent<MidiHub>();
            var runtime = runtimeObject.AddComponent<MondmadeVJ0Runtime>();
            SetField(hub, "surfaces", new[] { surface });
            SetField(runtime, "modules", new MonoBehaviour[] { hub });

            var rawMessages = new List<string>();
            var faderReceived = false;
            var faderValue = 0f;
            var buttonPressed = false;
            var buttonReleased = false;
            System.Action<MidiRawMessage> captureRaw = message =>
            {
                rawMessages.Add($"{message.AddressType} ch{message.Channel} #{message.Number}={message.Value:0.###}");
            };
            System.Action<MidiControlChange> capture = change =>
            {
                if (change.Reference.SurfaceInstanceId != surface.InstanceId) return;
                if (change.Reference.ControlId == "fader_1")
                {
                    faderReceived = true;
                    faderValue = change.State.Value;
                }
                else if (change.Reference.ControlId == "track_1")
                {
                    buttonPressed |= change.PressedDown;
                    buttonReleased |= change.Released;
                }
            };
            hub.RawInputObserved += captureRaw;
            hub.ControlChanged += capture;
            runtimeObject.SetActive(true);
            yield return null;

            try
            {
                Assert.That(hub.GetSurfaceStatus(surface.InstanceId), Is.EqualTo("Connected"));
                Debug.Log("[APC_CONTROL_PROBE_READY] During the next 90 seconds, move Fader 1 noticeably and press/release Track Button 1 once. Order does not matter.");
                var deadline = Time.realtimeSinceStartup + 90f;
                while ((!faderReceived || !buttonPressed || !buttonReleased) && Time.realtimeSinceStartup < deadline) yield return null;

                var missing = new List<string>();
                if (!faderReceived) missing.Add("Fader 1 CC 48 typed update");
                if (!buttonPressed) missing.Add("Track Button 1 press");
                if (!buttonReleased) missing.Add("Track Button 1 release");
                var rawSummary = rawMessages.Count == 0 ? "none" : string.Join(", ", rawMessages);
                Assert.That(missing, Is.Empty, $"Missing: {string.Join(", ", missing)}. Raw MIDI observed: {rawSummary}.");
                Assert.That(faderValue, Is.InRange(0f, 1f));
            }
            finally
            {
                hub.RawInputObserved -= captureRaw;
                hub.ControlChanged -= capture;
                Object.Destroy(runtimeObject);
                Object.Destroy(profile);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator OpensBothPortsAndSendsPadFeedback()
        {
            var inputPort = FindPort(true);
            var outputPort = FindPort(false);
            if (inputPort < 0 || outputPort < 0)
                Assert.Inconclusive("APC mini mk2 RtMidi input and output ports were not both available.");

            var profile = MidiSurfaceProfile.CreateApcMiniMk2Profile();
            var surface = new MidiSurfaceInstance();
            surface.Configure(profile, "APC mini mk2", 0, MidiInputBackend.RtMidiRaw, inputPort, outputPort);
            var reference = new MidiControlReference(surface.InstanceId, "pad_0_0");
            var runtimeObject = new GameObject("APC mini mk2 hardware test");
            runtimeObject.SetActive(false);
            var hub = runtimeObject.AddComponent<MidiHub>();
            var runtime = runtimeObject.AddComponent<MondmadeVJ0Runtime>();
            SetField(hub, "surfaces", new[] { surface });
            SetField(runtime, "modules", new MonoBehaviour[] { hub });
            runtimeObject.SetActive(true);
            yield return null;

            try
            {
                Assert.That(hub.GetSurfaceStatus(surface.InstanceId), Is.EqualTo("Connected"));
                var output = GetRawOutput(hub, surface.InstanceId);
                Assert.That(output, Is.Not.Null, "RtMidi output must be open in addition to the input status.");

                var owner = hub.CreateLayerOwner(null);
                hub.SetLed(reference, owner, MidiLedLayer.State, new MidiLedStyle { color = Color.green, brightness = 1f, effect = MidiLedEffect.Solid });
                hub.FlushFrame(runtime.Services);
                var sendDeadline = Time.realtimeSinceStartup + 5f;
                while (!IsOutputIdle(output) && Time.realtimeSinceStartup < sendDeadline) yield return null;
                Assert.That(IsOutputIdle(output), Is.True, "Queued LED messages must be drained by the shared output worker.");

                var sent = GetSentPayloads(hub);
                Assert.That(sent.Count, Is.EqualTo(80), "APC mini mk2 has 80 LED-capable controls.");
                var payload = sent[surface.InstanceId + "\0pad_0_0"];
                Assert.That((byte)payload.GetType().GetField("Status").GetValue(payload), Is.EqualTo(0x96));
                Assert.That((byte)payload.GetType().GetField("Number").GetValue(payload), Is.EqualTo(56));
                Assert.That((byte)payload.GetType().GetField("Value").GetValue(payload), Is.EqualTo(21));

                hub.SetLed(reference, owner, MidiLedLayer.State, MidiLedStyle.Off);
                hub.FlushFrame(runtime.Services);
                sendDeadline = Time.realtimeSinceStartup + 5f;
                while (!IsOutputIdle(output) && Time.realtimeSinceStartup < sendDeadline) yield return null;
                Assert.That(IsOutputIdle(output), Is.True);
                sent = GetSentPayloads(hub);
                payload = sent[surface.InstanceId + "\0pad_0_0"];
                Assert.That((byte)payload.GetType().GetField("Value").GetValue(payload), Is.EqualTo(0));
            }
            finally
            {
                Object.Destroy(runtimeObject);
                Object.Destroy(profile);
            }
            yield return null;
        }

        [Explicit("Requires the user to confirm APC mini mk2 Pad 1 lights green.")]
        [UnityTest]
        public IEnumerator PadOneLedCanBeSeenOnTheController()
        {
            var inputPort = FindPort(true);
            var outputPort = FindPort(false);
            if (inputPort < 0 || outputPort < 0)
                Assert.Inconclusive("APC mini mk2 RtMidi input and output ports were not both available.");

            var profile = MidiSurfaceProfile.CreateApcMiniMk2Profile();
            var surface = new MidiSurfaceInstance();
            surface.Configure(profile, "APC mini mk2", 0, MidiInputBackend.RtMidiRaw, inputPort, outputPort);
            var reference = new MidiControlReference(surface.InstanceId, "pad_0_0");
            var runtimeObject = new GameObject("APC mini mk2 LED visual probe");
            runtimeObject.SetActive(false);
            var hub = runtimeObject.AddComponent<MidiHub>();
            var runtime = runtimeObject.AddComponent<MondmadeVJ0Runtime>();
            SetField(hub, "surfaces", new[] { surface });
            SetField(runtime, "modules", new MonoBehaviour[] { hub });
            runtimeObject.SetActive(true);
            yield return null;

            var owner = hub.CreateLayerOwner(null);
            try
            {
                Assert.That(hub.GetSurfaceStatus(surface.InstanceId), Is.EqualTo("Connected"));
                hub.SetLed(reference, owner, MidiLedLayer.State, new MidiLedStyle { color = Color.green, brightness = 1f, effect = MidiLedEffect.Solid });
                hub.FlushFrame(runtime.Services);
                var output = GetRawOutput(hub, surface.InstanceId);
                var sendDeadline = Time.realtimeSinceStartup + 5f;
                while (!IsOutputIdle(output) && Time.realtimeSinceStartup < sendDeadline) yield return null;
                Assert.That(IsOutputIdle(output), Is.True, "Pad 1 must receive the queued green LED feedback before the visual prompt.");
                Debug.Log("[APC_LED_READY] The upper-left Pad 1 should now be solid green for 60 seconds.");
                yield return new WaitForSecondsRealtime(60f);
            }
            finally
            {
                hub.SetLed(reference, owner, MidiLedLayer.State, MidiLedStyle.Off);
                hub.FlushFrame(runtime.Services);
                Object.Destroy(runtimeObject);
                Object.Destroy(profile);
            }
            yield return null;
        }

        [Explicit("Requires the connected APC mini mk2 MIDI output to verify one shared Clock and LED sender.")]
        [UnityTest]
        public IEnumerator MidiClockAndLedFeedbackShareOneSerializedOutputWorker()
        {
            var inputPort = FindPort(true);
            var outputPort = FindPort(false);
            if (inputPort < 0 || outputPort < 0)
                Assert.Inconclusive("APC mini mk2 RtMidi input and output ports were not both available.");

            var profile = MidiSurfaceProfile.CreateApcMiniMk2Profile();
            var surface = new MidiSurfaceInstance();
            surface.Configure(profile, "APC mini mk2", 0, MidiInputBackend.RtMidiRaw, inputPort, outputPort);
            var runtimeObject = new GameObject("APC mini mk2 shared clock and LED output probe");
            runtimeObject.SetActive(false);
            var hub = runtimeObject.AddComponent<MidiHub>();
            var transport = runtimeObject.AddComponent<MusicalTransport>();
            var clock = runtimeObject.AddComponent<MidiClockSender>();
            var runtime = runtimeObject.AddComponent<MondmadeVJ0Runtime>();
            SetField(hub, "surfaces", new[] { surface });
            SetField(clock, "outputPortOrdinal", outputPort);
            SetField(runtime, "modules", new MonoBehaviour[] { transport, hub, clock });
            runtimeObject.SetActive(true);

            try
            {
                var deadline = Time.realtimeSinceStartup + 10f;
                object hubLease = null;
                object clockLease = null;
                while (Time.realtimeSinceStartup < deadline)
                {
                    hubLease = GetRawOutput(hub, surface.InstanceId);
                    clockLease = typeof(MidiClockSender).GetField("output", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(clock);
                    if (IsOutputOk(hubLease) && IsOutputOk(clockLease)) break;
                    yield return null;
                }

                Assert.That(IsOutputOk(hubLease), Is.True, "The shared APC MIDI output must connect.");
                Assert.That(IsOutputOk(clockLease), Is.True, "The Clock sender must acquire the same output lease.");
                Assert.That(GetLeaseEndpoint(hubLease), Is.SameAs(GetLeaseEndpoint(clockLease)), "Clock and LED sends must share exactly one port endpoint and worker.");

                var owner = hub.CreateLayerOwner(null);
                hub.SetLed(new MidiControlReference(surface.InstanceId, "pad_0_0"), owner, MidiLedLayer.State,
                    new MidiLedStyle { color = Color.green, brightness = 1f, effect = MidiLedEffect.Solid });
                hub.FlushFrame(runtime.Services);
                deadline = Time.realtimeSinceStartup + 5f;
                while ((clock.SentPulses < 24 || !IsOutputIdle(hubLease)) && Time.realtimeSinceStartup < deadline) yield return null;

                Assert.That(clock.Status, Is.EqualTo("Connected"));
                Assert.That(clock.SentPulses, Is.GreaterThanOrEqualTo(24), "At least one quarter-note of MIDI Clock must reach the shared worker.");
                Assert.That(clock.MissedPulses, Is.EqualTo(0));
                Assert.That(IsOutputIdle(hubLease), Is.True, "The LED queue must drain while Clock remains active.");
            }
            finally
            {
                Object.Destroy(runtimeObject);
                Object.Destroy(profile);
            }
            yield return null;
        }

        [Explicit("Requires a person to press Pad 1 and unplug/reconnect the APC mini mk2 when prompted.")]
        [UnityTest]
        public IEnumerator ReconnectPreservesInputStateAndResyncsDesiredLedWhilePanicIsLatched()
        {
            var inputPort = FindPort(true);
            var outputPort = FindPort(false);
            if (inputPort < 0 || outputPort < 0)
                Assert.Inconclusive("APC mini mk2 RtMidi input and output ports were not both available.");

            var profile = MidiSurfaceProfile.CreateApcMiniMk2Profile();
            var surface = new MidiSurfaceInstance();
            surface.Configure(profile, "APC mini mk2", 0, MidiInputBackend.RtMidiRaw, inputPort, outputPort);
            var padReference = new MidiControlReference(surface.InstanceId, "pad_0_0");
            var faderReference = new MidiControlReference(surface.InstanceId, "fader_1");
            var trackReference = new MidiControlReference(surface.InstanceId, "track_1");
            var runtimeObject = new GameObject("APC mini mk2 reconnect probe");
            runtimeObject.SetActive(false);
            var hub = runtimeObject.AddComponent<MidiHub>();
            var runtime = runtimeObject.AddComponent<MondmadeVJ0Runtime>();
            var routeHost = runtimeObject.AddComponent<RouteHost>();
            var toggleTrigger = new MidiButtonTriggerSource();
            SetField(toggleTrigger, "control", trackReference);
            var toggle = new ToggleBoolSource();
            SetField(toggle, "trigger", toggleTrigger);
            var toggleRoute = new BoolRoute();
            toggleRoute.Configure("A17 Toggle state", toggle);
            var holdSource = new MidiButtonBoolSource();
            SetField(holdSource, "control", trackReference);
            var holdRoute = new BoolRoute();
            holdRoute.Configure("A17 Hold state", holdSource);
            var faderSource = new MidiAbsoluteFloatSource();
            SetField(faderSource, "control", faderReference);
            var faderRoute = new FloatRoute();
            faderRoute.Configure("A17 Fader state", faderSource);
            SetField(routeHost, "routes", new List<IRoute> { toggleRoute, holdRoute, faderRoute });
            SetField(hub, "surfaces", new[] { surface });
            SetField(runtime, "modules", new MonoBehaviour[] { hub });
            SetField(runtime, "routeHosts", new[] { routeHost });
            var padPressed = false;
            var padReleased = false;
            System.Action<MidiControlChange> capture = change =>
            {
                if (change.Reference.SurfaceInstanceId != surface.InstanceId || change.Reference.ControlId != "pad_0_0") return;
                if (change.PressedDown) padPressed = true;
                if (change.Released) padReleased = true;
            };
            hub.ControlChanged += capture;
            runtimeObject.SetActive(true);

            var owner = hub.CreateLayerOwner(null);
            try
            {
                yield return null;
                Assert.That(hub.GetSurfaceStatus(surface.InstanceId), Is.EqualTo("Connected"));
                Debug.Log("[APC_RECONNECT_PAD_READY] Tap and release upper-left Pad 1 once to confirm physical input.");
                var waitUntil = Time.realtimeSinceStartup + 120f;
                while ((!padPressed || !padReleased) && Time.realtimeSinceStartup < waitUntil) yield return null;
                Assert.That(padPressed && padReleased, Is.True, "Pad 1 press and release must be received before the disconnect probe.");

                InjectControl(hub, surface.InstanceId, MidiAddressType.Note, 0, 56, 100f / 127f);
                InjectControl(hub, surface.InstanceId, MidiAddressType.ControlChange, 0, 0x30, 0.7f);
                InjectControl(hub, surface.InstanceId, MidiAddressType.Note, 0, 0x64, 100f / 127f);
                var retainedPad = hub.GetState(padReference);
                var retainedFader = hub.GetState(faderReference);
                Assert.That(retainedPad.Pressed, Is.True);
                Assert.That(retainedFader.Value, Is.EqualTo(0.7f).Within(0.0001f));
                Assert.That(toggleRoute.CurrentValue, Is.True, "A17 must hold Toggle=true after a real MIDI button event.");
                Assert.That(holdRoute.CurrentValue, Is.True, "A17 must hold the pressed Button value until a real Release arrives.");
                Assert.That(faderRoute.CurrentValue, Is.EqualTo(0.7f).Within(0.0001f));

                hub.SetLed(padReference, owner, MidiLedLayer.State, new MidiLedStyle { color = Color.green, brightness = 1f, effect = MidiLedEffect.Solid });
                hub.FlushFrame(runtime.Services);
                var outputBeforeDisconnect = GetRawOutput(hub, surface.InstanceId);
                Assert.That(outputBeforeDisconnect, Is.Not.Null);
                var generationBeforeDisconnect = GetConnectionGeneration(outputBeforeDisconnect);
                Assert.That(GetPayloadValue(GetSentPayloads(hub), surface.InstanceId + "\0pad_0_0"), Is.EqualTo(21));
                var ledDeadline = Time.realtimeSinceStartup + 5f;
                while (!IsOutputIdle(outputBeforeDisconnect) && Time.realtimeSinceStartup < ledDeadline) yield return null;
                Assert.That(IsOutputIdle(outputBeforeDisconnect), Is.True, "Latest desired LED feedback must reach the device before unplugging.");

                Debug.Log("[APC_RECONNECT_UNPLUG_READY] Unplug the APC mini mk2 USB cable now; the probe waits up to 120 seconds.");
                waitUntil = Time.realtimeSinceStartup + 120f;
                var disconnected = false;
                while (Time.realtimeSinceStartup < waitUntil)
                {
                    yield return new WaitForSecondsRealtime(0.5f);
                    if (FindPort(true) < 0 && FindPort(false) < 0) { disconnected = true; break; }
                }
                Assert.That(disconnected, Is.True, "Both APC mini mk2 MIDI ports must disappear after unplugging.");
                Assert.That(hub.GetState(padReference).Pressed, Is.EqualTo(retainedPad.Pressed), "The last Pad state must survive USB disconnection.");
                Assert.That(hub.GetState(faderReference).Value, Is.EqualTo(retainedFader.Value).Within(0.0001f), "The last Fader value must survive USB disconnection.");
                Assert.That(toggleRoute.CurrentValue, Is.True, "Toggle=true must survive USB disconnection.");
                Assert.That(holdRoute.CurrentValue, Is.True, "Hold=true must survive USB disconnection without an invented Release.");
                Assert.That(faderRoute.CurrentValue, Is.EqualTo(0.7f).Within(0.0001f));

                hub.PanicAllOff();
                hub.FlushFrame(runtime.Services);
                Debug.Log("[APC_RECONNECT_PLUG_READY] Panic is latched while disconnected. Reconnect the APC mini mk2 USB cable now.");
                waitUntil = Time.realtimeSinceStartup + 120f;
                var reconnectedAndSynced = false;
                while (Time.realtimeSinceStartup < waitUntil)
                {
                    yield return new WaitForSecondsRealtime(0.5f);
                    var currentOutput = GetRawOutput(hub, surface.InstanceId);
                    if (FindPort(true) >= 0 && FindPort(false) >= 0 && currentOutput != null && GetConnectionGeneration(currentOutput) > generationBeforeDisconnect && IsOutputOk(currentOutput) && IsOutputIdle(currentOutput) &&
                        GetSentPayloads(hub).Count == 80 && GetPayloadValue(GetSentPayloads(hub), surface.InstanceId + "\0pad_0_0") == 0)
                    {
                        reconnectedAndSynced = true;
                        break;
                    }
                }

                var diagnosticOutput = GetRawOutput(hub, surface.InstanceId);
                var diagnosticSent = GetSentPayloads(hub);
                var padPayloadKey = surface.InstanceId + "\0pad_0_0";
                var padPayloadValue = diagnosticSent.Contains(padPayloadKey) ? GetPayloadValue(diagnosticSent, padPayloadKey).ToString() : "missing";
                Assert.That(reconnectedAndSynced, Is.True,
                    $"Reconnect must reopen the Output port and resync all LEDs to Panic OFF. inputPorts=[{DescribePorts(true)}], outputPorts=[{DescribePorts(false)}], " +
                    $"outputStatus={GetOutputStatus(diagnosticOutput)}, generation={GetConnectionGeneration(diagnosticOutput)}, generationBeforeDisconnect={generationBeforeDisconnect}, " +
                    $"outputOk={IsOutputOk(diagnosticOutput)}, outputIdle={IsOutputIdle(diagnosticOutput)}, sentPayloadCount={diagnosticSent.Count}, pad0PayloadValue={padPayloadValue}.");
                Assert.That(hub.PanicLatched, Is.True);
                Assert.That(hub.GetState(padReference).Pressed, Is.EqualTo(retainedPad.Pressed));
                Assert.That(hub.GetState(faderReference).Value, Is.EqualTo(retainedFader.Value).Within(0.0001f));
                Assert.That(toggleRoute.CurrentValue, Is.True);
                Assert.That(holdRoute.CurrentValue, Is.True);
                Assert.That(faderRoute.CurrentValue, Is.EqualTo(0.7f).Within(0.0001f));

                hub.ResumeOutputs();
                hub.FlushFrame(runtime.Services);
                var resumeDeadline = Time.realtimeSinceStartup + 5f;
                while (!IsOutputIdle(outputBeforeDisconnect) && Time.realtimeSinceStartup < resumeDeadline) yield return null;
                Assert.That(IsOutputIdle(outputBeforeDisconnect), Is.True, "Resume must drain the full desired LED resync.");
                Assert.That(hub.PanicLatched, Is.False);
                Assert.That(GetPayloadValue(GetSentPayloads(hub), surface.InstanceId + "\0pad_0_0"), Is.EqualTo(21), "Resuming output must restore the latest desired LED state.");
            }
            finally
            {
                hub.ControlChanged -= capture;
                hub.RemoveLayerOwner(owner);
                Object.Destroy(runtimeObject);
                Object.Destroy(profile);
            }
            yield return null;
        }

        private static int FindPort(bool input)
        {
            try
            {
                if (input)
                {
                    using (var device = MidiIn.Create())
                        for (var index = 0; index < device.PortCount; index++)
                            if (device.GetPortName(index).IndexOf("APC mini mk2", System.StringComparison.OrdinalIgnoreCase) >= 0) return index;
                }
                else
                {
                    using (var device = MidiOut.Create())
                        for (var index = 0; index < device.PortCount; index++)
                            if (device.GetPortName(index).IndexOf("APC mini mk2", System.StringComparison.OrdinalIgnoreCase) >= 0) return index;
                }
            }
            catch { }
            return -1;
        }

        private static string DescribePorts(bool input)
        {
            var names = new List<string>();
            try
            {
                if (input)
                {
                    using (var device = MidiIn.Create())
                        for (var index = 0; index < device.PortCount; index++) names.Add(index + ":" + device.GetPortName(index));
                }
                else
                {
                    using (var device = MidiOut.Create())
                        for (var index = 0; index < device.PortCount; index++) names.Add(index + ":" + device.GetPortName(index));
                }
            }
            catch (System.Exception exception) { return "<error: " + exception.Message + ">"; }
            return string.Join(" | ", names);
        }

        private static object GetRawOutput(MidiHub hub, string surfaceId)
        {
            var surfaces = (IDictionary)typeof(MidiHub).GetField("surfaceRuntime", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hub);
            var surface = surfaces[surfaceId];
            return surface.GetType().GetField("Output", BindingFlags.Instance | BindingFlags.Public).GetValue(surface);
        }

        private static IDictionary GetSentPayloads(MidiHub hub)
            => (IDictionary)typeof(MidiHub).GetField("sentPayloads", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hub);

        private static byte GetPayloadValue(IDictionary sent, string key)
            => (byte)sent[key].GetType().GetField("Value").GetValue(sent[key]);

        private static bool IsOutputOk(object output)
            => output != null && (bool)output.GetType().GetProperty("IsOk").GetValue(output);

        private static bool IsOutputIdle(object output)
            => output != null && (bool)output.GetType().GetProperty("IsIdle").GetValue(output);

        private static string GetOutputStatus(object output)
            => output == null ? "null" : (string)output.GetType().GetProperty("Status").GetValue(output);

        private static object GetLeaseEndpoint(object lease)
            => lease.GetType().GetField("port", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(lease);

        private static long GetConnectionGeneration(object output)
            => (long)output.GetType().GetProperty("ConnectionGeneration").GetValue(output);

        private static void InjectControl(MidiHub hub, string surfaceId, MidiAddressType type, int channel, int number, float value)
        {
            var packetType = typeof(MidiHub).GetNestedType("InputPacket", BindingFlags.NonPublic);
            var constructor = packetType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0];
            var packet = constructor.Invoke(new object[] { surfaceId, type, channel, number, value, 0, 0L, null, 0, 0d });
            typeof(MidiHub).GetMethod("ProcessControl", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hub, new[] { packet });
        }

        private static void SetField(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
