using System;
using System.Collections.Generic;
using System.Linq;
using MondmadeVJ0;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;

namespace MondmadeVJ0.Editor
{
    [CustomPropertyDrawer(typeof(VFXFloatOutput))]
    [CustomPropertyDrawer(typeof(VFXIntOutput))]
    [CustomPropertyDrawer(typeof(VFXUIntOutput))]
    [CustomPropertyDrawer(typeof(VFXBoolOutput))]
    [CustomPropertyDrawer(typeof(VFXVector2Output))]
    [CustomPropertyDrawer(typeof(VFXVector3Output))]
    [CustomPropertyDrawer(typeof(VFXVector4Output))]
    [CustomPropertyDrawer(typeof(VFXColorOutput))]
    [CustomPropertyDrawer(typeof(VFXMatrixOutput))]
    [CustomPropertyDrawer(typeof(VFXTextureOutput))]
    [CustomPropertyDrawer(typeof(VFXTextureIndexOutput))]
    [CustomPropertyDrawer(typeof(VFXTriggerEventOutput))]
    public sealed class VFXOutputDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var height = 0f;
            foreach (var child in DirectChildren(property))
            {
                if (child.name == "nodeId") continue;
                height += EditorGUI.GetPropertyHeight(child, true) + EditorGUIUtility.standardVerticalSpacing;
            }
            return Mathf.Max(EditorGUIUtility.singleLineHeight, height);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var children = DirectChildren(property).Where(child => child.name != "nodeId").ToArray();
            var target = property.FindPropertyRelative("target")?.objectReferenceValue as VisualEffect;
            var outputType = property.managedReferenceValue?.GetType();
            var y = position.y;
            foreach (var child in children)
            {
                var height = EditorGUI.GetPropertyHeight(child, true);
                var row = new Rect(position.x, y, position.width, height);
                if (child.name == "propertyName")
                    DrawPropertyChoice(row, child, target, outputType);
                else if (child.name == "eventName")
                    DrawEventChoice(row, child, target);
                else
                    EditorGUI.PropertyField(row, child, true);
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

        private static void DrawPropertyChoice(Rect rect, SerializedProperty property, VisualEffect target, Type outputType)
        {
            if (target == null || target.visualEffectAsset == null)
            {
                EditorGUI.PropertyField(rect, property, new GUIContent("Exposed Property"));
                return;
            }
            var exposed = new List<VFXExposedProperty>();
            target.visualEffectAsset.GetExposedProperties(exposed);
            var names = exposed.Select(value => value.name)
                .Where(name => Supports(target, name, outputType))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            DrawChoice(rect, property, "Exposed Property", names);
        }

        private static bool Supports(VisualEffect effect, string name, Type outputType)
        {
            if (outputType == typeof(VFXFloatOutput)) return effect.HasFloat(name);
            if (outputType == typeof(VFXIntOutput)) return effect.HasInt(name);
            if (outputType == typeof(VFXUIntOutput)) return effect.HasUInt(name);
            if (outputType == typeof(VFXBoolOutput)) return effect.HasBool(name);
            if (outputType == typeof(VFXVector2Output)) return effect.HasVector2(name);
            if (outputType == typeof(VFXVector3Output)) return effect.HasVector3(name);
            if (outputType == typeof(VFXVector4Output) || outputType == typeof(VFXColorOutput)) return effect.HasVector4(name);
            if (outputType == typeof(VFXMatrixOutput)) return effect.HasMatrix4x4(name);
            if (outputType == typeof(VFXTextureOutput) || outputType == typeof(VFXTextureIndexOutput)) return effect.HasTexture(name);
            return false;
        }

        private static void DrawEventChoice(Rect rect, SerializedProperty property, VisualEffect target)
        {
            if (target == null || target.visualEffectAsset == null)
            {
                EditorGUI.PropertyField(rect, property, new GUIContent("Event"));
                return;
            }
            var names = new List<string>();
            target.visualEffectAsset.GetEvents(names);
            DrawChoice(rect, property, "Event", names.Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToList());
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
}
