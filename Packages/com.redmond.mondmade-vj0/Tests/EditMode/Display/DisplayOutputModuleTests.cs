using System.Collections.Generic;
using System.Reflection;
using MondmadeVJ0.Display;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MondmadeVJ0.Tests.Display
{
    public sealed class DisplayOutputModuleTests
    {
        private readonly List<Object> cleanup = new List<Object>();
        private DisplayOutputModule module;
        private Camera outputCamera;
        private Canvas outputCanvas;
        private TMP_Dropdown dropdown;
        private RawImage outputImage;
        private RawImage previewImage;
        private Texture2D previousOutputTexture;
        private Texture2D previousPreviewTexture;
        private RuntimeServices services;
        private int previousCullingMask;

        [SetUp]
        public void SetUp()
        {
            var eventSystemObject = Own(new GameObject("Display Output Test EventSystem"));
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<StandaloneInputModule>();

            var operatorCanvasObject = Own(new GameObject("Operator Canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)));
            operatorCanvasObject.layer = LayerMask.NameToLayer("UI");
            var operatorCanvas = operatorCanvasObject.GetComponent<Canvas>();
            operatorCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            operatorCanvas.targetDisplay = 0;

            var dropdownObject = Own(new GameObject("Display Dropdown", typeof(RectTransform), typeof(TMP_Dropdown)));
            dropdownObject.transform.SetParent(operatorCanvasObject.transform, false);
            dropdown = dropdownObject.GetComponent<TMP_Dropdown>();
            dropdown.template = CreateRectChild(dropdownObject.transform, "Template");
            dropdown.captionText = CreateTmpTextChild(operatorCanvasObject.transform, "Caption");
            dropdown.itemText = CreateTmpTextChild(dropdown.template, "Item");
            dropdown.itemText.gameObject.AddComponent<Toggle>();
            dropdown.template.gameObject.SetActive(false);
            dropdown.options.Add(new TMP_Dropdown.OptionData("Original option"));

            previewImage = CreateRawImageChild(operatorCanvasObject.transform, "Preview");
            SetLayerRecursively(operatorCanvasObject.transform, LayerMask.NameToLayer("UI"));

            var outputCanvasObject = Own(new GameObject("Output Canvas", typeof(RectTransform), typeof(Canvas)));
            outputCanvas = outputCanvasObject.GetComponent<Canvas>();
            outputCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            outputCanvas.targetDisplay = 0;
            outputImage = CreateRawImageChild(outputCanvasObject.transform, "Output Image");
            outputImage.rectTransform.anchorMin = Vector2.zero;
            outputImage.rectTransform.anchorMax = Vector2.one;
            outputImage.rectTransform.offsetMin = Vector2.zero;
            outputImage.rectTransform.offsetMax = Vector2.zero;

            var cameraObject = Own(new GameObject("Output Camera"));
            outputCamera = cameraObject.AddComponent<Camera>();
            previousCullingMask = outputCamera.cullingMask;
            previousOutputTexture = CreateTexture("Previous Output");
            previousPreviewTexture = CreateTexture("Previous Preview");
            outputImage.texture = previousOutputTexture;
            previewImage.texture = previousPreviewTexture;

            var moduleObject = Own(new GameObject("Display Output Module"));
            module = moduleObject.AddComponent<DisplayOutputModule>();
            SetField(module, "outputCamera", outputCamera);
            SetField(module, "outputCanvas", outputCanvas);
            SetField(module, "displayDropdown", dropdown);
            SetField(module, "outputImage", outputImage);
            SetField(module, "previewImage", previewImage);

            services = new RuntimeServices();
        }

        [TearDown]
        public void TearDown()
        {
            module?.Shutdown(services);
            if (outputCamera != null) outputCamera.targetTexture = null;
            for (var i = cleanup.Count - 1; i >= 0; i--)
                if (cleanup[i] != null) Object.DestroyImmediate(cleanup[i]);
            cleanup.Clear();
        }

        [Test]
        public void EditorPreviewSharesOneDisplaySizedTextureAndRestoresReferencesOnShutdown()
        {
            module.Register(services);
            module.Initialize(services);

            Assert.That(module.State, Is.EqualTo(DisplayOutputState.EditorPreview));
            Assert.That(module.OutputTexture, Is.Not.Null);
            const int expectedWidth = 1280;
            const int expectedHeight = 720;
            Assert.That(module.OutputTexture.width, Is.EqualTo(expectedWidth));
            Assert.That(module.OutputTexture.height, Is.EqualTo(expectedHeight));
            Assert.That(outputCamera.targetTexture, Is.SameAs(module.OutputTexture));
            Assert.That(outputImage.texture, Is.SameAs(module.OutputTexture));
            Assert.That(previewImage.texture, Is.SameAs(module.OutputTexture));
            Assert.That(outputCanvas.enabled, Is.True);
            Assert.That(outputCanvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceCamera));
            Assert.That(outputCanvas.worldCamera, Is.Not.Null);
            Assert.That(outputCanvas.worldCamera.targetDisplay, Is.EqualTo(1));
            var operatorSurface = GameObject.Find("MondmadeVJ0 Operator Surface").GetComponent<Camera>();
            Assert.That(operatorSurface.targetDisplay, Is.EqualTo(0));
            Assert.That(operatorSurface.depth, Is.GreaterThan(outputCanvas.worldCamera.depth));
            Assert.That(outputCanvas.worldCamera.depth, Is.GreaterThan(outputCamera.depth));
            Assert.That(dropdown.interactable, Is.True);
            var aspectFitter = previewImage.GetComponent<AspectRatioFitter>();
            Assert.That(aspectFitter, Is.Not.Null);
            Assert.That(aspectFitter.aspectMode, Is.EqualTo(AspectRatioFitter.AspectMode.FitInParent));
            Assert.That(aspectFitter.aspectRatio, Is.EqualTo(expectedWidth / (float)expectedHeight).Within(0.001f));

            var extraCanvasObject = Own(new GameObject("Extra Preview Canvas", typeof(RectTransform), typeof(Canvas)));
            extraCanvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var extraPreview = CreateRawImageChild(extraCanvasObject.transform, "Extra Preview");
            var originalExtraTexture = CreateTexture("Previous Extra Preview");
            extraPreview.texture = originalExtraTexture;
            Assert.That(module.RegisterPreview(extraPreview), Is.True);
            Assert.That(extraPreview.texture, Is.SameAs(module.OutputTexture));
            Assert.That(module.RegisterPreview(outputImage), Is.False, "The output-surface RawImage must not be registered as a preview.");
            Assert.That(module.UnregisterPreview(extraPreview), Is.True);
            Assert.That(extraPreview.texture, Is.SameAs(originalExtraTexture));

            module.Shutdown(services);

            Assert.That(outputCamera.targetTexture, Is.Null);
            Assert.That(outputCamera.cullingMask, Is.EqualTo(previousCullingMask));
            Assert.That(outputImage.texture, Is.SameAs(previousOutputTexture));
            Assert.That(previewImage.texture, Is.SameAs(previousPreviewTexture));
            Assert.That(outputCanvas.enabled, Is.True);
            Assert.That(outputCanvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(outputCanvas.worldCamera, Is.Null);
            Assert.That(GameObject.Find("MondmadeVJ0 Operator Surface"), Is.Null);
            Assert.That(GameObject.Find("MondmadeVJ0 Output Surface"), Is.Null);
            Assert.That(module.OutputTexture, Is.Null);
        }

        [Test]
        public void DisplayLabelShowsScreenNumberIndexAndCurrentResolution()
        {
            Assert.That(DisplayOutputModule.FormatDisplayLabel(2, 2560, 1440), Is.EqualTo("画面3 / Display 2 — 2560 × 1440"));
        }

        [Test]
        public void RejectsRawImagesThatWouldFeedTheOutputTextureBackIntoItsOwnRender()
        {
            Assert.That(module.RegisterPreview(outputImage), Is.False);

            var worldCanvasObject = Own(new GameObject("World Space Preview Canvas", typeof(RectTransform), typeof(Canvas)));
            var worldCanvas = worldCanvasObject.GetComponent<Canvas>();
            worldCanvas.renderMode = RenderMode.WorldSpace;
            var recursiveImage = CreateRawImageChild(worldCanvasObject.transform, "Recursive Preview");
            Assert.That(module.RegisterPreview(recursiveImage), Is.False);
        }

        private GameObject Own(GameObject gameObject)
        {
            cleanup.Add(gameObject);
            return gameObject;
        }

        private T Own<T>(T unityObject) where T : Object
        {
            cleanup.Add(unityObject);
            return unityObject;
        }

        private Texture2D CreateTexture(string name)
        {
            return Own(new Texture2D(4, 4) { name = name });
        }

        private static RectTransform CreateRectChild(Transform parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            return child.GetComponent<RectTransform>();
        }

        private static TextMeshProUGUI CreateTmpTextChild(Transform parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            child.transform.SetParent(parent, false);
            return child.GetComponent<TextMeshProUGUI>();
        }

        private static RawImage CreateRawImageChild(Transform parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            child.transform.SetParent(parent, false);
            return child.GetComponent<RawImage>();
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            foreach (Transform child in root) SetLayerRecursively(child, layer);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            typeof(DisplayOutputModule).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }
    }
}
