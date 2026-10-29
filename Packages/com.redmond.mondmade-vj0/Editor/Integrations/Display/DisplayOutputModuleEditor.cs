using UnityEditor;
using UnityEngine;

namespace MondmadeVJ0.Display.Editor
{
    [CustomEditor(typeof(DisplayOutputModule))]
    public sealed class DisplayOutputModuleEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("二画面出力 Validation", EditorStyles.boldLabel);

            var module = (DisplayOutputModule)target;
            var issues = module.ValidateConfiguration();
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("基本設定は有効です。EditorではTarget Display index 1のGameビューでプレビューできます。物理ディスプレイへの出力はStandalone Playerで確認してください。", MessageType.Info);
                return;
            }

            foreach (var issue in issues)
            {
                var messageType = issue.Severity == DisplayOutputIssueSeverity.Error ? MessageType.Error : MessageType.Warning;
                EditorGUILayout.HelpBox(issue.Message, messageType);
            }
        }
    }
}
