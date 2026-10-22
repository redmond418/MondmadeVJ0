using System;
using System.Collections.Generic;
using System.Linq;
using MondmadeVJ0;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MondmadeVJ0.Editor
{
    [CustomPropertyDrawer(typeof(AnimatorFloatOutput))]
    [CustomPropertyDrawer(typeof(AnimatorIntOutput))]
    [CustomPropertyDrawer(typeof(AnimatorBoolOutput))]
    [CustomPropertyDrawer(typeof(AnimatorTriggerOutput))]
    [CustomPropertyDrawer(typeof(AnimatorLayerWeightFloatOutput))]
    [CustomPropertyDrawer(typeof(AnimatorStateTriggerOutput))]
    public sealed class AnimatorOutputDrawer : PropertyDrawer
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
            var animator = property.FindPropertyRelative("target")?.objectReferenceValue as Animator;
            var y = position.y;
            foreach (var child in children)
            {
                var height = EditorGUI.GetPropertyHeight(child, true);
                var row = new Rect(position.x, y, position.width, height);
                if (child.name == "parameterName" && IsParameterOutput(property, out var expected))
                    DrawParameter(row, child, animator, expected);
                else if (child.name == "layerName")
                    DrawLayer(row, child, animator);
                else if (child.name == "layerIndex" && !string.IsNullOrEmpty(property.FindPropertyRelative("layerName")?.stringValue))
                    EditorGUI.LabelField(row, "Layer Index", "Selected by layer name");
                else if (child.name == "stateName")
                    DrawState(row, child, animator);
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

        private static bool IsParameterOutput(SerializedProperty property, out AnimatorControllerParameterType expected)
        {
            expected = AnimatorControllerParameterType.Float;
            var type = property.managedReferenceValue?.GetType();
            if (type == typeof(AnimatorFloatOutput)) expected = AnimatorControllerParameterType.Float;
            else if (type == typeof(AnimatorIntOutput)) expected = AnimatorControllerParameterType.Int;
            else if (type == typeof(AnimatorBoolOutput)) expected = AnimatorControllerParameterType.Bool;
            else if (type == typeof(AnimatorTriggerOutput)) expected = AnimatorControllerParameterType.Trigger;
            else return false;
            return true;
        }

        private static void DrawParameter(Rect rect, SerializedProperty property, Animator animator, AnimatorControllerParameterType expected)
        {
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                EditorGUI.PropertyField(rect, property, new GUIContent("Parameter"));
                return;
            }
            var names = animator.parameters.Where(parameter => parameter.type == expected).Select(parameter => parameter.name).ToList();
            DrawChoice(rect, property, "Parameter", names);
        }

        private static void DrawLayer(Rect rect, SerializedProperty property, Animator animator)
        {
            if (animator == null || animator.runtimeAnimatorController == null || animator.layerCount == 0)
            {
                EditorGUI.PropertyField(rect, property, new GUIContent("Layer"));
                return;
            }
            DrawChoice(rect, property, "Layer", Enumerable.Range(0, animator.layerCount).Select(animator.GetLayerName).ToList());
        }

        private static void DrawState(Rect rect, SerializedProperty property, Animator animator)
        {
            var controller = animator == null ? null : BaseController(animator.runtimeAnimatorController) as AnimatorController;
            var names = controller == null ? new List<string>() : ControllerStates(controller);
            if (names.Count == 0) EditorGUI.PropertyField(rect, property, new GUIContent("State Path"));
            else DrawChoice(rect, property, "State Path", names);
        }

        private static RuntimeAnimatorController BaseController(RuntimeAnimatorController controller)
        {
            while (controller is AnimatorOverrideController overrides) controller = overrides.runtimeAnimatorController;
            return controller;
        }

        private static List<string> ControllerStates(AnimatorController controller)
        {
            var result = new List<string>();
            foreach (var layer in controller.layers)
                AddStates(result, layer.stateMachine, layer.name);
            return result.Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToList();
        }

        private static void AddStates(List<string> result, AnimatorStateMachine machine, string path)
        {
            foreach (var child in machine.states) result.Add(path + "." + child.state.name);
            foreach (var child in machine.stateMachines) AddStates(result, child.stateMachine, path + "." + child.stateMachine.name);
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
