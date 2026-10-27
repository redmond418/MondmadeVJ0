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
            if (window != null)
            {
                Invoke(window, "StopListening");
                UnityEngine.Object.DestroyImmediate(window);
            }
            if (hostObject != null) UnityEngine.Object.DestroyImmediate(hostObject);
            if (hubObject != null) UnityEngine.Object.DestroyImmediate(hubObject);
            if (profile != null) UnityEngine.Object.DestroyImmediate(profile);
            if (previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(previewScene);
        }

        [Test]
        public void StartListeningInEditModeCreatesAnInputOnlyHub()
        {
            SetField(window, "hub", hub);
            SetField(window, "surfaceId", hub.Surfaces[0].InstanceId);

            Invoke(window, "StartListening");

            var editorInputHub = (MidiHub)GetField(window, "editorInputHub");
            Assert.That(GetField(window, "listening"), Is.True,
                "Edit-mode MIDI Learn should receive input without entering Play mode.");
            Assert.That(editorInputHub, Is.Not.Null);
            Assert.That(editorInputHub, Is.Not.SameAs(hub),
                "Edit-mode Learn must not initialize the scene's runtime hub in place.");
            Assert.That(editorInputHub.Surfaces[0].OutputPortOrdinal, Is.EqualTo(-1),
                "The editor-only listener must not open a MIDI output port.");
            Assert.That(GetField(window, "lastMessage"), Is.EqualTo("Waiting for a MIDI message."));
        }

        [Test]
        public void CancelStopsEditModeListening()
        {
            SetField(window, "hub", hub);
            Invoke(window, "StartListening");

            Invoke(window, "StopListening");

            Assert.That(GetField(window, "listening"), Is.False);
            Assert.That(GetField(window, "editorInputHub"), Is.Null,
                "Cancel must shut down the temporary editor listener.");
        }

        [Test]
        public void ReceivedLogicalControlWritesTheSelectedSerializedMidiReference()
        {
            var route = new FloatRoute();
            route.Configure("MIDI Learn assignment", new MidiAbsoluteFloatSource());
            host.AddRoute(route);
            SetField(window, "hub", hub);
            SetField(window, "targetObject", host);
            SetField(window, "propertyPath", "routes.Array.data[0].source.control");
            SetField(window, "surfaceId", "test-surface");
            Invoke(window, "StartListening");

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
            Assert.That(GetField(window, "editorInputHub"), Is.Null,
                "A successful Learn should close its temporary editor listener.");
        }

        [Test]
        public void SimulatorInputCanBeLearnedInEditMode()
        {
            var route = new FloatRoute();
            route.Configure("MIDI Learn assignment", new MidiAbsoluteFloatSource());
            host.AddRoute(route);
            var surfaceId = hub.Surfaces[0].InstanceId;
            SetField(window, "hub", hub);
            SetField(window, "targetObject", host);
            SetField(window, "propertyPath", "routes.Array.data[0].source.control");
            SetField(window, "surfaceId", surfaceId);
            Invoke(window, "StartListening");

            var simulationObject = new GameObject("MIDI Simulator Learn Source");
            SceneManager.MoveGameObjectToScene(simulationObject, previewScene);
            var simulationHub = simulationObject.AddComponent<MidiHub>();
            EditorUtility.CopySerialized(hub, simulationHub);
            var services = new RuntimeServices();
            try
            {
                simulationHub.Initialize(services);
                var reference = new MidiControlReference(surfaceId, "fader");
                Assert.That(simulationHub.QueueEditorInput(reference, 0.75f), Is.True);
                simulationHub.DrainInputQueue(services, 16);

                var serialized = new SerializedObject(host);
                serialized.Update();
                var learned = serialized.FindProperty("routes.Array.data[0].source.control");
                Assert.That(learned.FindPropertyRelative("surfaceInstanceId").stringValue, Is.EqualTo(surfaceId));
                Assert.That(learned.FindPropertyRelative("controlId").stringValue, Is.EqualTo("fader"));
                Assert.That(GetField(window, "lastMessage"), Is.EqualTo("Assigned fader."));
            }
            finally
            {
                simulationHub.Shutdown(services);
                UnityEngine.Object.DestroyImmediate(simulationObject);
            }
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
