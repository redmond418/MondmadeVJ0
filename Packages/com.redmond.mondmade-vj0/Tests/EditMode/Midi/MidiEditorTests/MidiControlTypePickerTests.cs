using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using MondmadeVJ0.Editor;
using UnityEditor;
using UnityEngine;

namespace MondmadeVJ0.Tests
{
    public sealed class MidiControlTypePickerTests
    {
        private MidiSurfaceProfile profile;
        private GameObject hostObject;
        private RouteHost host;

        [SetUp]
        public void SetUp()
        {
            profile = ScriptableObject.CreateInstance<MidiSurfaceProfile>();
            profile.Configure("test", "Picker", new[]
            {
                Control("button", MidiControlType.Button),
                Control("velocity", MidiControlType.VelocityButton),
                Control("fader", MidiControlType.AbsoluteFader),
                Control("knob", MidiControlType.AbsoluteKnob),
                Control("relative", MidiControlType.RelativeEncoder),
                Control("xy", MidiControlType.XY),
                Control("bend", MidiControlType.PitchBend),
                Control("aftertouch", MidiControlType.Aftertouch)
            });
            hostObject = new GameObject("MIDI picker test host");
            host = hostObject.AddComponent<RouteHost>();
        }

        [TearDown]
        public void TearDown()
        {
            if (profile != null) Object.DestroyImmediate(profile);
            if (hostObject != null) Object.DestroyImmediate(hostObject);
        }

        [Test]
        public void ValueAndTriggerPickersOnlyShowCompatibleControlTypes()
        {
            CollectionAssert.AreEqual(new[] { "fader", "knob", "bend", "aftertouch" },
                GetControls(new FloatRoute(), new MidiAbsoluteFloatSource(), "control"));
            CollectionAssert.AreEqual(new[] { "relative" },
                GetControls(new FloatRoute(), new MidiRelativeFloatSource(), "control"));
            CollectionAssert.AreEqual(new[] { "velocity" },
                GetControls(new FloatRoute(), new MidiVelocityFloatSource(), "control"));
            CollectionAssert.AreEqual(new[] { "button", "velocity" },
                GetControls(new BoolRoute(), new MidiButtonBoolSource(), "control"));
            CollectionAssert.AreEqual(new[] { "button", "velocity" },
                GetControls(new TriggerRoute(), new MidiButtonTriggerSource(), "control"));
            CollectionAssert.AreEqual(new[] { "xy" },
                GetControls(new Vector2Route(), new MidiXYVector2Source(), "xControl"));
        }

        [Test]
        public void GroupIndexPickerOnlyShowsButtonTypes()
        {
            var source = new MidiButtonGroupIndexSource();
            SetField(source, "controls", new[] { new MidiControlReference("surface", "button") });
            CollectionAssert.AreEqual(new[] { "button", "velocity" },
                GetControls(new IntRoute(), source, "controls.Array.data[0]"));
        }

        private List<string> GetControls(IRoute route, RouteNode source, string memberPath)
        {
            SetField(route, "source", source);
            Assert.That(host.AddRoute(route), Is.True);
            try
            {
                var serializedHost = new SerializedObject(host);
                var property = serializedHost.FindProperty($"routes.Array.data[0].source.{memberPath}");
                Assert.That(property, Is.Not.Null, $"Serialized MIDI reference path: {memberPath}");
                var method = typeof(MidiControlReferenceDrawer).GetMethod("GetCompatibleControls", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null);
                var compatible = (List<MidiSurfaceControl>)method.Invoke(null, new object[] { property, profile.Controls });
                var ids = new List<string>();
                foreach (var control in compatible) ids.Add(control.id);
                return ids;
            }
            finally { host.RemoveRoute(route.Id); }
        }

        private static MidiSurfaceControl Control(string id, MidiControlType type)
            => new MidiSurfaceControl { id = id, label = id, type = type };

        private static void SetField(object target, string name, object value)
        {
            for (var type = target.GetType(); type != null && type != typeof(object); type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
            }
            Assert.Fail($"Field '{name}' was not found on {target.GetType().Name}.");
        }
    }
}
