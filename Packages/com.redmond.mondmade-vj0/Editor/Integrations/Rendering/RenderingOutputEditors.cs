using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MondmadeVJ0;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MondmadeVJ0.Editor
{
    [CustomPropertyDrawer(typeof(RendererFloatOutput))]
    [CustomPropertyDrawer(typeof(RendererIntOutput))]
    [CustomPropertyDrawer(typeof(RendererVectorOutput))]
    [CustomPropertyDrawer(typeof(RendererColorOutput))]
    [CustomPropertyDrawer(typeof(RendererTextureOutput))]
    [CustomPropertyDrawer(typeof(RendererTextureIndexOutput))]
    [CustomPropertyDrawer(typeof(MaterialFloatOutput))]
    [CustomPropertyDrawer(typeof(MaterialIntOutput))]
    [CustomPropertyDrawer(typeof(MaterialVectorOutput))]
    [CustomPropertyDrawer(typeof(MaterialColorOutput))]
    [CustomPropertyDrawer(typeof(MaterialTextureOutput))]
    public sealed class ShaderOutputDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => DirectChildren(property)
            .Where(child => child.name != "nodeId")
            .Sum(child => EditorGUI.GetPropertyHeight(child, true) + EditorGUIUtility.standardVerticalSpacing);

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var children = DirectChildren(property).Where(child => child.name != "nodeId").ToArray();
            var material = ResolveMaterial(property);
            var outputType = property.managedReferenceValue?.GetType();
            var y = position.y;
            foreach (var child in children)
            {
                var height = EditorGUI.GetPropertyHeight(child, true);
                var row = new Rect(position.x, y, position.width, height);
                if (child.name == "propertyName") DrawShaderProperty(row, child, material, outputType);
                else EditorGUI.PropertyField(row, child, true);
                y += height + EditorGUIUtility.standardVerticalSpacing;
            }
            EditorGUI.EndProperty();
        }

        private static IEnumerable<SerializedProperty> DirectChildren(SerializedProperty parent)
        {
            var iterator = parent.Copy();
            var end = iterator.GetEndProperty();
            var enter = true;
            while (iterator.NextVisible(enter) && !SerializedProperty.EqualContents(iterator, end))
            {
                enter = false;
                if (iterator.depth == parent.depth + 1) yield return iterator.Copy();
            }
        }

        private static Material ResolveMaterial(SerializedProperty property)
        {
            var target = property.FindPropertyRelative("target")?.objectReferenceValue;
            if (target is Material material) return material;
            if (target is Renderer renderer)
            {
                var index = property.FindPropertyRelative("materialIndex")?.intValue ?? 0;
                var materials = renderer.sharedMaterials;
                return index >= 0 && index < materials.Length ? materials[index] : null;
            }
            return null;
        }

        private static void DrawShaderProperty(Rect rect, SerializedProperty property, Material material, Type outputType)
        {
            if (material == null || material.shader == null)
            {
                EditorGUI.PropertyField(rect, property, new GUIContent("Shader Property"));
                return;
            }
            var names = new List<string>();
            var shader = material.shader;
            for (var i = 0; i < shader.GetPropertyCount(); i++)
            {
                var type = shader.GetPropertyType(i);
                if (IsCompatible(type, outputType)) names.Add(shader.GetPropertyName(i));
            }
            DrawChoice(rect, property, "Shader Property", names);
        }

        private static bool IsCompatible(ShaderPropertyType shaderType, Type outputType)
        {
            if (outputType == typeof(RendererFloatOutput) || outputType == typeof(MaterialFloatOutput)) return shaderType == ShaderPropertyType.Float || shaderType == ShaderPropertyType.Range;
            if (outputType == typeof(RendererIntOutput) || outputType == typeof(MaterialIntOutput)) return shaderType == ShaderPropertyType.Int;
            if (outputType == typeof(RendererVectorOutput) || outputType == typeof(MaterialVectorOutput)) return shaderType == ShaderPropertyType.Vector || shaderType == ShaderPropertyType.Color;
            if (outputType == typeof(RendererColorOutput) || outputType == typeof(MaterialColorOutput)) return shaderType == ShaderPropertyType.Color || shaderType == ShaderPropertyType.Vector;
            if (outputType == typeof(RendererTextureOutput) || outputType == typeof(RendererTextureIndexOutput) || outputType == typeof(MaterialTextureOutput)) return shaderType == ShaderPropertyType.Texture;
            return false;
        }

        private static void DrawChoice(Rect rect, SerializedProperty property, string label, List<string> names)
        {
            if (names.Count == 0)
            {
                EditorGUI.PropertyField(rect, property, new GUIContent(label));
                return;
            }
            var selected = names.IndexOf(property.stringValue);
            if (selected < 0 && !string.IsNullOrEmpty(property.stringValue))
            {
                var choices = new[] { "Missing: " + property.stringValue }.Concat(names).ToArray();
                var choice = EditorGUI.Popup(rect, label, 0, choices);
                if (choice > 0 && choice - 1 < names.Count) property.stringValue = names[choice - 1];
            }
            else
            {
                selected = EditorGUI.Popup(rect, label, selected, names.ToArray());
                if (selected >= 0 && selected < names.Count) property.stringValue = names[selected];
            }
        }
    }

    [CustomPropertyDrawer(typeof(VolumeFloatOutput))]
    [CustomPropertyDrawer(typeof(VolumeIntOutput))]
    [CustomPropertyDrawer(typeof(VolumeBoolOutput))]
    [CustomPropertyDrawer(typeof(VolumeColorOutput))]
    [CustomPropertyDrawer(typeof(VolumeVector2Output))]
    [CustomPropertyDrawer(typeof(VolumeVectorOutput))]
    [CustomPropertyDrawer(typeof(VolumeVector4Output))]
    public sealed class VolumeOutputDrawer : PropertyDrawer
    {
        private static readonly Type VolumeParameterDefinition = typeof(VolumeParameter<>);

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => ShaderOutputDrawerHeight(property);

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var children = DirectChildren(property).Where(child => child.name != "nodeId").ToArray();
            var componentProperty = property.FindPropertyRelative("componentTypeName");
            var parameterProperty = property.FindPropertyRelative("parameterName");
            var type = property.managedReferenceValue?.GetType();
            var expected = type?.BaseType?.IsGenericType == true ? type.BaseType.GetGenericArguments()[0] : null;
            var y = position.y;
            foreach (var child in children)
            {
                var height = EditorGUI.GetPropertyHeight(child, true);
                var row = new Rect(position.x, y, position.width, height);
                if (child.name == "componentTypeName") DrawComponentChoice(row, child);
                else if (child.name == "parameterName") DrawParameterChoice(row, child, componentProperty?.stringValue, expected);
                else EditorGUI.PropertyField(row, child, true);
                y += height + EditorGUIUtility.standardVerticalSpacing;
            }
            EditorGUI.EndProperty();
        }

        private static float ShaderOutputDrawerHeight(SerializedProperty property) => DirectChildren(property)
            .Where(child => child.name != "nodeId")
            .Sum(child => EditorGUI.GetPropertyHeight(child, true) + EditorGUIUtility.standardVerticalSpacing);

        private static IEnumerable<SerializedProperty> DirectChildren(SerializedProperty parent)
        {
            var iterator = parent.Copy();
            var end = iterator.GetEndProperty();
            var enter = true;
            while (iterator.NextVisible(enter) && !SerializedProperty.EqualContents(iterator, end))
            {
                enter = false;
                if (iterator.depth == parent.depth + 1) yield return iterator.Copy();
            }
        }

        private static void DrawComponentChoice(Rect rect, SerializedProperty property)
        {
            var types = TypeCache.GetTypesDerivedFrom<VolumeComponent>()
                .Where(candidate => !candidate.IsAbstract && !candidate.ContainsGenericParameters)
                .OrderBy(candidate => candidate.FullName, StringComparer.Ordinal)
                .ToList();
            var labels = types.Select(candidate => candidate.FullName).ToList();
            var selected = types.FindIndex(candidate => string.Equals(candidate.AssemblyQualifiedName, property.stringValue, StringComparison.Ordinal) || string.Equals(candidate.FullName, property.stringValue, StringComparison.Ordinal));
            if (selected < 0 && !string.IsNullOrEmpty(property.stringValue))
            {
                var choices = new[] { "Missing: " + property.stringValue }.Concat(labels).ToArray();
                var choice = EditorGUI.Popup(rect, "Volume Component", 0, choices);
                if (choice > 0 && choice - 1 < types.Count) property.stringValue = types[choice - 1].AssemblyQualifiedName;
            }
            else if (types.Count == 0) EditorGUI.PropertyField(rect, property, new GUIContent("Volume Component"));
            else
            {
                selected = EditorGUI.Popup(rect, "Volume Component", selected, labels.ToArray());
                if (selected >= 0 && selected < types.Count) property.stringValue = types[selected].AssemblyQualifiedName;
            }
        }

        private static void DrawParameterChoice(Rect rect, SerializedProperty property, string componentName, Type expected)
        {
            var componentType = ResolveType(componentName);
            if (componentType == null || expected == null)
            {
                EditorGUI.PropertyField(rect, property, new GUIContent("Parameter"));
                return;
            }
            var parameterType = VolumeParameterDefinition.MakeGenericType(expected);
            var names = componentType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field => parameterType.IsAssignableFrom(field.FieldType) && (field.IsPublic || field.IsDefined(typeof(SerializeField), true)))
                .Select(field => field.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            if (names.Count == 0)
            {
                EditorGUI.PropertyField(rect, property, new GUIContent("Parameter"));
                return;
            }
            var selected = names.IndexOf(property.stringValue);
            if (selected < 0 && !string.IsNullOrEmpty(property.stringValue))
            {
                var choices = new[] { "Missing: " + property.stringValue }.Concat(names).ToArray();
                var choice = EditorGUI.Popup(rect, "Parameter", 0, choices);
                if (choice > 0 && choice - 1 < names.Count) property.stringValue = names[choice - 1];
            }
            else
            {
                selected = EditorGUI.Popup(rect, "Parameter", selected, names.ToArray());
                if (selected >= 0 && selected < names.Count) property.stringValue = names[selected];
            }
        }

        private static Type ResolveType(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var type = Type.GetType(name, false);
            if (type != null) return type;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if ((type = assembly.GetType(name, false)) != null) return type;
            return null;
        }
    }
}
