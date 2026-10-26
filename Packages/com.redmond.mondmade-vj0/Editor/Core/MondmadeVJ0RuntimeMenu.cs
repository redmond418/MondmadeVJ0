using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MondmadeVJ0.Editor
{
    internal static class MondmadeVJ0RuntimeMenu
    {
        private static readonly ModuleTemplate[] Modules =
        {
            new ModuleTemplate("Transport", "MondmadeVJ0.MusicalTransport"),
            new ModuleTemplate("MIDI", "MondmadeVJ0.MidiHub"),
            new ModuleTemplate("OSC", "MondmadeVJ0.OscHub"),
            new ModuleTemplate("OSC Transport Sync", "MondmadeVJ0.OscTransportSyncAdapter"),
        };

        [MenuItem("GameObject/Mondmade VJ0/Runtime", false, 10)]
        private static void CreateRuntime(MenuCommand command)
        {
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create MondmadeVJ0 Runtime Template");
            try
            {
                var root = CreateGameObject("MondmadeVJ0 Runtime", command.context as GameObject);
                root.SetActive(false);
                var runtime = Undo.AddComponent<MondmadeVJ0Runtime>(root);
                var modules = new List<MonoBehaviour>(Modules.Length);
                for (var i = 0; i < Modules.Length; i++) AddModule(root, Modules[i], modules);

                var routesObject = CreateGameObject("Routes", root);
                var routeHost = Undo.AddComponent<RouteHost>(routesObject);
                Configure(runtime, modules, routeHost);
                root.SetActive(true);
                Selection.activeObject = root;
                Undo.CollapseUndoOperations(undoGroup);
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }
        }

        private static void AddModule(GameObject root, ModuleTemplate template, List<MonoBehaviour> modules)
        {
            var type = FindLoadedType(template.TypeName);
            if (type == null || !typeof(MonoBehaviour).IsAssignableFrom(type) || !typeof(IRuntimeModule).IsAssignableFrom(type)) return;
            var child = CreateGameObject(template.ObjectName, root);
            var module = Undo.AddComponent(child, type) as MonoBehaviour;
            if (module != null) modules.Add(module);
        }

        private static Type FindLoadedType(string fullName)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < assemblies.Length; i++)
            {
                var type = assemblies[i].GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }

        private static GameObject CreateGameObject(string name, GameObject parent)
        {
            var gameObject = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(gameObject, "Create " + name);
            GameObjectUtility.SetParentAndAlign(gameObject, parent);
            return gameObject;
        }

        private static void Configure(MondmadeVJ0Runtime runtime, List<MonoBehaviour> modules, RouteHost routeHost)
        {
            var serializedRuntime = new SerializedObject(runtime);
            SetObjectArray(serializedRuntime.FindProperty("modules"), modules);
            var hosts = serializedRuntime.FindProperty("routeHosts");
            hosts.arraySize = 1;
            hosts.GetArrayElementAtIndex(0).objectReferenceValue = routeHost;
            serializedRuntime.ApplyModifiedProperties();
        }

        private static void SetObjectArray(SerializedProperty property, List<MonoBehaviour> values)
        {
            property.arraySize = values.Count;
            for (var i = 0; i < values.Count; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        private sealed class ModuleTemplate
        {
            public readonly string ObjectName;
            public readonly string TypeName;

            public ModuleTemplate(string objectName, string typeName)
            {
                ObjectName = objectName;
                TypeName = typeName;
            }
        }
    }
}
