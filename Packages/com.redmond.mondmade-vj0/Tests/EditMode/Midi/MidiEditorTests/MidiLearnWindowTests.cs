using System;
using System.Reflection;
using MondmadeVJ0.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MondmadeVJ0.Tests
{
    public sealed class MidiLearnWindowTests
    {
        private Scene previewScene;
        private GameObject hostObject;
        private GameObject hubObject;
        private RouteHost host;
        private MidiHub hub;
        private MidiSurfaceProfile profile;
        private MidiLearnWindow window;

        [SetUp]
        public void SetUp()
        {
            previewScene = EditorSceneManager.NewPreviewScene();
            hostObject = new GameObject("MIDI Learn Test RouteHost");
            hubObject = new GameObject("MIDI Learn Test Hub");
            SceneManager.MoveGameObjectToScene(hostObject, previewScene);
            SceneManager.MoveGameObjectToScene(hubObject, previewScene);
            host = hostObject.AddComponent<RouteHost>();
            hub = hubObject.AddComponent<MidiHub>();
            profile = ScriptableObject.CreateInstance<MidiSurfaceProfile>();
            profile.Configure("test", "Test Surface", new[]
            {
                new MidiSurfaceControl { id = "fader", label = "Fader", type = MidiControlType.AbsoluteFader }
            });
            var surface = new MidiSurfaceInstance();
            surface.Configure(profile, "Test Surface");
            SetField(hub, "surfaces", new[] { surface });
            window = ScriptableObject.CreateInstance<MidiLearnWindow>();
        }

        [TearDown]
        public void TearDown()
        {
            if (window != null) UnityEngine.Object.DestroyImmediate(window);
            if (hostObject != null) UnityEngine.Object.DestroyImmediate(hostObject);
            if (hubObject != null) UnityEngine.Object.DestroyImmediate(hubObject);
            if (profile != null) UnityEngine.Object.DestroyImmediate(profile);
            if (previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(previewScene);
        }

        [Test]
        public void StartListeningInEditModeArmsAutomaticallyForPlayMode()
        {
            SetField(window, "hub", hub);
            SetField(window, "surfaceId", hub.Surfaces[0].InstanceId);

            Invoke(window, "StartListening");

            Assert.That(GetField(window, "startOnPlay"), Is.True,
                "Edit-mode MIDI Learn should arm and attach after Play mode begins.");
            Assert.That(GetField(window, "listening"), Is.False,
                "The window must not imply that a non-running Runtime is already receiving MIDI.");
            Assert.That(GetField(window, "lastMessage"), Is.EqualTo("Armed. Enter Play mode to start MIDI Learn."));
        }

        [Test]
        public void CancelClearsAnArmedEditModeLearn()
        {
            SetField(window, "hub", hub);
            Invoke(window, "StartListening");

            Invoke(window, "StopListening", true);

            Assert.That(GetField(window, "startOnPlay"), Is.False,
                "Cancel must disarm a Learn request before Play mode begins.");
        }

        [Test]
        public void ReceivedLogicalControlWritesTheSelectedSerializedMidiReference()
        {
            var route = new FloatRoute();
            route.Configure("MIDI Learn assignment", new MidiAbsoluteFloatSource());
            host.AddRoute(route);
            SetField(window, "targetObject", host);
            SetField(window, "propertyPath", "routes.Array.data[0].source.control");
            SetField(window, "surfaceId", "test-surface");
            SetField(window, "listening", true);

            var change = new MidiControlChange(
                new MidiControlReference("test-surface", "fader"),
                new MidiControlState(0.5f, 0f, false, 0f, 0), false, false);
            Invoke(window, "OnControlChanged", change);

            var serialized = new SerializedObject(host);
            serialized.Update();
            var reference = serialized.FindProperty("routes.Array.data[0].source.control");
            Assert.That(reference.FindPropertyRelative("surfaceInstanceId").stringValue, Is.EqualTo("test-surface"));
            Assert.That(reference.FindPropertyRelative("controlId").stringValue, Is.EqualTo("fader"));
            Assert.That(GetField(window, "lastMessage"), Is.EqualTo("Assigned fader."));
        }

        private static void Invoke(object target, string name, params object[] arguments)
        {
            var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Expected MidiLearnWindow method " + name);
            method.Invoke(target, arguments);
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

        private static object GetField(object target, string name)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field.GetValue(target);
            }
            throw new MissingFieldException(target.GetType().FullName, name);
        }
    }
}
