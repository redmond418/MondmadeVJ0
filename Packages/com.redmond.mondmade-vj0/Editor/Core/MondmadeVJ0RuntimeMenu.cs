using UnityEditor;
using UnityEngine;

namespace MondmadeVJ0.Editor
{
    internal static class MondmadeVJ0RuntimeMenu
    {
        [MenuItem("GameObject/Mondmade VJ0/Runtime", false, 10)]
        private static void CreateRuntime(MenuCommand command)
        {
            var gameObject = new GameObject("MondmadeVJ0 Runtime");
            Undo.RegisterCreatedObjectUndo(gameObject, "Create MondmadeVJ0 Runtime");
            GameObjectUtility.SetParentAndAlign(gameObject, command.context as GameObject);
            Undo.AddComponent<MondmadeVJ0Runtime>(gameObject);
            Selection.activeObject = gameObject;
        }
    }
}
