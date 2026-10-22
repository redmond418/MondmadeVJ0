using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using MondmadeVJ0;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MondmadeVJ0.Editor
{
    public abstract class ComponentMemberOutputDrawer : PropertyDrawer
    {
        protected abstract Type ValueType { get; }
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight * 3f + 8f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var target = property.FindPropertyRelative("target");
            var member = property.FindPropertyRelative("memberName");
            var lineHeight = EditorGUIUtility.singleLineHeight;
            var targetRect = new Rect(position.x, position.y, position.width, lineHeight);
            EditorGUI.PropertyField(targetRect, target, new GUIContent(label.text + " Target"));
            var component = target.objectReferenceValue as Component;
            if (component == null)
            {
                EditorGUI.HelpBox(new Rect(position.x, targetRect.yMax + 2f, position.width, lineHeight * 2f + 2f), "Choose a Component to list writable " + ValueType.Name + " members.", MessageType.Info);
                EditorGUI.EndProperty();
                return;
            }

            var candidates = FindMembers(component.GetType(), ValueType);
            var labels = candidates.Select(candidate => candidate.Name).ToArray();
            var selected = Array.IndexOf(labels, member.stringValue);
            var memberRect = new Rect(position.x, targetRect.yMax + 2f, position.width, lineHeight);
            if (labels.Length == 0)
                EditorGUI.LabelField(memberRect, "Member", "No compatible public member found.");
            else
            {
                selected = EditorGUI.Popup(memberRect, "Member", selected < 0 ? 0 : selected, labels);
                if (selected >= 0 && selected < labels.Length) member.stringValue = labels[selected];
            }

            var buttonRect = new Rect(position.x, memberRect.yMax + 2f, position.width, lineHeight);
            using (new EditorGUI.DisabledScope(selected < 0 || selected >= labels.Length))
                if (GUI.Button(buttonRect, "Generate IL2CPP accessor"))
                    ComponentAccessorGenerator.Add(component.GetType(), labels[selected], ValueType);
            EditorGUI.EndProperty();
        }

        private static List<MemberInfo> FindMembers(Type componentType, Type valueType)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            var fields = componentType.GetFields(flags)
                .Where(field => field.FieldType == valueType && !field.IsInitOnly && !field.IsLiteral && !field.IsStatic)
                .Cast<MemberInfo>();
            var properties = componentType.GetProperties(flags)
                .Where(property => property.PropertyType == valueType && property.GetIndexParameters().Length == 0 && property.GetSetMethod(false) != null)
                .Cast<MemberInfo>();
            return fields.Concat(properties).GroupBy(member => member.Name, StringComparer.Ordinal).Select(group => group.First()).OrderBy(member => member.Name, StringComparer.Ordinal).ToList();
        }
    }

    [CustomPropertyDrawer(typeof(ComponentMemberFloatOutput))] public sealed class ComponentMemberFloatOutputDrawer : ComponentMemberOutputDrawer { protected override Type ValueType => typeof(float); }
    [CustomPropertyDrawer(typeof(ComponentMemberIntOutput))] public sealed class ComponentMemberIntOutputDrawer : ComponentMemberOutputDrawer { protected override Type ValueType => typeof(int); }
    [CustomPropertyDrawer(typeof(ComponentMemberBoolOutput))] public sealed class ComponentMemberBoolOutputDrawer : ComponentMemberOutputDrawer { protected override Type ValueType => typeof(bool); }
    [CustomPropertyDrawer(typeof(ComponentMemberVector2Output))] public sealed class ComponentMemberVector2OutputDrawer : ComponentMemberOutputDrawer { protected override Type ValueType => typeof(Vector2); }
    [CustomPropertyDrawer(typeof(ComponentMemberVector3Output))] public sealed class ComponentMemberVector3OutputDrawer : ComponentMemberOutputDrawer { protected override Type ValueType => typeof(Vector3); }
    [CustomPropertyDrawer(typeof(ComponentMemberVector4Output))] public sealed class ComponentMemberVector4OutputDrawer : ComponentMemberOutputDrawer { protected override Type ValueType => typeof(Vector4); }
    [CustomPropertyDrawer(typeof(ComponentMemberColorOutput))] public sealed class ComponentMemberColorOutputDrawer : ComponentMemberOutputDrawer { protected override Type ValueType => typeof(Color); }

    internal static class ComponentAccessorGenerator
    {
        private const string DirectoryPath = "Assets/MondmadeVJ0.Generated";
        private const string ManifestPath = DirectoryPath + "/accessors.json";
        private const string SourcePath = DirectoryPath + "/MondmadeVJ0GeneratedAccessors.cs";
        private static HashSet<string> cachedKeys;

        [Serializable]
        private sealed class Manifest { public List<Entry> entries = new List<Entry>(); }
        [Serializable]
        private sealed class Entry { public string componentType; public string memberName; public string valueType; }

        public static void Add(Type componentType, string memberName, Type valueType)
        {
            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType)) throw new ArgumentException("Accessor target must be a Component.");
            if (!componentType.IsPublic && !(componentType.IsNestedPublic && componentType.DeclaringType?.IsPublic == true))
            {
                EditorUtility.DisplayDialog("Accessor generation", "The component type must be public so generated Assembly-CSharp code can access it.", "OK");
                return;
            }
            if (componentType.ContainsGenericParameters || !Regex.IsMatch(memberName ?? string.Empty, @"^[_\p{L}][\p{L}\p{Nd}_]*$"))
            {
                EditorUtility.DisplayDialog("Accessor generation", "Generic component types and unsupported member names cannot be generated.", "OK");
                return;
            }

            var manifest = ReadManifest();
            if (!manifest.entries.Any(entry => entry.componentType == componentType.AssemblyQualifiedName && entry.memberName == memberName && entry.valueType == valueType.AssemblyQualifiedName))
                manifest.entries.Add(new Entry { componentType = componentType.AssemblyQualifiedName, memberName = memberName, valueType = valueType.AssemblyQualifiedName });
            cachedKeys = null;
            EnsureDirectory();
            System.IO.File.WriteAllText(ManifestPath, JsonUtility.ToJson(manifest, true));
            System.IO.File.WriteAllText(SourcePath, CreateSource(manifest));
            AssetDatabase.Refresh();
            Debug.Log($"[MondmadeVJ0] Generated IL2CPP accessor for {componentType.FullName}.{memberName} ({valueType.Name}).");
        }

        internal static HashSet<string> GeneratedKeys()
        {
            if (cachedKeys != null) return cachedKeys;
            cachedKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in ReadManifest().entries) cachedKeys.Add(Key(entry.componentType, entry.memberName, entry.valueType));
            return cachedKeys;
        }

        internal static string Key(string componentType, string memberName, string valueType) => componentType + "|" + memberName + "|" + valueType;

        private static Manifest ReadManifest()
        {
            if (!System.IO.File.Exists(ManifestPath)) return new Manifest();
            try { return JsonUtility.FromJson<Manifest>(System.IO.File.ReadAllText(ManifestPath)) ?? new Manifest(); }
            catch { return new Manifest(); }
        }

        private static void EnsureDirectory()
        {
            if (!AssetDatabase.IsValidFolder(DirectoryPath)) AssetDatabase.CreateFolder("Assets", "MondmadeVJ0.Generated");
        }

        private static string CreateSource(Manifest manifest)
        {
            var builder = new StringBuilder();
            builder.AppendLine("// Generated by Mondmade VJ0. Do not edit this file by hand.");
            builder.AppendLine("using UnityEngine;");
            builder.AppendLine("using MondmadeVJ0;");
            builder.AppendLine("namespace MondmadeVJ0.Generated");
            builder.AppendLine("{");
            builder.AppendLine("    internal static class MondmadeVJ0GeneratedAccessors");
            builder.AppendLine("    {");
            builder.AppendLine("        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]");
            builder.AppendLine("        private static void Register()");
            builder.AppendLine("        {");
            foreach (var entry in manifest.entries.OrderBy(value => value.componentType, StringComparer.Ordinal).ThenBy(value => value.memberName, StringComparer.Ordinal))
            {
                var componentType = Type.GetType(entry.componentType);
                var valueType = Type.GetType(entry.valueType);
                if (componentType == null || valueType == null || !TryGetWritableMember(componentType, entry.memberName, valueType, out _)) continue;
                var typeName = CSharpName(componentType);
                builder.Append("            ComponentAccessorRegistry.Register<").Append(CSharpName(valueType)).Append(">(typeof(").Append(typeName).Append("), \"").Append(entry.memberName).Append("\", (component, value) => ((").Append(typeName).Append(")component).").Append(entry.memberName).AppendLine(" = value);");
            }
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static bool TryGetWritableMember(Type componentType, string memberName, Type valueType, out MemberInfo member)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            member = (MemberInfo)componentType.GetField(memberName, flags);
            if (member is FieldInfo field) return field.FieldType == valueType && !field.IsInitOnly && !field.IsLiteral;
            var property = componentType.GetProperty(memberName, flags);
            if (property != null && property.PropertyType == valueType && property.GetIndexParameters().Length == 0 && property.GetSetMethod(false) != null)
            { member = property; return true; }
            member = null;
            return false;
        }

        private static string CSharpName(Type type)
        {
            if (type == typeof(float)) return "float";
            if (type == typeof(int)) return "int";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(Vector2)) return "Vector2";
            if (type == typeof(Vector3)) return "Vector3";
            if (type == typeof(Vector4)) return "Vector4";
            if (type == typeof(Color)) return "Color";
            if (type.IsArray) return CSharpName(type.GetElementType()) + "[]";
            return "global::" + (type.FullName ?? type.Name).Replace('+', '.');
        }
    }

    [BuildCallbackVersion(2)]
    internal sealed class ComponentAccessorBuildGuard : IPreprocessBuildWithReport, IProcessSceneWithReport
    {
        private static HashSet<string> generatedKeys;
        public int callbackOrder => 1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!BuildPipeline.isBuildingPlayer) return;
            generatedKeys = ComponentAccessorGenerator.GeneratedKeys();
            var missing = new HashSet<string>(StringComparer.Ordinal);
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                GameObject prefab = null;
                try
                {
                    prefab = PrefabUtility.LoadPrefabContents(path);
                    CollectMissing(prefab, generatedKeys, missing);
                }
                catch (Exception exception) { Debug.LogWarning($"[MondmadeVJ0] Could not inspect prefab '{path}' for generated accessors: {exception.Message}"); }
                finally { if (prefab != null) PrefabUtility.UnloadPrefabContents(prefab); }
            }

            if (missing.Count > 0)
                throw new BuildFailedException("Mondmade VJ0 requires generated IL2CPP Component accessors before this build:\n" + string.Join("\n", missing));
        }

        public void OnProcessScene(UnityEngine.SceneManagement.Scene scene, BuildReport report)
        {
            if (report == null || !BuildPipeline.isBuildingPlayer || generatedKeys == null) return;
            var missing = new HashSet<string>(StringComparer.Ordinal);
            foreach (var root in scene.GetRootGameObjects()) CollectMissing(root, generatedKeys, missing);
            if (missing.Count > 0)
                throw new BuildFailedException($"Mondmade VJ0 requires generated IL2CPP Component accessors in scene '{scene.path}':\n" + string.Join("\n", missing));
        }

        private static void CollectMissing(GameObject root, HashSet<string> generated, HashSet<string> missing)
        {
            foreach (var host in root.GetComponentsInChildren<RouteHost>(true))
            {
                foreach (var route in host.Routes)
                {
                    if (route == null || !route.IsEnabled) continue;
                    foreach (var node in MondmadeVJ0Validation.EnumerateNodes(route))
                    {
                        if (node == null) continue;
                        var type = node.GetType();
                        while (type != null && (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(ComponentMemberOutput<>))) type = type.BaseType;
                        if (type == null) continue;
                        var target = type.GetProperty("Target")?.GetValue(node) as Component;
                        var member = type.GetProperty("MemberName")?.GetValue(node) as string;
                        if (target == null || string.IsNullOrWhiteSpace(member)) continue;
                        var valueType = type.GetGenericArguments()[0];
                        var key = ComponentAccessorGenerator.Key(target.GetType().AssemblyQualifiedName, member, valueType.AssemblyQualifiedName);
                        if (!generated.Contains(key)) missing.Add($"{host.name}/{route.Name}: {target.GetType().Name}.{member} ({valueType.Name})");
                    }
                }
            }
        }
    }
}
