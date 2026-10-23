using System.Buffers;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MondmadeVJ0.Tests
{
    public sealed class MidiHubContractTests
    {
        [Test]
        public void MinisBackendRejectsOverlappingProductAssignmentsButRawOrdinalsRemainSelectable()
        {
            var profile = MidiSurfaceProfile.CreateInstance<MidiSurfaceProfile>();
            profile.Configure("test", "APC mini mk2", new[]
            {
                new MidiSurfaceControl { id = "pad", type = MidiControlType.Button, inputAddressType = MidiAddressType.Note, inputNumber = 56 }
            });
            var firstMinis = new MidiSurfaceInstance();
            firstMinis.Configure(profile, "APC mini mk2", 0, MidiInputBackend.Minis);
            var secondMinis = new MidiSurfaceInstance();
            secondMinis.Configure(profile, "APC mini mk2", 1, MidiInputBackend.Minis);
            var firstRaw = new MidiSurfaceInstance();
            firstRaw.Configure(profile, "APC mini mk2", 0, MidiInputBackend.RtMidiRaw, 0, 0);
            var secondRaw = new MidiSurfaceInstance();
            secondRaw.Configure(profile, "APC mini mk2", 1, MidiInputBackend.RtMidiRaw, 2, 2);

            Assert.That(HasAmbiguousMinisAssignment(firstMinis, new[] { firstMinis, secondMinis }), Is.True);
            Assert.That(HasAmbiguousMinisAssignment(firstMinis, new[] { firstMinis, firstRaw }), Is.True);
            Assert.That(HasAmbiguousMinisAssignment(firstRaw, new[] { firstRaw, secondRaw }), Is.False);

            var unrelatedMinis = new MidiSurfaceInstance();
            unrelatedMinis.Configure(profile, "Other controller", 0, MidiInputBackend.Minis);
            Assert.That(HasAmbiguousMinisAssignment(firstMinis, new[] { firstMinis, unrelatedMinis }), Is.False);
            UnityEngine.Object.Destroy(profile);
        }

        private static bool HasAmbiguousMinisAssignment(MidiSurfaceInstance instance, IReadOnlyList<MidiSurfaceInstance> instances)
        {
            var method = typeof(MidiHub).GetMethod("HasAmbiguousMinisAssignment", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "MidiHub must expose its internal assignment decision to this contract test.");
            return (bool)method.Invoke(null, new object[] { instance, instances });
        }

        [UnityTest]
        public IEnumerator RawNoteOnZeroReleasesControlAndSurfaceInstancesStayIsolated()
        {
            var profile = MidiSurfaceProfile.CreateInstance<MidiSurfaceProfile>();
            profile.Configure("test", "APC mini mk2", new[]
            {
                new MidiSurfaceControl { id = "pad_0_0", label = "Pad 1", type = MidiControlType.VelocityButton, inputAddressType = MidiAddressType.Note, channel = -1, inputNumber = 56, pressThreshold = 0.5f }
            });
            var first = new MidiSurfaceInstance();
            first.Configure(profile, "APC mini mk2", 0, MidiInputBackend.RtMidiRaw, -1, -1);
            var second = new MidiSurfaceInstance();
            second.Configure(profile, "APC mini mk2", 1, MidiInputBackend.RtMidiRaw, -1, -1);
            var harness = CreateHarness(first, second);
            var changes = new List<MidiControlChange>();
            harness.Hub.ControlChanged += changes.Add;
            try
            {
                PushRaw(harness, first.InstanceId, 0x90, 56, 100);
                var firstReference = new MidiControlReference(first.InstanceId, "pad_0_0");
                var secondReference = new MidiControlReference(second.InstanceId, "pad_0_0");
                Assert.That(harness.Hub.GetState(firstReference).Pressed, Is.True);
                Assert.That(harness.Hub.GetState(secondReference).Pressed, Is.False);
                Assert.That(harness.Hub.GetState(firstReference).Velocity, Is.EqualTo(100f / 127f).Within(0.0001f));
                Assert.That(changes, Has.Count.EqualTo(1));
                Assert.That(changes[0].PressedDown, Is.True);

                PushRaw(harness, first.InstanceId, 0x90, 56, 0);
                Assert.That(harness.Hub.GetState(firstReference).Pressed, Is.False);
                Assert.That(changes, Has.Count.EqualTo(2));
                Assert.That(changes[1].Released, Is.True);

                PushRaw(harness, second.InstanceId, 0x90, 56, 127);
                Assert.That(harness.Hub.GetState(firstReference).Pressed, Is.False);
                Assert.That(harness.Hub.GetState(secondReference).Pressed, Is.True);
            }
            finally
            {
                harness.Hub.ControlChanged -= changes.Add;
                DestroyHarness(harness);
                UnityEngine.Object.Destroy(profile);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator RelativeEncoderDecodesAllDocumentedFormats()
        {
            var profile = MidiSurfaceProfile.CreateInstance<MidiSurfaceProfile>();
            profile.Configure("test", "Encoder surface", new[]
            {
                Relative("twos", 20, MidiRelativeMode.TwosComplement),
                Relative("binary", 21, MidiRelativeMode.BinaryOffset),
                Relative("sign", 22, MidiRelativeMode.SignMagnitude)
            });
            var surface = new MidiSurfaceInstance();
            surface.Configure(profile, "Encoder surface", 0, MidiInputBackend.RtMidiRaw, -1, -1);
            var harness = CreateHarness(surface);
            try
            {
                PushControl(harness, surface.InstanceId, MidiAddressType.ControlChange, 0, 20, 1f / 127f);
                PushControl(harness, surface.InstanceId, MidiAddressType.ControlChange, 0, 21, 65f / 127f);
                PushControl(harness, surface.InstanceId, MidiAddressType.ControlChange, 0, 22, 65f / 127f);

                Assert.That(harness.Hub.GetState(new MidiControlReference(surface.InstanceId, "twos")).Value, Is.EqualTo(0.5f + 1f / 63f).Within(0.0001f));
                Assert.That(harness.Hub.GetState(new MidiControlReference(surface.InstanceId, "binary")).Value, Is.EqualTo(0.5f + 1f / 63f).Within(0.0001f));
                Assert.That(harness.Hub.GetState(new MidiControlReference(surface.InstanceId, "sign")).Value, Is.EqualTo(0.5f - 1f / 63f).Within(0.0001f));
            }
            finally
            {
                DestroyHarness(harness);
                UnityEngine.Object.Destroy(profile);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator AbsoluteButtonPitchAndPressureControlsMapToTheirValues()
        {
            var profile = MidiSurfaceProfile.CreateInstance<MidiSurfaceProfile>();
            profile.Configure("test", "Control types", new[]
            {
                new MidiSurfaceControl { id = "button", type = MidiControlType.Button, inputAddressType = MidiAddressType.Note, channel = 0, inputNumber = 10, pressThreshold = 0.5f },
                new MidiSurfaceControl { id = "velocity", type = MidiControlType.VelocityButton, inputAddressType = MidiAddressType.Note, channel = 0, inputNumber = 11, pressThreshold = 0.1f },
                new MidiSurfaceControl { id = "fader", type = MidiControlType.AbsoluteFader, inputAddressType = MidiAddressType.ControlChange, channel = 0, inputNumber = 12, minimum = -1f, maximum = 1f },
                new MidiSurfaceControl { id = "knob", type = MidiControlType.AbsoluteKnob, inputAddressType = MidiAddressType.ControlChange, channel = 0, inputNumber = 13, minimum = 2f, maximum = 6f },
                new MidiSurfaceControl { id = "bend", type = MidiControlType.PitchBend, inputAddressType = MidiAddressType.PitchBend, channel = 0, minimum = -1f, maximum = 1f },
                new MidiSurfaceControl { id = "aftertouch", type = MidiControlType.Aftertouch, inputAddressType = MidiAddressType.ChannelPressure, channel = 0 }
            });
            var surface = new MidiSurfaceInstance();
            surface.Configure(profile, "Control types", 0, MidiInputBackend.RtMidiRaw, -1, -1);
            var harness = CreateHarness(surface);
            try
            {
                PushControl(harness, surface.InstanceId, MidiAddressType.Note, 0, 10, 0.75f);
                PushControl(harness, surface.InstanceId, MidiAddressType.Note, 0, 11, 0.25f);
                PushControl(harness, surface.InstanceId, MidiAddressType.ControlChange, 0, 12, 0.75f);
                PushControl(harness, surface.InstanceId, MidiAddressType.ControlChange, 0, 13, 0.25f);
                PushControl(harness, surface.InstanceId, MidiAddressType.PitchBend, 0, 0, 0.5f);
                PushControl(harness, surface.InstanceId, MidiAddressType.ChannelPressure, 0, 0, 0.75f);

                Assert.That(harness.Hub.GetState(new MidiControlReference(surface.InstanceId, "button")).Pressed, Is.True);
                var velocity = harness.Hub.GetState(new MidiControlReference(surface.InstanceId, "velocity"));
                Assert.That(velocity.Pressed, Is.True);
                Assert.That(velocity.Velocity, Is.EqualTo(0.25f).Within(0.0001f));
                Assert.That(harness.Hub.GetState(new MidiControlReference(surface.InstanceId, "fader")).Value, Is.EqualTo(0.5f).Within(0.0001f));
                Assert.That(harness.Hub.GetState(new MidiControlReference(surface.InstanceId, "knob")).Value, Is.EqualTo(3f).Within(0.0001f));
                Assert.That(harness.Hub.GetState(new MidiControlReference(surface.InstanceId, "bend")).Value, Is.EqualTo(0.5f).Within(0.0001f));
                var aftertouch = harness.Hub.GetState(new MidiControlReference(surface.InstanceId, "aftertouch"));
                Assert.That(aftertouch.Value, Is.EqualTo(0.75f).Within(0.0001f));
                Assert.That(aftertouch.Pressure, Is.EqualTo(0.75f).Within(0.0001f));
            }
            finally
            {
                DestroyHarness(harness);
                UnityEngine.Object.Destroy(profile);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator XySourceCombinesTwoCompatibleControlsAndPublishesInitialValue()
        {
            var profile = MidiSurfaceProfile.CreateInstance<MidiSurfaceProfile>();
            profile.Configure("test", "XY surface", new[]
            {
                new MidiSurfaceControl { id = "x", label = "X", type = MidiControlType.XY, inputAddressType = MidiAddressType.ControlChange, channel = 0, inputNumber = 20, initialValue = 0.2f, minimum = 0f, maximum = 1f },
                new MidiSurfaceControl { id = "y", label = "Y", type = MidiControlType.XY, inputAddressType = MidiAddressType.ControlChange, channel = 0, inputNumber = 21, initialValue = 0.8f, minimum = 0f, maximum = 1f }
            });
            var surface = new MidiSurfaceInstance();
            surface.Configure(profile, "XY surface", 0, MidiInputBackend.RtMidiRaw, -1, -1);
            var source = new MidiXYVector2Source();
            SetField(source, "xControl", new MidiControlReference(surface.InstanceId, "x"));
            SetField(source, "yControl", new MidiControlReference(surface.InstanceId, "y"));
            var route = new Vector2Route();
            route.Configure("XY pad", source);
            var output = new RecordingVector2Output();
            route.Outputs.Add(output);
            var harness = CreateRouteHarness(surface, route);
            try
            {
                Assert.That(route.Status, Is.EqualTo("Active"));
                Assert.That(route.CurrentValue, Is.EqualTo(new Vector2(0.2f, 0.8f)));
                CollectionAssert.AreEqual(new[] { new Vector2(0.2f, 0.8f) }, output.Values);

                PushControl(harness, surface.InstanceId, MidiAddressType.ControlChange, 0, 20, 0.65f);
                PushControl(harness, surface.InstanceId, MidiAddressType.ControlChange, 0, 21, 0.4f);
                CollectionAssert.AreEqual(new[] { new Vector2(0.2f, 0.8f), new Vector2(0.65f, 0.8f), new Vector2(0.65f, 0.4f) }, output.Values);
            }
            finally
            {
                DestroyHarness(harness);
                UnityEngine.Object.Destroy(profile);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ExternalSignalUpdatesMidiLedStateAndInteractionFlashFallsBackToStateLayer()
        {
            var profile = MidiSurfaceProfile.CreateInstance<MidiSurfaceProfile>();
            profile.Configure("test", "LED layers", new[]
            {
                new MidiSurfaceControl { id = "pad", type = MidiControlType.Button, hasLed = true, outputAddressType = MidiAddressType.Note, outputChannel = 0, outputNumber = 56 }
            });
            var surface = new MidiSurfaceInstance();
            surface.Configure(profile, "LED layers", 0, MidiInputBackend.RtMidiRaw, -1, -1);
            var signalObject = new GameObject("LED state trigger");
            var signal = signalObject.AddComponent<SharedTriggerSignal>();

            var stateTrigger = new SignalTriggerSource();
            SetField(stateTrigger, "signal", signal);
            var toggle = new ToggleBoolSource();
            SetField(toggle, "trigger", stateTrigger);
            var stateRoute = new BoolRoute();
            stateRoute.Configure("external LED state", toggle);
            var stateOutput = new MidiLedBoolOutput();
            SetField(stateOutput, "control", new MidiControlReference(surface.InstanceId, "pad"));
            stateRoute.Outputs.Add(stateOutput);

            var flashTrigger = new SignalTriggerSource();
            SetField(flashTrigger, "signal", signal);
            var flashRoute = new TriggerRoute();
            flashRoute.Configure("interaction LED flash", flashTrigger);
            var flashOutput = new MidiLedTriggerFlashOutput();
            SetField(flashOutput, "control", new MidiControlReference(surface.InstanceId, "pad"));
            SetField(flashOutput, "durationSeconds", 0.05f);
            flashRoute.Outputs.Add(flashOutput);

            var harness = CreateRouteHarness(surface, stateRoute, flashRoute);
            try
            {
                var key = surface.InstanceId + "\0pad";
                var initial = ResolveLedStyle(harness.Hub, key);
                Assert.That(initial.brightness, Is.EqualTo(0f));

                signal.Fire();
                Assert.That(stateRoute.CurrentValue, Is.True);
                var flashing = ResolveLedStyle(harness.Hub, key);
                Assert.That(flashing.color, Is.EqualTo(Color.white), "The Interaction layer must temporarily override the State LED.");
                Assert.That(flashing.brightness, Is.EqualTo(1f));

                yield return new WaitForSecondsRealtime(0.12f);

                var restored = ResolveLedStyle(harness.Hub, key);
                Assert.That(restored.color, Is.EqualTo(Color.green), "When the Interaction flash expires, the latest State LED must become visible again.");
                Assert.That(restored.brightness, Is.EqualTo(1f));
            }
            finally
            {
                DestroyHarness(harness);
                Object.Destroy(signalObject);
                Object.Destroy(profile);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ValueAndIndexedOutputsPublishTheirMidiLedStyles()
        {
            var profile = MidiSurfaceProfile.CreateInstance<MidiSurfaceProfile>();
            profile.Configure("test", "LED value outputs", new[]
            {
                LedControl("float", 10),
                LedControl("color", 11),
                LedControl("index0", 12),
                LedControl("index1", 13),
                LedControl("index2", 14)
            });
            var surface = new MidiSurfaceInstance();
            surface.Configure(profile, "LED value outputs", 0, MidiInputBackend.RtMidiRaw, -1, -1);

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            var floatRoute = new FloatRoute();
            floatRoute.Configure("float LED", new ConstantFloatSource { Value = 0.25f });
            var floatOutput = new MidiLedValueFloatOutput();
            SetField(floatOutput, "control", new MidiControlReference(surface.InstanceId, "float"));
            SetField(floatOutput, "gradient", gradient);
            SetField(floatOutput, "brightness", 0.6f);
            floatRoute.Outputs.Add(floatOutput);

            var expectedColor = new Color(0.2f, 0.3f, 0.4f, 0.5f);
            var colorRoute = new ColorRoute();
            colorRoute.Configure("color LED", new ConstantColorSource { Value = expectedColor });
            var colorOutput = new MidiLedColorOutput();
            SetField(colorOutput, "control", new MidiControlReference(surface.InstanceId, "color"));
            SetField(colorOutput, "brightness", 0.8f);
            colorRoute.Outputs.Add(colorOutput);

            var indexedRoute = new IntRoute();
            indexedRoute.Configure("indexed LED", new ConstantIntSource { Value = 1 });
            var indexedOutput = new MidiLedIndexedIntOutput();
            SetField(indexedOutput, "controls", new[]
            {
                new MidiControlReference(surface.InstanceId, "index0"),
                new MidiControlReference(surface.InstanceId, "index1"),
                new MidiControlReference(surface.InstanceId, "index2")
            });
            SetField(indexedOutput, "offColor", Color.black);
            SetField(indexedOutput, "onColor", Color.magenta);
            indexedRoute.Outputs.Add(indexedOutput);

            var harness = CreateRouteHarness(surface, floatRoute, colorRoute, indexedRoute);
            try
            {
                Assert.That(floatRoute.Status, Is.EqualTo("Active"));
                Assert.That(colorRoute.Status, Is.EqualTo("Active"));
                Assert.That(indexedRoute.Status, Is.EqualTo("Active"));

                var floatStyle = ResolveLedStyle(harness.Hub, surface.InstanceId + "\0float");
                Assert.That(floatStyle.color, Is.EqualTo(gradient.Evaluate(0.25f)));
                Assert.That(floatStyle.brightness, Is.EqualTo(0.6f));

                var colorStyle = ResolveLedStyle(harness.Hub, surface.InstanceId + "\0color");
                Assert.That(colorStyle.color, Is.EqualTo(expectedColor));
                Assert.That(colorStyle.brightness, Is.EqualTo(0.8f));

                AssertIndexedStyle(harness.Hub, surface.InstanceId, "index0", Color.black, 0f);
                AssertIndexedStyle(harness.Hub, surface.InstanceId, "index1", Color.magenta, 1f);
                AssertIndexedStyle(harness.Hub, surface.InstanceId, "index2", Color.black, 0f);
            }
            finally
            {
                DestroyHarness(harness);
                Object.Destroy(profile);
            }
            yield return null;
        }

        private static MidiSurfaceControl LedControl(string id, int number)
            => new MidiSurfaceControl
            {
                id = id,
                type = MidiControlType.Button,
                inputAddressType = MidiAddressType.Note,
                inputNumber = number,
                hasLed = true,
                outputAddressType = MidiAddressType.Note,
                outputNumber = number
            };

        private static void AssertIndexedStyle(MidiHub hub, string surfaceId, string controlId, Color color, float brightness)
        {
            var style = ResolveLedStyle(hub, surfaceId + "\0" + controlId);
            Assert.That(style.color, Is.EqualTo(color), controlId);
            Assert.That(style.brightness, Is.EqualTo(brightness), controlId);
        }

        private static MidiSurfaceControl Relative(string id, int number, MidiRelativeMode mode)
            => new MidiSurfaceControl { id = id, type = MidiControlType.RelativeEncoder, inputAddressType = MidiAddressType.ControlChange, channel = 0, inputNumber = number, relativeMode = mode, initialValue = 0.5f, minimum = 0f, maximum = 1f };

        private static Harness CreateHarness(params MidiSurfaceInstance[] surfaces)
        {
            var gameObject = new GameObject("MIDI Hub contract test");
            gameObject.SetActive(false);
            var hub = gameObject.AddComponent<MidiHub>();
            var runtime = gameObject.AddComponent<MondmadeVJ0Runtime>();
            SetField(hub, "surfaces", surfaces);
            SetField(runtime, "modules", new MonoBehaviour[] { hub });
            gameObject.SetActive(true);
            return new Harness(gameObject, hub, runtime);
        }

        private static Harness CreateRouteHarness(MidiSurfaceInstance surface, params IRoute[] routes)
        {
            var gameObject = new GameObject("MIDI XY route contract test");
            gameObject.SetActive(false);
            var hub = gameObject.AddComponent<MidiHub>();
            var runtime = gameObject.AddComponent<MondmadeVJ0Runtime>();
            var host = gameObject.AddComponent<RouteHost>();
            SetField(hub, "surfaces", new[] { surface });
            SetField(runtime, "modules", new MonoBehaviour[] { hub });
            SetField(runtime, "routeHosts", new[] { host });
            SetField(host, "routes", new List<IRoute>(routes));
            gameObject.SetActive(true);
            return new Harness(gameObject, hub, runtime);
        }

        private static MidiLedStyle ResolveLedStyle(MidiHub hub, string key)
        {
            var method = typeof(MidiHub).GetMethod("ResolveLayer", BindingFlags.Instance | BindingFlags.NonPublic);
            return (MidiLedStyle)method.Invoke(hub, new object[] { key });
        }

        private static void PushRaw(Harness harness, string surfaceId, byte status, byte number, byte value)
        {
            var bytes = ArrayPool<byte>.Shared.Rent(3);
            bytes[0] = status;
            bytes[1] = number;
            bytes[2] = value;
            PushPacket(harness, CreatePacket(surfaceId, bytes, 3));
        }

        private static void PushControl(Harness harness, string surfaceId, MidiAddressType type, int channel, int number, float value)
            => PushPacket(harness, CreatePacket(surfaceId, type, channel, number, value));

        private static void PushPacket(Harness harness, object packet)
        {
            var hubType = typeof(MidiHub);
            hubType.GetMethod("Enqueue", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(harness.Hub, new[] { packet, (object)false });
            harness.Hub.DrainInputQueue(harness.Runtime.Services, 128);
        }

        private static object CreatePacket(string surfaceId, MidiAddressType type, int channel, int number, float value)
            => CreatePacket(surfaceId, type, channel, number, value, null, 0);

        private static object CreatePacket(string surfaceId, byte[] raw, int length)
            => CreatePacket(surfaceId, default, 0, 0, 0f, raw, length);

        private static object CreatePacket(string surfaceId, MidiAddressType type, int channel, int number, float value, byte[] raw, int length)
        {
            var packetType = typeof(MidiHub).GetNestedType("InputPacket", BindingFlags.NonPublic);
            var constructor = packetType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                new[] { typeof(string), typeof(MidiAddressType), typeof(int), typeof(int), typeof(float), typeof(int), typeof(long), typeof(byte[]), typeof(int), typeof(double) }, null);
            return constructor.Invoke(new object[] { surfaceId, type, channel, number, value, 0, System.Diagnostics.Stopwatch.GetTimestamp(), raw, length, 0d });
        }

        private static void SetField(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static void DestroyHarness(Harness harness)
        {
            if (harness.GameObject != null) UnityEngine.Object.Destroy(harness.GameObject);
        }

        private sealed class RecordingVector2Output : IValueOutput<Vector2>
        {
            public readonly List<Vector2> Values = new List<Vector2>();
            public void Initialize(RouteContext context) { }
            public void Write(Vector2 value) => Values.Add(value);
        }

        private sealed class Harness
        {
            public readonly GameObject GameObject;
            public readonly MidiHub Hub;
            public readonly MondmadeVJ0Runtime Runtime;
            public Harness(GameObject gameObject, MidiHub hub, MondmadeVJ0Runtime runtime) { GameObject = gameObject; Hub = hub; Runtime = runtime; }
        }
    }
}
