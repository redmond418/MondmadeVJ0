using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MondmadeVJ0.Display.Editor
{
    internal static class DisplayOutputSetupTool
    {
        private const string MenuPath = "Tools/Mondmade VJ0/Display Output/Auto Setup";
        private const string TmpEssentialsPackageName = "TMP Essential Resources";
        private static bool importingTmpEssentials;

        [MenuItem(MenuPath, false, 70)]
        private static void AutoSetup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Display Output", "Play Modeを終了してから実行してください。", "OK");
                return;
            }

            var settings = TMP_Settings.GetSettings();
            if (settings == null || TMP_Settings.GetFontAsset() == null)
            {
                ImportTmpEssentialsAndContinue();
                return;
            }

            ApplySetup();
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateAutoSetup()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private static void ImportTmpEssentialsAndContinue()
        {
            if (importingTmpEssentials) return;
            importingTmpEssentials = true;
            AssetDatabase.importPackageCompleted += OnTmpPackageImported;
            TMP_PackageResourceImporter.ImportResources(true, false, false);
        }

        private static void OnTmpPackageImported(string packageName)
        {
            if (packageName != TmpEssentialsPackageName) return;
            AssetDatabase.importPackageCompleted -= OnTmpPackageImported;
            importingTmpEssentials = false;
            EditorApplication.delayCall += AutoSetupAfterTmpImport;
        }

        private static void AutoSetupAfterTmpImport()
        {
            if (TMP_Settings.GetSettings() == null || TMP_Settings.GetFontAsset() == null)
            {
                EditorUtility.DisplayDialog("Display Output", "TMP EssentialsのFont Assetを読み込めませんでした。TextMeshProのリソースを確認してください。", "OK");
                return;
            }
            ApplySetup();
        }

        private static void ApplySetup()
        {
            var mainCamera = FindMainCamera();
            if (mainCamera == null)
            {
                EditorUtility.DisplayDialog("Display Output", "TagがMainCameraのCameraがSceneにありません。MainCameraを追加してから再実行してください。", "OK");
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Set Up Display Output");

            var scene = SceneManager.GetActiveScene();
            var module = FindModule(scene);
            var runtime = FindRuntime(scene);
            if (runtime == null)
                runtime = CreateRuntime(scene);
            RemoveLegacyStatusText(module, scene);

            var outputCanvas = GetModuleReference<Canvas>(module, "outputCanvas") ?? FindOutputCanvas(scene);
            var outputImage = GetModuleReference<RawImage>(module, "outputImage");
            if (outputCanvas == null)
                outputCanvas = CreateOutputCanvas(scene, out outputImage);
            else
            {
                ConfigureOutputCanvas(outputCanvas);
                if (outputImage == null)
                    outputImage = FindOutputImage(outputCanvas);
                if (outputImage == null)
                    outputImage = CreateOutputImage(outputCanvas.transform);
            }
            ConfigureOutputImage(outputCanvas, outputImage);

            var operatorCanvas = FindOperatorCanvas(scene, module, outputCanvas);
            if (operatorCanvas == null)
                operatorCanvas = CreateOperatorCanvas(scene);
            ConfigureOperatorCanvas(operatorCanvas);
            EnsureEventSystem(scene);

            var oldDropdown = GetModuleReference<TMP_Dropdown>(module, "displayDropdown");
            if (oldDropdown == null)
                oldDropdown = FindComponentInCanvas<TMP_Dropdown>(operatorCanvas);
            var legacyDropdown = oldDropdown == null ? FindComponentInCanvas<Dropdown>(operatorCanvas) : null;
            oldDropdown = EnsureTmpDropdown(operatorCanvas, oldDropdown, legacyDropdown);

            var previewImage = GetModuleReference<RawImage>(module, "previewImage");
            if (!IsInsideCanvas(previewImage, operatorCanvas))
                previewImage = FindPreviewImage(operatorCanvas, outputCanvas, oldDropdown);
            if (previewImage == null)
                previewImage = CreatePreviewImage(operatorCanvas.transform);
            EnsurePreviewAspectFrame(previewImage);

            var uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer < 0) uiLayer = 5;
            SetLayerRecursively(operatorCanvas.gameObject, uiLayer);
            SetLayerRecursively(outputCanvas.gameObject, uiLayer);

            EnsureDisplaySurfaceCamera(scene, 0, mainCamera);
            EnsureDisplaySurfaceCamera(scene, 1, mainCamera);

            if (module == null)
            {
                module = Undo.AddComponent<DisplayOutputModule>(runtime.gameObject);
                if (module == null)
                {
                    Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
                    EditorUtility.DisplayDialog("Display Output", "DisplayOutputModuleを追加できませんでした。uGUI / TextMeshPro Integrationが有効か確認してください。", "OK");
                    return;
                }
            }

            SetModuleReferences(module, mainCamera, outputCanvas, oldDropdown, outputImage, previewImage, uiLayer);
            RegisterRuntimeModule(runtime, module);

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = runtime.gameObject;
            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
            EditorUtility.DisplayDialog(
                "Display Output",
                "セットアップしました。EditorではGameビューのDisplay 2（Target Display index 1）を選ぶと副画面プレビューを確認できます。Standalone Playerでは画面1のTMP_Dropdownから出力先を選びます。",
                "OK");
        }

        private static Camera FindMainCamera()
        {
            var taggedCamera = Camera.main;
            if (taggedCamera != null && taggedCamera.gameObject.scene == SceneManager.GetActiveScene())
                return taggedCamera;
            foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (camera.gameObject.scene == SceneManager.GetActiveScene() && camera.CompareTag("MainCamera"))
                    return camera;
            return null;
        }

        private static DisplayOutputModule FindModule(Scene scene)
        {
            foreach (var module in UnityEngine.Object.FindObjectsByType<DisplayOutputModule>(FindObjectsSortMode.None))
                if (module.gameObject.scene == scene)
                    return module;
            return null;
        }

        private static MondmadeVJ0Runtime FindRuntime(Scene scene)
        {
            foreach (var runtime in UnityEngine.Object.FindObjectsByType<MondmadeVJ0Runtime>(FindObjectsSortMode.None))
                if (runtime.gameObject.scene == scene)
                    return runtime;
            return null;
        }

        private static MondmadeVJ0Runtime CreateRuntime(Scene scene)
        {
            var gameObject = new GameObject("Mondmade VJ0 Runtime");
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            Undo.RegisterCreatedObjectUndo(gameObject, "Create Mondmade VJ0 Runtime");
            return Undo.AddComponent<MondmadeVJ0Runtime>(gameObject);
        }

        private static Canvas FindOutputCanvas(Scene scene)
        {
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (canvas.gameObject.scene == scene && canvas.name == "Display Output Canvas")
                    return canvas;
            return null;
        }

        private static Canvas CreateOutputCanvas(Scene scene, out RawImage outputImage)
        {
            var gameObject = new GameObject("Display Output Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            Undo.RegisterCreatedObjectUndo(gameObject, "Create Display Output Canvas");
            var canvas = gameObject.GetComponent<Canvas>();
            ConfigureOutputCanvas(canvas);
            var scaler = gameObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            outputImage = CreateOutputImage(gameObject.transform);
            return canvas;
        }

        private static void ConfigureOutputCanvas(Canvas canvas)
        {
            Undo.RecordObject(canvas, "Configure Display Output Canvas");
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.targetDisplay = 1;
            canvas.enabled = true;
        }

        private static RawImage FindOutputImage(Canvas canvas)
        {
            foreach (var image in canvas.GetComponentsInChildren<RawImage>(true))
                if (image.transform != canvas.transform)
                    return image;
            return null;
        }

        private static RawImage CreateOutputImage(Transform parent)
        {
            var gameObject = new GameObject("Output Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            Undo.RegisterCreatedObjectUndo(gameObject, "Create Output RawImage");
            Undo.SetTransformParent(gameObject.transform, parent, "Parent Output RawImage");
            var rect = gameObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var image = gameObject.GetComponent<RawImage>();
            image.raycastTarget = false;
            return image;
        }

        private static void ConfigureOutputImage(Canvas outputCanvas, RawImage image)
        {
            if (image.GetComponentInParent<Canvas>() != outputCanvas)
                Undo.SetTransformParent(image.transform, outputCanvas.transform, "Move Output RawImage To Canvas");
            Undo.RecordObject(image, "Configure Output RawImage");
            var rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            image.raycastTarget = false;
        }

        private static Canvas FindOperatorCanvas(Scene scene, DisplayOutputModule module, Canvas outputCanvas)
        {
            var moduleDropdown = GetModuleReference<TMP_Dropdown>(module, "displayDropdown");
            if (moduleDropdown == null)
            {
                var serialized = module == null ? null : new SerializedObject(module);
                var oldDropdownProperty = serialized?.FindProperty("displayDropdown");
                moduleDropdown = oldDropdownProperty?.objectReferenceValue as TMP_Dropdown;
            }
            var moduleCanvas = moduleDropdown != null ? moduleDropdown.GetComponentInParent<Canvas>() : null;
            if (moduleCanvas != null && moduleCanvas != outputCanvas && moduleCanvas.gameObject.scene == scene)
                return moduleCanvas;

            if (Selection.activeGameObject != null)
            {
                var selectedCanvas = Selection.activeGameObject.GetComponentInParent<Canvas>();
                if (selectedCanvas != null && selectedCanvas != outputCanvas && selectedCanvas.gameObject.scene == scene)
                    return selectedCanvas;
            }

            Canvas fallback = null;
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (canvas.gameObject.scene != scene || canvas == outputCanvas) continue;
                if (canvas.targetDisplay != 0 || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                if (canvas.GetComponentInChildren<TMP_Dropdown>(true) != null || canvas.GetComponentInChildren<Dropdown>(true) != null)
                    return canvas;
                if (fallback == null) fallback = canvas;
            }
            return fallback;
        }

        private static Canvas CreateOperatorCanvas(Scene scene)
        {
            var gameObject = new GameObject("Operator Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            Undo.RegisterCreatedObjectUndo(gameObject, "Create Operator Canvas");
            var canvas = gameObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.targetDisplay = 0;
            var scaler = gameObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            return canvas;
        }

        private static void ConfigureOperatorCanvas(Canvas canvas)
        {
            Undo.RecordObject(canvas, "Configure Operator Canvas");
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.targetDisplay = 0;
            if (canvas.GetComponent<GraphicRaycaster>() == null)
                Undo.AddComponent<GraphicRaycaster>(canvas.gameObject);
        }

        private static void EnsureEventSystem(Scene scene)
        {
            foreach (var eventSystem in UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
                if (eventSystem.gameObject.scene == scene)
                    return;

            var gameObject = new GameObject("EventSystem", typeof(EventSystem));
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            Undo.RegisterCreatedObjectUndo(gameObject, "Create EventSystem");
            var inputModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputModuleType != null && typeof(BaseInputModule).IsAssignableFrom(inputModuleType))
                Undo.AddComponent(gameObject, inputModuleType);
            else
                Undo.AddComponent<StandaloneInputModule>(gameObject);
        }

        private static T FindComponentInCanvas<T>(Canvas canvas) where T : Component
        {
            foreach (var component in canvas.GetComponentsInChildren<T>(true))
                if (component.transform != canvas.transform)
                    return component;
            return null;
        }

        private static TMP_Dropdown EnsureTmpDropdown(Canvas canvas, TMP_Dropdown current, Dropdown legacy)
        {
            if (current != null)
            {
                if (current.GetComponentInParent<Canvas>() != canvas)
                    Undo.SetTransformParent(current.transform, canvas.transform, "Move Display Dropdown To Operator Canvas");
                ConfigureDropdownCanvas(current);
                return current;
            }

            RectTransform sourceRect = legacy != null ? legacy.GetComponent<RectTransform>() : null;
            Transform parent = sourceRect != null ? sourceRect.parent : canvas.transform;
            var siblingIndex = sourceRect != null ? sourceRect.GetSiblingIndex() : parent.childCount;
            var dropdownObject = TMP_DefaultControls.CreateDropdown(GetTmpControlResources());
            dropdownObject.name = "Display Output Dropdown (TMP)";
            Undo.RegisterCreatedObjectUndo(dropdownObject, "Create TMP Display Dropdown");
            Undo.SetTransformParent(dropdownObject.transform, parent, "Parent TMP Display Dropdown");
            dropdownObject.transform.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, parent.childCount - 1));
            var dropdownRect = dropdownObject.GetComponent<RectTransform>();
            if (sourceRect != null)
                CopyRectTransformLayout(sourceRect, dropdownRect);
            else
            {
                dropdownRect.anchorMin = new Vector2(0, 1);
                dropdownRect.anchorMax = new Vector2(0, 1);
                dropdownRect.pivot = new Vector2(0, 1);
                dropdownRect.anchoredPosition = new Vector2(16, -16);
                dropdownRect.sizeDelta = new Vector2(280, 40);
            }

            var result = dropdownObject.GetComponent<TMP_Dropdown>();
            var defaultFont = TMP_Settings.GetFontAsset();
            if (defaultFont != null)
            {
                result.captionText.font = defaultFont;
                result.itemText.font = defaultFont;
            }
            SetLayerRecursively(dropdownObject, LayerMask.NameToLayer("UI") < 0 ? 5 : LayerMask.NameToLayer("UI"));
            if (legacy != null)
                Undo.DestroyObjectImmediate(legacy.gameObject);
            ConfigureDropdownCanvas(result);
            return result;
        }

        private static TMP_DefaultControls.Resources GetTmpControlResources()
        {
            return new TMP_DefaultControls.Resources
            {
                standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
                background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
                inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
                knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
                checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
                dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
                mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd")
            };
        }

        private static void ConfigureDropdownCanvas(TMP_Dropdown dropdown)
        {
            Undo.RecordObject(dropdown, "Configure Display Dropdown");
            var canvas = dropdown.GetComponentInParent<Canvas>();
            if (canvas != null) ConfigureOperatorCanvas(canvas);
        }

        private static RawImage FindPreviewImage(Canvas operatorCanvas, Canvas outputCanvas, TMP_Dropdown dropdown)
        {
            foreach (var image in operatorCanvas.GetComponentsInChildren<RawImage>(true))
            {
                if (image.transform.IsChildOf(dropdown.transform)) continue;
                if (image.GetComponentInParent<Canvas>() == outputCanvas) continue;
                return image;
            }
            return null;
        }

        private static bool IsInsideCanvas(RawImage image, Canvas canvas)
        {
            return image != null && image.GetComponentInParent<Canvas>() == canvas;
        }

        private static RawImage CreatePreviewImage(Transform operatorCanvas)
        {
            var frame = new GameObject("Display Preview Frame", typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(frame, "Create Display Preview Frame");
            Undo.SetTransformParent(frame.transform, operatorCanvas, "Parent Display Preview Frame");
            var frameRect = frame.GetComponent<RectTransform>();
            frameRect.anchorMin = new Vector2(0.5f, 0.5f);
            frameRect.anchorMax = new Vector2(0.5f, 0.5f);
            frameRect.pivot = new Vector2(0.5f, 0.5f);
            frameRect.anchoredPosition = Vector2.zero;
            frameRect.sizeDelta = new Vector2(640, 360);
            var background = frame.GetComponent<Image>();
            background.color = new Color(0.08f, 0.08f, 0.08f, 1f);
            background.raycastTarget = false;

            var imageObject = new GameObject("Preview Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(AspectRatioFitter));
            Undo.RegisterCreatedObjectUndo(imageObject, "Create Display Preview RawImage");
            Undo.SetTransformParent(imageObject.transform, frame.transform, "Parent Display Preview RawImage");
            ConfigurePreviewRawImage(imageObject.GetComponent<RawImage>());
            return imageObject.GetComponent<RawImage>();
        }

        private static void EnsurePreviewAspectFrame(RawImage previewImage)
        {
            if (previewImage.GetComponent<AspectRatioFitter>() != null
                && previewImage.transform.parent != null
                && previewImage.transform.parent.name == "Display Preview Frame")
            {
                var existingFitter = previewImage.GetComponent<AspectRatioFitter>();
                Undo.RecordObject(existingFitter, "Configure Preview Aspect Ratio");
                existingFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                existingFitter.aspectRatio = 16f / 9f;
                Undo.RecordObject(previewImage, "Configure Preview RawImage");
                previewImage.raycastTarget = false;
                return;
            }

            var originalRect = previewImage.rectTransform;
            var originalParent = originalRect.parent;
            var originalSiblingIndex = originalRect.GetSiblingIndex();
            var frame = new GameObject("Display Preview Frame", typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(frame, "Create Display Preview Frame");
            Undo.SetTransformParent(frame.transform, originalParent, "Parent Display Preview Frame");
            var frameRect = frame.GetComponent<RectTransform>();
            CopyRectTransformLayout(originalRect, frameRect);
            frameRect.SetSiblingIndex(originalSiblingIndex);
            var background = frame.GetComponent<Image>();
            background.color = new Color(0.08f, 0.08f, 0.08f, 1f);
            background.raycastTarget = false;

            Undo.RecordObject(originalRect, "Fit Preview Image Into Frame");
            Undo.SetTransformParent(originalRect, frame.transform, "Parent Preview Image");
            originalRect.anchorMin = Vector2.zero;
            originalRect.anchorMax = Vector2.one;
            originalRect.pivot = new Vector2(0.5f, 0.5f);
            originalRect.anchoredPosition = Vector2.zero;
            originalRect.sizeDelta = Vector2.zero;
            originalRect.offsetMin = Vector2.zero;
            originalRect.offsetMax = Vector2.zero;
            originalRect.localRotation = Quaternion.identity;
            originalRect.localScale = Vector3.one;
            ConfigurePreviewRawImage(previewImage);
        }

        private static void ConfigurePreviewRawImage(RawImage image)
        {
            var fitter = image.GetComponent<AspectRatioFitter>();
            if (fitter == null) fitter = Undo.AddComponent<AspectRatioFitter>(image.gameObject);
            Undo.RecordObject(fitter, "Configure Preview Aspect Ratio");
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 16f / 9f;
            Undo.RecordObject(image, "Configure Preview RawImage");
            image.raycastTarget = false;
        }

        private static void CopyRectTransformLayout(RectTransform source, RectTransform destination)
        {
            destination.anchorMin = source.anchorMin;
            destination.anchorMax = source.anchorMax;
            destination.pivot = source.pivot;
            destination.anchoredPosition3D = source.anchoredPosition3D;
            destination.sizeDelta = source.sizeDelta;
            destination.offsetMin = source.offsetMin;
            destination.offsetMax = source.offsetMax;
            destination.localRotation = source.localRotation;
            destination.localScale = source.localScale;
        }

        private static void EnsureDisplaySurfaceCamera(Scene scene, int displayIndex, Camera mainCamera)
        {
            var objectName = $"Display Output Surface {displayIndex}";
            Camera displayCamera = null;
            foreach (var candidate in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (candidate.gameObject.scene != scene || candidate == mainCamera) continue;
                if (candidate.gameObject.name == objectName)
                {
                    displayCamera = candidate;
                    break;
                }
                if (displayCamera == null && candidate.targetDisplay == displayIndex && candidate.cullingMask == 0)
                    displayCamera = candidate;
            }

            if (displayCamera == null)
            {
                var gameObject = new GameObject(objectName, typeof(Camera));
                SceneManager.MoveGameObjectToScene(gameObject, scene);
                Undo.RegisterCreatedObjectUndo(gameObject, "Create Display Surface Camera");
                displayCamera = gameObject.GetComponent<Camera>();
            }
            else if (displayCamera.gameObject.name != objectName)
            {
                Undo.RecordObject(displayCamera.gameObject, "Name Display Surface Camera");
                displayCamera.gameObject.name = objectName;
            }

            Undo.RecordObject(displayCamera, "Configure Display Surface Camera");
            displayCamera.enabled = true;
            displayCamera.targetDisplay = displayIndex;
            displayCamera.cullingMask = 0;
            displayCamera.clearFlags = CameraClearFlags.SolidColor;
            displayCamera.backgroundColor = Color.black;
            displayCamera.depth = -100;
            var listener = displayCamera.GetComponent<AudioListener>();
            if (listener != null)
            {
                Undo.RecordObject(listener, "Disable Display Surface AudioListener");
                listener.enabled = false;
            }
        }

        private static void SetModuleReferences(
            DisplayOutputModule module,
            Camera outputCamera,
            Canvas outputCanvas,
            TMP_Dropdown displayDropdown,
            RawImage outputImage,
            RawImage previewImage,
            int uiLayer)
        {
            Undo.RecordObject(module, "Configure DisplayOutputModule");
            var serialized = new SerializedObject(module);
            serialized.Update();
            serialized.FindProperty("outputCamera").objectReferenceValue = outputCamera;
            serialized.FindProperty("outputCanvas").objectReferenceValue = outputCanvas;
            serialized.FindProperty("displayDropdown").objectReferenceValue = displayDropdown;
            serialized.FindProperty("outputImage").objectReferenceValue = outputImage;
            serialized.FindProperty("previewImage").objectReferenceValue = previewImage;
            serialized.FindProperty("preferredDisplayIndex").intValue = 1;
            serialized.FindProperty("operatorUiLayerMask").intValue = 1 << uiLayer;
            serialized.FindProperty("editorPreviewResolution").vector2IntValue = new Vector2Int(1920, 1080);
            serialized.ApplyModifiedProperties();
        }

        private static void RemoveLegacyStatusText(DisplayOutputModule module, Scene scene)
        {
            var legacyText = GetModuleReference<Text>(module, "legacyStatusText");
            if (legacyText == null || legacyText.gameObject.scene != scene) return;
            Undo.DestroyObjectImmediate(legacyText.gameObject);
        }

        private static void RegisterRuntimeModule(MondmadeVJ0Runtime runtime, DisplayOutputModule module)
        {
            var serialized = new SerializedObject(runtime);
            serialized.Update();
            var modules = serialized.FindProperty("modules");
            for (var i = 0; i < modules.arraySize; i++)
                if (modules.GetArrayElementAtIndex(i).objectReferenceValue == module)
                    return;

            Undo.RecordObject(runtime, "Register DisplayOutputModule");
            var index = modules.arraySize;
            modules.InsertArrayElementAtIndex(index);
            modules.GetArrayElementAtIndex(index).objectReferenceValue = module;
            serialized.ApplyModifiedProperties();
        }

        private static T GetModuleReference<T>(DisplayOutputModule module, string propertyName) where T : UnityEngine.Object
        {
            if (module == null) return null;
            var serialized = new SerializedObject(module);
            var property = serialized.FindProperty(propertyName);
            return property != null ? property.objectReferenceValue as T : null;
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
                SetLayerRecursively(child.gameObject, layer);
        }
    }
}
