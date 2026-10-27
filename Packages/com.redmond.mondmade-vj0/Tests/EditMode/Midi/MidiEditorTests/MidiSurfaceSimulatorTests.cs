using MondmadeVJ0;
using NUnit.Framework;
using UnityEngine;

namespace MondmadeVJ0.Tests
{
    public sealed class MidiSurfaceSimulatorTests
    {
        private GameObject hubObject;
        private MidiHub hub;
        private MidiSurfaceProfile profile;
        private RuntimeServices services;

        [SetUp]
        public void SetUp()
        {
            hubObject = new GameObject("MIDI Simulator Test Hub");
            hub = hubObject.AddComponent<MidiHub>();
            profile = ScriptableObject.CreateInstance<MidiSurfaceProfile>();
            profile.Configure("test", "Simulator Test Surface", new[]
            {
                new MidiSurfaceControl
                {
                    id = "fader", label = "Fader", type = MidiControlType.AbsoluteFader,
                    inputAddressType = MidiAddressType.ControlChange, channel = 0, inputNumber = 48,
                    minimum = 0f, maximum = 1f
                },
                new MidiSurfaceControl
                {
                    id = "pad", label = "Pad", type = MidiControlType.VelocityButton,
                    inputAddressType = MidiAddressType.Note, channel = 0, inputNumber = 36,
                    pressThreshold = 0.5f
                }
            });
            var surface = new MidiSurfaceInstance();
            surface.Configure(profile, "Mondmade VJ0 Simulator Test", backend: MidiInputBackend.Minis);
            SetField(hub, "surfaces", new[] { surface });
            services = new RuntimeServices();
            hub.Initialize(services);
        }

        [TearDown]
        public void TearDown()
        {
            if (hub != null && services != null) hub.Shutdown(services);
            if (hubObject != null) Object.DestroyImmediate(hubObject);
            if (profile != null) Object.DestroyImmediate(profile);
        }

        [Test]
        public void VirtualFaderInputUsesTheMidiHubQueueAndLogicalControlPath()
        {
            var reference = new MidiControlReference(hub.Surfaces[0].InstanceId, "fader");
            MidiControlChange observed = default;
            hub.ControlChanged += change => observed = change;

            Assert.That(hub.QueueEditorInput(reference, 0.72f), Is.True);
            hub.DrainInputQueue(services, 16);

            Assert.That(hub.GetState(reference).Value, Is.EqualTo(0.72f).Within(0.01f));
            Assert.That(observed.Reference.ControlId, Is.EqualTo("fader"));
            Assert.That(observed.State.Value, Is.EqualTo(0.72f).Within(0.01f));
        }

        [Test]
        public void VirtualPadInputProducesAProperPressAndRelease()
        {
            var reference = new MidiControlReference(hub.Surfaces[0].InstanceId, "pad");
            var pressedDownCount = 0;
            var releasedCount = 0;
            hub.ControlChanged += change =>
            {
                if (change.PressedDown) pressedDownCount++;
                if (change.Released) releasedCount++;
            };

            Assert.That(hub.QueueEditorInput(reference, 1f), Is.True);
            hub.DrainInputQueue(services, 16);
            Assert.That(hub.GetState(reference).Pressed, Is.True);
            Assert.That(pressedDownCount, Is.EqualTo(1));

            Assert.That(hub.QueueEditorInput(reference, 0f), Is.True);
            hub.DrainInputQueue(services, 16);
            Assert.That(hub.GetState(reference).Pressed, Is.False);
            Assert.That(releasedCount, Is.EqualTo(1));
        }

        private static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }
    }
}
