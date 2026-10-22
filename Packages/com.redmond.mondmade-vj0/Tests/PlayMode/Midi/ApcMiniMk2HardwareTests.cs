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
        [Explicit("Requires a person to press APC mini mk2 Pad 1 while the test is waiting.")]
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
                observed = message;
                received = true;
            }

            hub.RawInputObserved += Capture;
            runtimeObject.SetActive(true);
            yield return null;

            try
            {
                Assert.That(hub.GetSurfaceStatus(surface.InstanceId), Is.EqualTo("Connected"));
                Debug.Log("[APC_PROBE_READY] Press the upper-left APC mini mk2 Pad 1 once now.");
                var deadline = Time.realtimeSinceStartup + 60f;
                while (!received && Time.realtimeSinceStartup < deadline)
                    yield return null;

                Assert.That(received, Is.True, "Press the APC mini mk2 upper-left Pad 1 while this test is waiting.");
                Assert.That(observed.SurfaceInstanceId, Is.EqualTo(surface.InstanceId));
                Assert.That(observed.AddressType, Is.EqualTo(MidiAddressType.Note));
                Assert.That(observed.Number, Is.EqualTo(56), "The upper-left Pad 1 sends note 56.");
            }
            finally
            {
                hub.RawInputObserved -= Capture;
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

                var sent = GetSentPayloads(hub);
                Assert.That(sent.Count, Is.EqualTo(80), "APC mini mk2 has 80 LED-capable controls.");
                var payload = sent[surface.InstanceId + "\0pad_0_0"];
                Assert.That((byte)payload.GetType().GetField("Status").GetValue(payload), Is.EqualTo(0x96));
                Assert.That((byte)payload.GetType().GetField("Number").GetValue(payload), Is.EqualTo(56));
                Assert.That((byte)payload.GetType().GetField("Value").GetValue(payload), Is.EqualTo(21));

                hub.SetLed(reference, owner, MidiLedLayer.State, MidiLedStyle.Off);
                hub.FlushFrame(runtime.Services);
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
                Debug.Log("[APC_LED_READY] The upper-left Pad 1 should now be solid green for 60 seconds.");
                yield return new WaitForSeconds(60f);
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

        private static object GetRawOutput(MidiHub hub, string surfaceId)
        {
            var surfaces = (IDictionary)typeof(MidiHub).GetField("surfaceRuntime", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hub);
            var surface = surfaces[surfaceId];
            return surface.GetType().GetField("Output", BindingFlags.Instance | BindingFlags.Public).GetValue(surface);
        }

        private static IDictionary GetSentPayloads(MidiHub hub)
            => (IDictionary)typeof(MidiHub).GetField("sentPayloads", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hub);

        private static void SetField(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
