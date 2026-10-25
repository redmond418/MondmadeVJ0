using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MondmadeVJ0.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace MondmadeVJ0.Tests
{
    public sealed class RouteHostEditorPersistenceTests
    {
        private const string ScenePath = "Assets/MondmadeVJ0TempA21.unity";
        private GameObject hostObject;
        private GameObject targetObject;
        private RouteHost host;
        private RouteHostEditor editor;
        private Scene temporaryScene;
        private Scene backgroundScene;
        private SceneSetup[] originalSceneSetup;

        [SetUp]
        public void SetUp()
        {
            Undo.ClearAll();
            originalSceneSetup = EditorSceneManager.GetSceneManagerSetup();
            hostObject = new GameObject("A21 RouteHost");
            targetObject = new GameObject("A21 Output Target");
            host = hostObject.AddComponent<RouteHost>();
            editor = UnityEditor.Editor.CreateEditor(host, typeof(RouteHostEditor)) as RouteHostEditor;
        }

        [TearDown]
        public void TearDown()
        {
            if (editor != null) UnityEngine.Object.DestroyImmediate(editor);
            if (hostObject != null) UnityEngine.Object.DestroyImmediate(hostObject);
            if (targetObject != null) UnityEngine.Object.DestroyImmediate(targetObject);
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.IsValid() && scene.isLoaded && SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true);
            if (temporaryScene.IsValid() && temporaryScene.isLoaded && SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(temporaryScene, true);
            if (backgroundScene.IsValid() && backgroundScene.isLoaded && SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(backgroundScene, true);
            AssetDatabase.DeleteAsset(ScenePath);
            if (originalSceneSetup != null && originalSceneSetup.Length > 0)
                EditorSceneManager.RestoreSceneManagerSetup(originalSceneSetup);
            else
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Undo.ClearAll();
        }

        [Test]
        public void DuplicateRouteCopiesConfigurationRegeneratesNodeIdsAndSupportsUndoRedo()
        {
            var route = CreateFloatRoute();
            host.AddRoute(route);

            Invoke("DuplicateRoute", 0, null);

            Assert.That(host.Count, Is.EqualTo(2));
            var clone = (FloatRoute)host.Routes[1];
            Assert.That(clone.Id, Is.Not.EqualTo(route.Id));
            Assert.That(((ConstantFloatSource)clone.Source).Value, Is.EqualTo(0.75f));
            Assert.That(((ConstantFloatSource)clone.Source).NodeId, Is.Not.EqualTo(((ConstantFloatSource)route.Source).NodeId));
            Assert.That(((AddFloatModifier)clone.Modifiers[0]).NodeId, Is.Not.EqualTo(((AddFloatModifier)route.Modifiers[0]).NodeId));

            Undo.PerformUndo();
            editor.serializedObject.Update();
            Assert.That(host.Count, Is.EqualTo(1));

            Undo.PerformRedo();
            editor.serializedObject.Update();
            Assert.That(host.Count, Is.EqualTo(2));
            clone = (FloatRoute)host.Routes[1];
            ((ConstantFloatSource)clone.Source).Value = 0.25f;
            Assert.That(((ConstantFloatSource)((FloatRoute)host.Routes[0]).Source).Value, Is.EqualTo(0.75f));
        }

        [Test]
        public void RouteInspectorShowsNewSourceModifierAndOutputPropertiesExpanded()
        {
            host.AddRoute(CreateFloatRoute());

            var inspector = editor.CreateInspectorGUI();
            var routeProperty = editor.serializedObject.FindProperty("routes").GetArrayElementAtIndex(0);
            var sourceProperty = routeProperty.FindPropertyRelative("source");
            var modifiers = routeProperty.FindPropertyRelative("modifiers");
            var outputs = routeProperty.FindPropertyRelative("outputs");

            Assert.That(inspector, Is.Not.Null);
            Assert.That(sourceProperty.isExpanded, Is.True);
            Assert.That(modifiers.GetArrayElementAtIndex(0).isExpanded, Is.True);
            Assert.That(outputs.GetArrayElementAtIndex(0).isExpanded, Is.True);
        }

        [Test]
        public void RouteAndNodeCopyPastePreserveValuesAndRegenerateStableIds()
        {
            var route = CreateFloatRoute();
            host.AddRoute(route);
            var originalRouteId = route.Id;
            var originalSourceNodeId = ((ConstantFloatSource)route.Source).NodeId;
            var originalModifierNodeId = ((AddFloatModifier)route.Modifiers[0]).NodeId;
            var routes = editor.serializedObject.FindProperty("routes");
            Invoke("CopyRoute", routes.GetArrayElementAtIndex(0));
            Invoke("PasteRoute", 0, null);

            var pastedRoute = (FloatRoute)host.Routes[0];
            Assert.That(pastedRoute.Id, Is.Not.EqualTo(originalRouteId));
            Assert.That(((ConstantFloatSource)pastedRoute.Source).Value, Is.EqualTo(0.75f));
            Assert.That(((ConstantFloatSource)pastedRoute.Source).NodeId, Is.Not.EqualTo(originalSourceNodeId));

            routes = editor.serializedObject.FindProperty("routes");
            var modifiers = routes.GetArrayElementAtIndex(0).FindPropertyRelative("modifiers");
            Invoke("CopyNode", modifiers.GetArrayElementAtIndex(0));
            Invoke("PasteNodeAt", "routes.Array.data[0].modifiers", 0, null);

            pastedRoute = (FloatRoute)host.Routes[0];
            Assert.That(pastedRoute.Modifiers.Count, Is.EqualTo(1));
            var pastedModifier = (AddFloatModifier)pastedRoute.Modifiers[0];
            Assert.That(pastedModifier.NodeId, Is.Not.EqualTo(originalModifierNodeId));
            Assert.That(GetField(pastedModifier, "amount"), Is.EqualTo(0.5f));
            SetField(pastedModifier, "amount", 0.25f);
            Assert.That(GetField(route.Modifiers[0], "amount"), Is.EqualTo(0.5f));
        }

        [Test]
        public void ManagedRouteConfigurationAndSceneObjectReferencesSurviveSaveAndReload()
        {
            AssetDatabase.DeleteAsset(ScenePath);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            temporaryScene = scene;
            var sceneHostObject = new GameObject("A21 Saved RouteHost");
            var sceneTargetObject = new GameObject("A21 Saved Output Target");
            SceneManager.MoveGameObjectToScene(sceneHostObject, scene);
            SceneManager.MoveGameObjectToScene(sceneTargetObject, scene);
            var sceneHost = sceneHostObject.AddComponent<RouteHost>();
            var route = new BoolRoute();
            var source = new ConstantBoolSource { Value = true };
            route.Configure("A21 persisted route", source);
            var output = new RendererEnabledBoolOutput();
            SetField(output, "target", sceneTargetObject.AddComponent<MeshRenderer>());
            route.Outputs.Add(output);
            sceneHost.AddRoute(route);

            Assert.That(EditorSceneManager.SaveScene(scene, ScenePath), Is.True);
            backgroundScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.CloseScene(scene, true);
            var reloaded = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            temporaryScene = reloaded;
            RouteHost restoredHost = null;
            MeshRenderer restoredRenderer = null;
            foreach (var root in reloaded.GetRootGameObjects())
            {
                var candidateHost = root.GetComponent<RouteHost>();
                var candidateRenderer = root.GetComponent<MeshRenderer>();
                if (candidateHost != null) restoredHost = candidateHost;
                if (candidateRenderer != null) restoredRenderer = candidateRenderer;
            }

            Assert.That(restoredHost, Is.Not.Null);
            Assert.That(restoredHost.Count, Is.EqualTo(1));
            var restoredRoute = (BoolRoute)restoredHost.Routes[0];
            Assert.That(restoredRoute.Name, Is.EqualTo("A21 persisted route"));
            Assert.That(((ConstantBoolSource)restoredRoute.Source).Value, Is.True);
            Assert.That(restoredRoute.Id, Is.EqualTo(route.Id));
            var restoredOutput = (RendererEnabledBoolOutput)restoredRoute.Outputs[0];
            var targetField = typeof(RendererEnabledBoolOutput).GetField("target", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(targetField.GetValue(restoredOutput), Is.SameAs(restoredRenderer));
        }

        private FloatRoute CreateFloatRoute()
        {
            var route = new FloatRoute();
            var source = new ConstantFloatSource { Value = 0.75f };
            route.Configure("A21 duplicate route", source);
            var modifier = new AddFloatModifier();
            SetField(modifier, "amount", 0.5f);
            route.Modifiers.Add(modifier);
            route.Outputs.Add(new DebugFloatOutput());
            return route;
        }

        private void Invoke(string name, params object[] arguments)
        {
            var method = typeof(RouteHostEditor).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Expected RouteHostEditor method " + name);
            method.Invoke(editor, arguments);
            editor.serializedObject.Update();
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

    public sealed class RouteHostEditorInspectorTests
    {
        private Scene temporaryScene;
        private GameObject hostObject;
        private GameObject targetObject;
        private RouteHost host;
        private RouteHostEditor editor;

        [SetUp]
        public void SetUp()
        {
            temporaryScene = EditorSceneManager.NewPreviewScene();
            hostObject = new GameObject("A21 Inspector Test RouteHost");
            targetObject = new GameObject("A21 Inspector Test Target");
            SceneManager.MoveGameObjectToScene(hostObject, temporaryScene);
            SceneManager.MoveGameObjectToScene(targetObject, temporaryScene);
            host = hostObject.AddComponent<RouteHost>();
            editor = UnityEditor.Editor.CreateEditor(host, typeof(RouteHostEditor)) as RouteHostEditor;
        }

        [TearDown]
        public void TearDown()
        {
            if (editor != null) UnityEngine.Object.DestroyImmediate(editor);
            if (hostObject != null) UnityEngine.Object.DestroyImmediate(hostObject);
            if (targetObject != null) UnityEngine.Object.DestroyImmediate(targetObject);
            if (temporaryScene.IsValid()) EditorSceneManager.ClosePreviewScene(temporaryScene);
        }

        [Test]
        public void RouteInspectorShowsNestedSourceModifierAndOutputParameters()
        {
            var route = new Vector3Route();
            route.Configure("A21 nested inspector", new FloatToVector3Source
            {
                Input = new ConstantFloatSource { Value = 0.75f }
            });
            var modifier = new AddVector3Modifier();
            SetField(modifier, "amount", new Vector4(0.1f, 0.2f, 0.3f, 0f));
            route.Modifiers.Add(modifier);
            var output = new TransformScaleVector3Output();
            SetField(output, "target", targetObject.transform);
            route.Outputs.Add(output);
            route.Outputs.Add(new ComponentMemberVector3Output());
            host.AddRoute(route);
            var unconfiguredRoute = new FloatRoute();
            unconfiguredRoute.Configure("A21 empty nested source", new FloatBinarySource());
            host.AddRoute(unconfiguredRoute);

            var inspector = editor.CreateInspectorGUI();
            var propertyPaths = inspector.Query<PropertyField>().ToList()
                .Select(field => field.bindingPath)
                .ToList();

            Assert.That(propertyPaths, Does.Contain("routes.Array.data[0].source.input.Value"), "Nested Source settings should be editable.");
            Assert.That(propertyPaths, Does.Contain("routes.Array.data[0].modifiers.Array.data[0].amount"), "Modifier settings should be editable.");
            Assert.That(propertyPaths, Does.Contain("routes.Array.data[0].outputs.Array.data[0].target"), "Output settings should be editable.");
            Assert.That(propertyPaths, Does.Contain("routes.Array.data[0].outputs.Array.data[1]"), "Integration outputs should keep their specialized PropertyDrawers.");

            var labels = inspector.Query<Label>().ToList().Select(label => label.text).ToList();
            Assert.That(labels, Does.Contain("Input (float Source)"), "Nested reference fields should explain the required Source type.");
            Assert.That(inspector.Query<Button>().ToList().Any(button => button.text == "Select"), Is.True,
                "An empty nested Source reference should provide a type picker.");
        }

        [Test]
        public void RouteInspectorProvidesAddPickerForNestedSerializeReferenceLists()
        {
            var route = new FloatRoute();
            route.Configure("A21 nested source list", new MostRecentFloatSource());
            host.AddRoute(route);

            var inspector = editor.CreateInspectorGUI();
            var buttons = inspector.Query<Button>().ToList();

            Assert.That(buttons.Any(button => button.text == "Add Input"), Is.True,
                "Inherited SerializeReference input lists should have a compatible Add picker.");
        }

        [Test]
        public void RouteInspectorDeletesNestedManagedReferenceListItems()
        {
            var source = new MostRecentFloatSource();
            SetField(source, "inputs", new List<IValueSource<float>>
            {
                new ConstantFloatSource { Value = 0.25f }
            });
            var route = new FloatRoute();
            route.Configure("A21 nested source delete", source);
            host.AddRoute(route);

            var inspector = editor.CreateInspectorGUI();
            var deleteButton = inspector.Query<Button>().ToList().Last(button => button.text == "Delete");
            Assert.That(deleteButton, Is.Not.Null, "Each nested list entry should expose a Delete control.");
            Invoke("DeleteNode", "routes.Array.data[0].source.inputs", 0, null);
            editor.serializedObject.Update();
            var inputs = editor.serializedObject.FindProperty("routes").GetArrayElementAtIndex(0)
                .FindPropertyRelative("source").FindPropertyRelative("inputs");

            Assert.That(inputs.arraySize, Is.Zero, "Delete should remove a list entry rather than leave an empty placeholder.");
        }

        [Test]
        public void RouteInspectorProvidesCompatiblePickerForTriggerModifierSources()
        {
            var route = new TriggerRoute();
            route.Configure("A21 nested trigger modifier", new SignalTriggerSource());
            route.Modifiers.Add(new GateTriggerModifier());
            host.AddRoute(route);

            var inspector = editor.CreateInspectorGUI();
            var labels = inspector.Query<Label>().ToList().Select(label => label.text).ToList();

            Assert.That(labels, Does.Contain("Gate (bool Source)"));
            Assert.That(inspector.Query<Button>().ToList().Any(button => button.text == "Select"), Is.True);
        }

        private void Invoke(string name, params object[] arguments)
        {
            var method = typeof(RouteHostEditor).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Expected RouteHostEditor method " + name);
            method.Invoke(editor, arguments);
            editor.serializedObject.Update();
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
