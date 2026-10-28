using System;
using System.Collections.Generic;
using UnityEngine;

namespace MondmadeVJ0.Display
{
    public enum DisplayOutputState
    {
        Uninitialized,
        EditorPreview,
        PreviewOnly,
        Output,
        Error
    }

    public enum DisplayOutputIssueSeverity
    {
        Warning,
        Error
    }

    public readonly struct DisplayOutputValidationIssue
    {
        public DisplayOutputValidationIssue(DisplayOutputIssueSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }

        public DisplayOutputIssueSeverity Severity { get; }
        public string Message { get; }
    }

    /// <summary>Captures one camera into a display-sized texture shared by preview and output RawImages.</summary>
    [DisallowMultipleComponent]
    public sealed class DisplayOutputModule : MonoBehaviour, IRuntimeModule
    {
        private static readonly HashSet<int> ActivatedDisplayIndices = new HashSet<int>();

        [SerializeField] private Camera outputCamera;
        [SerializeField] private Canvas outputCanvas;
        [SerializeField] private UnityEngine.UI.Dropdown displayDropdown;
        [SerializeField] private UnityEngine.UI.RawImage outputImage;
        [SerializeField] private UnityEngine.UI.RawImage previewImage;
        [SerializeField] private UnityEngine.UI.Text statusText;
        [SerializeField, Range(1, 7)] private int preferredDisplayIndex = 1;
        [SerializeField] private LayerMask operatorUiLayerMask = 1 << 5;
        [SerializeField] private Vector2Int editorPreviewResolution = new Vector2Int(1280, 720);

        private readonly List<int> availableDisplayIndices = new List<int>(7);
        private readonly List<PreviewRegistration> previewRegistrations = new List<PreviewRegistration>();

        private RuntimeServices services;
        private RenderTexture outputTexture;
        private RenderTexture previousCameraTargetTexture;
        private RenderTexture lastAssignedCameraTargetTexture;
        private Texture previousOutputImageTexture;
        private Texture lastAssignedOutputImageTexture;
        private bool previousOutputCanvasEnabled;
        private int previousOutputCanvasTargetDisplay;
        private int previousCameraCullingMask;
        private bool previousDropdownInteractable;
        private int previousDropdownValue;
        private List<UnityEngine.UI.Dropdown.OptionData> previousDropdownOptions;
        private string previousStatusText;
        private bool stateCaptured;
        private bool initializationAttempted;
        private bool initialized;
        private bool previewOnly;
        private bool editorPreviewMode;
        private int selectedDisplayIndex = -1;
        private int currentWidth;
        private int currentHeight;
        private string statusMessage = "未初期化";

        public string ModuleName => "Display Output";
        /// <summary>The module-owned texture shared with the output surface and registered previews. Do not release or destroy it.</summary>
        public RenderTexture OutputTexture => outputTexture;
        public int SelectedDisplayIndex => selectedDisplayIndex;
        public bool IsPreviewOnly => previewOnly;
        public string StatusMessage => statusMessage;
        public DisplayOutputState State { get; private set; } = DisplayOutputState.Uninitialized;

        /// <summary>Raised after the shared texture is replaced or becomes unavailable.</summary>
        public event Action<RenderTexture> OutputTextureChanged;

        public void Register(RuntimeServices runtimeServices)
        {
            services = runtimeServices;
        }

        public void Initialize(RuntimeServices runtimeServices)
        {
            if (initializationAttempted) return;
            initializationAttempted = true;
            services = runtimeServices;
            CapturePreviousState();

            var issues = ValidateConfiguration();
            var hasErrors = false;
            foreach (var issue in issues)
            {
                if (issue.Severity == DisplayOutputIssueSeverity.Error)
                {
                    hasErrors = true;
                    services?.Report("DISPLAY_CONFIG", issue.Message);
                }
                else services?.Report("DISPLAY_CONFIG_WARNING", issue.Message);
            }

            if (hasErrors)
            {
                SetError("二画面出力の設定にエラーがあります。InspectorのValidationを確認してください。");
                return;
            }

            initialized = true;
            displayDropdown.onValueChanged.AddListener(OnDisplayDropdownChanged);
            outputCamera.cullingMask &= ~operatorUiLayerMask.value;
            if (!RegisterPreview(previewImage))
            {
                SetError("画面1のプレビューRawImageを登録できませんでした。");
                return;
            }

            if (Application.isEditor)
            {
                ConfigurePreviewOnly(editorPreview: true);
                return;
            }

            EnumerateAndActivateDisplays();
            if (availableDisplayIndices.Count == 0)
            {
                ConfigurePreviewOnly(editorPreview: false);
                return;
            }

            ConfigureDisplayOptions();
            var preferredOption = availableDisplayIndices.IndexOf(preferredDisplayIndex);
            SelectDisplay(preferredOption >= 0 ? preferredOption : 0);
        }

        public void DrainInputQueue(RuntimeServices runtimeServices, int maxEvents) { }

        public void Tick(RuntimeServices runtimeServices, double deltaTime)
        {
            if (!initialized) return;

            if (previewOnly)
            {
                GetPreviewDimensions(out var width, out var height);
                if (width != currentWidth || height != currentHeight)
                {
                    currentWidth = width;
                    currentHeight = height;
                    UpdateDropdownPreviewLabel(width, height);
                    if (EnsureTexture(width, height))
                    {
                        State = editorPreviewMode ? DisplayOutputState.EditorPreview : DisplayOutputState.PreviewOnly;
                        SetStatus(editorPreviewMode
                            ? "Editor Previewのみ利用できます。実出力はStandalone Playerで確認してください。"
                            : "副ディスプレイなし — プレビューのみ利用できます。");
                    }
                }
                return;
            }

            if (!TryGetDisplayDimensions(selectedDisplayIndex, out var selectedDisplayWidth, out var selectedDisplayHeight))
            {
                if (State != DisplayOutputState.Error)
                {
                    currentWidth = currentHeight = 0;
                    SetError($"Display {selectedDisplayIndex} の描画解像度を取得できません。");
                    ClearOutputTexture();
                }
                return;
            }

            if (selectedDisplayWidth != currentWidth || selectedDisplayHeight != currentHeight)
            {
                currentWidth = selectedDisplayWidth;
                currentHeight = selectedDisplayHeight;
                UpdateDisplayOptionLabel(selectedDisplayIndex, currentWidth, currentHeight);
                if (EnsureTexture(currentWidth, currentHeight))
                {
                    State = DisplayOutputState.Output;
                    SetStatus($"出力中: {FormatDisplayLabel(selectedDisplayIndex, currentWidth, currentHeight)}");
                }
            }
        }

        public void FlushFrame(RuntimeServices runtimeServices) { }

        public void Shutdown(RuntimeServices runtimeServices)
        {
            if (displayDropdown != null && initialized)
                displayDropdown.onValueChanged.RemoveListener(OnDisplayDropdownChanged);

            RestoreOutputTextureReferences();
            ReleaseOutputTexture();

            if (stateCaptured)
            {
                if (outputCamera != null)
                {
                    if (outputCamera.targetTexture == lastAssignedCameraTargetTexture)
                        outputCamera.targetTexture = previousCameraTargetTexture;
                    outputCamera.cullingMask = (outputCamera.cullingMask & ~operatorUiLayerMask.value)
                        | (previousCameraCullingMask & operatorUiLayerMask.value);
                }
                if (outputCanvas != null)
                {
                    outputCanvas.enabled = previousOutputCanvasEnabled;
                    outputCanvas.targetDisplay = previousOutputCanvasTargetDisplay;
                }
                if (outputImage != null && outputImage.texture == lastAssignedOutputImageTexture)
                    outputImage.texture = previousOutputImageTexture;
                RestoreRegisteredPreviewTextures();
                RestoreDropdown();
                if (statusText != null) statusText.text = previousStatusText;
            }

            services = null;
            initialized = false;
            initializationAttempted = false;
            stateCaptured = false;
            previewOnly = false;
            selectedDisplayIndex = -1;
            currentWidth = currentHeight = 0;
            availableDisplayIndices.Clear();
            editorPreviewMode = false;
            State = DisplayOutputState.Uninitialized;
            statusMessage = "未初期化";
        }

        /// <summary>Shares OutputTexture with an additional uGUI RawImage until it is unregistered or the module shuts down.</summary>
        public bool RegisterPreview(UnityEngine.UI.RawImage target)
        {
            if (target == null)
            {
                Report("DISPLAY_PREVIEW", "登録先のRawImageがnullです。");
                return false;
            }
            if (target == outputImage || IsRecursivePreviewTarget(target))
            {
                Report("DISPLAY_PREVIEW_RECURSION", $"RawImage '{target.name}' は出力Canvas内、または出力Cameraで描画されるCanvas上にあるため登録できません。");
                return false;
            }

            var registration = FindPreviewRegistration(target);
            if (registration == null)
            {
                registration = new PreviewRegistration(target, target.texture);
                previewRegistrations.Add(registration);
            }

            if (outputTexture != null)
            {
                target.texture = outputTexture;
                registration.LastAssignedTexture = outputTexture;
            }
            return true;
        }

        public bool UnregisterPreview(UnityEngine.UI.RawImage target)
        {
            var registration = FindPreviewRegistration(target);
            if (registration == null) return false;
            RestorePreviewRegistration(registration);
            previewRegistrations.Remove(registration);
            return true;
        }

        public IReadOnlyList<DisplayOutputValidationIssue> ValidateConfiguration()
        {
            var issues = new List<DisplayOutputValidationIssue>();
            if (outputCamera == null)
                AddIssue(issues, DisplayOutputIssueSeverity.Error, "出力Cameraが割り当てられていません。");
            if (outputCanvas == null)
                AddIssue(issues, DisplayOutputIssueSeverity.Error, "出力Canvasが割り当てられていません。");
            else
            {
                if (outputCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    AddIssue(issues, DisplayOutputIssueSeverity.Error, "出力CanvasはScreen Space - Overlayにしてください。");
                if (!Application.isPlaying && outputCanvas.targetDisplay != 0)
                    AddIssue(issues, DisplayOutputIssueSeverity.Error, "出力Canvasの初期Target DisplayはDisplay 0にしてください。");
            }

            if (displayDropdown == null)
                AddIssue(issues, DisplayOutputIssueSeverity.Error, "画面1のuGUI Dropdownが割り当てられていません。");
            else
            {
                if (displayDropdown.template == null || displayDropdown.captionText == null || displayDropdown.itemText == null)
                    AddIssue(issues, DisplayOutputIssueSeverity.Error, "DropdownのTemplate、Caption Text、Item Textを設定してください。");
                else if (displayDropdown.template.GetComponentInChildren<UnityEngine.UI.Toggle>(true) == null)
                    AddIssue(issues, DisplayOutputIssueSeverity.Error, "Dropdown Template内にToggleを持つItemを設定してください。");
                ValidateOperatorCanvas(issues, displayDropdown.GetComponentInParent<Canvas>(), "Dropdown");
            }

            if (previewImage == null)
                AddIssue(issues, DisplayOutputIssueSeverity.Error, "画面1のプレビューRawImageが割り当てられていません。");
            else
            {
                var previewCanvas = previewImage.GetComponentInParent<Canvas>();
                ValidateOperatorCanvas(issues, previewCanvas, "プレビューRawImage");
                if (previewCanvas == outputCanvas)
                    AddIssue(issues, DisplayOutputIssueSeverity.Error, "プレビューRawImageは出力Canvasの外に配置してください。");
            }

            if (statusText == null)
                AddIssue(issues, DisplayOutputIssueSeverity.Error, "画面状態を表示するuGUI Textが割り当てられていません。");
            else if (displayDropdown != null && statusText.GetComponentInParent<Canvas>() != displayDropdown.GetComponentInParent<Canvas>())
                AddIssue(issues, DisplayOutputIssueSeverity.Error, "状態Textは画面1の操作Canvasに配置してください。");

            if (outputImage == null)
                AddIssue(issues, DisplayOutputIssueSeverity.Error, "画面2のRawImageが割り当てられていません。");
            else
            {
                if (outputCanvas != null && outputImage.GetComponentInParent<Canvas>() != outputCanvas)
                    AddIssue(issues, DisplayOutputIssueSeverity.Error, "画面2のRawImageは出力Canvasの子に配置してください。");
                var rect = outputImage.rectTransform;
                if (rect.anchorMin != Vector2.zero || rect.anchorMax != Vector2.one || rect.offsetMin != Vector2.zero || rect.offsetMax != Vector2.zero)
                    AddIssue(issues, DisplayOutputIssueSeverity.Error, "画面2のRawImageは出力Canvas全面にStretch配置してください。");
            }

            if (outputCanvas != null)
                foreach (var graphic in outputCanvas.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                    if (graphic != outputImage)
                        AddIssue(issues, DisplayOutputIssueSeverity.Error, "出力Canvasには画面2のRawImage以外のuGUI Graphicを置かないでください。");

            if (outputCamera != null && operatorUiLayerMask.value != 0 && (outputCamera.cullingMask & operatorUiLayerMask.value) != 0)
                AddIssue(issues, DisplayOutputIssueSeverity.Warning, "出力Cameraから操作UI Layerを除外します（Play開始時に自動適用）。");
            if (operatorUiLayerMask.value == 0)
                AddIssue(issues, DisplayOutputIssueSeverity.Error, "操作UI Layer Maskを設定してください。");
            if (editorPreviewResolution.x <= 0 || editorPreviewResolution.y <= 0)
                AddIssue(issues, DisplayOutputIssueSeverity.Error, "Editor Preview Resolutionは幅・高さともに1以上にしてください。");
            if (preferredDisplayIndex < 1 || preferredDisplayIndex > 7)
                AddIssue(issues, DisplayOutputIssueSeverity.Error, "優先Display indexは副画面の1〜7を指定してください。");

            if (displayDropdown != null)
            {
                var eventSystem = UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
                if (eventSystem == null)
                    AddIssue(issues, DisplayOutputIssueSeverity.Error, "Dropdownを操作するEventSystemがSceneにありません。");
                else if (eventSystem.GetComponent<UnityEngine.EventSystems.BaseInputModule>() == null)
                    AddIssue(issues, DisplayOutputIssueSeverity.Error, "EventSystemにStandaloneInputModuleまたはInputSystemUIInputModuleがありません。");
            }

            foreach (var registration in previewRegistrations)
                if (registration.Target != null && IsRecursivePreviewTarget(registration.Target))
                    AddIssue(issues, DisplayOutputIssueSeverity.Error,
                        $"登録済みRawImage '{registration.Target.name}' が出力Cameraの描画対象に含まれます。");

            return issues;
        }

        public static string FormatDisplayLabel(int displayIndex, int width, int height)
        {
            return $"画面{displayIndex + 1} / Display {displayIndex} — {width} × {height}";
        }

        private void CapturePreviousState()
        {
            if (stateCaptured) return;
            stateCaptured = true;
            if (outputCanvas != null)
            {
                previousOutputCanvasEnabled = outputCanvas.enabled;
                previousOutputCanvasTargetDisplay = outputCanvas.targetDisplay;
            }
            if (outputCamera != null)
            {
                previousCameraTargetTexture = outputCamera.targetTexture;
                previousCameraCullingMask = outputCamera.cullingMask;
            }
            if (outputImage != null) previousOutputImageTexture = outputImage.texture;
            if (displayDropdown != null)
            {
                previousDropdownInteractable = displayDropdown.interactable;
                previousDropdownValue = displayDropdown.value;
                previousDropdownOptions = new List<UnityEngine.UI.Dropdown.OptionData>(displayDropdown.options.Count);
                foreach (var option in displayDropdown.options)
                    previousDropdownOptions.Add(new UnityEngine.UI.Dropdown.OptionData(option.text, option.image));
            }
            if (statusText != null) previousStatusText = statusText.text;
        }

        private void EnumerateAndActivateDisplays()
        {
            availableDisplayIndices.Clear();
            var displays = UnityEngine.Display.displays;
            for (var displayIndex = 1; displays != null && displayIndex < displays.Length; displayIndex++)
            {
                try
                {
                    if (!ActivatedDisplayIndices.Contains(displayIndex))
                    {
                        displays[displayIndex].Activate();
                        ActivatedDisplayIndices.Add(displayIndex);
                    }
                    if (displays[displayIndex].renderingWidth <= 0 || displays[displayIndex].renderingHeight <= 0)
                    {
                        Report("DISPLAY_SIZE", $"Display {displayIndex} が有効な描画解像度を返しませんでした。");
                        continue;
                    }
                    availableDisplayIndices.Add(displayIndex);
                }
                catch (Exception exception)
                {
                    services?.Report("DISPLAY_ACTIVATE", $"Display {displayIndex} をActivateできませんでした。", exception: exception);
                }
            }
        }

        private void ConfigurePreviewOnly(bool editorPreview)
        {
            previewOnly = true;
            editorPreviewMode = editorPreview;
            selectedDisplayIndex = -1;
            outputCanvas.enabled = false;
            displayDropdown.ClearOptions();
            GetPreviewDimensions(out currentWidth, out currentHeight);
            var label = editorPreview
                ? $"Editor Preview — {currentWidth} × {currentHeight}"
                : $"副ディスプレイなし — Preview {currentWidth} × {currentHeight}";
            displayDropdown.AddOptions(new List<UnityEngine.UI.Dropdown.OptionData> { new UnityEngine.UI.Dropdown.OptionData(label) });
            displayDropdown.SetValueWithoutNotify(0);
            displayDropdown.interactable = false;
            if (EnsureTexture(currentWidth, currentHeight))
            {
                State = editorPreview ? DisplayOutputState.EditorPreview : DisplayOutputState.PreviewOnly;
                SetStatus(editorPreview ? "Editor Previewのみ利用できます。実出力はStandalone Playerで確認してください。" : "副ディスプレイなし — プレビューのみ利用できます。");
            }
        }

        private void ConfigureDisplayOptions()
        {
            var options = new List<UnityEngine.UI.Dropdown.OptionData>(availableDisplayIndices.Count);
            foreach (var displayIndex in availableDisplayIndices)
            {
                TryGetDisplayDimensions(displayIndex, out var width, out var height);
                options.Add(new UnityEngine.UI.Dropdown.OptionData(FormatDisplayLabel(displayIndex, width, height)));
            }
            displayDropdown.ClearOptions();
            displayDropdown.AddOptions(options);
            displayDropdown.interactable = true;
        }

        private void OnDisplayDropdownChanged(int optionIndex)
        {
            if (!initialized || previewOnly) return;
            SelectDisplay(optionIndex);
        }

        private void SelectDisplay(int optionIndex)
        {
            if (optionIndex < 0 || optionIndex >= availableDisplayIndices.Count)
            {
                SetError("Dropdownから有効な副ディスプレイを選択できません。");
                outputCanvas.enabled = false;
                return;
            }

            selectedDisplayIndex = availableDisplayIndices[optionIndex];
            TryGetDisplayDimensions(selectedDisplayIndex, out currentWidth, out currentHeight);
            outputCanvas.targetDisplay = selectedDisplayIndex;
            outputCanvas.enabled = true;
            if (EnsureTexture(currentWidth, currentHeight))
            {
                State = DisplayOutputState.Output;
                SetStatus($"出力中: {FormatDisplayLabel(selectedDisplayIndex, currentWidth, currentHeight)}");
            }
        }

        private bool EnsureTexture(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                FailTexture($"RenderTextureの寸法が無効です: {width} × {height}。");
                return false;
            }
            if (width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize)
            {
                FailTexture($"出力解像度 {width} × {height} は最大Texture寸法 {SystemInfo.maxTextureSize} を超えています。");
                return false;
            }
            if (outputTexture != null && outputTexture.width == width && outputTexture.height == height && outputTexture.IsCreated())
                return true;

            RenderTexture replacement = null;
            try
            {
                replacement = new RenderTexture(width, height, 24, RenderTextureFormat.Default)
                {
                    name = $"MondmadeVJ0 Display Output {width}x{height}",
                    antiAliasing = 1,
                    useMipMap = false,
                    autoGenerateMips = false,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                if (!replacement.Create() || !replacement.IsCreated())
                {
                    FailTexture($"RenderTexture {width} × {height} を作成できませんでした。");
                    DestroyTexture(replacement);
                    return false;
                }
            }
            catch (Exception exception)
            {
                FailTexture($"RenderTexture {width} × {height} の作成に失敗しました: {exception.Message}");
                if (replacement != null) DestroyTexture(replacement);
                return false;
            }

            var oldTexture = outputTexture;
            outputTexture = replacement;
            currentWidth = width;
            currentHeight = height;
            outputCamera.targetTexture = replacement;
            lastAssignedCameraTargetTexture = replacement;
            outputImage.texture = replacement;
            lastAssignedOutputImageTexture = replacement;
            AssignTextureToRegisteredPreviews(replacement);
            if (oldTexture != null) DestroyTexture(oldTexture);
            NotifyOutputTextureChanged(replacement);
            return true;
        }

        private void FailTexture(string message)
        {
            Report("DISPLAY_RENDER_TEXTURE", message);
            ClearOutputTexture();
            SetError(message);
        }

        private void ClearOutputTexture()
        {
            var oldTexture = outputTexture;
            if (oldTexture == null) return;
            if (outputCamera != null && outputCamera.targetTexture == oldTexture)
            {
                outputCamera.targetTexture = previousCameraTargetTexture;
                lastAssignedCameraTargetTexture = previousCameraTargetTexture;
            }
            if (outputImage != null && outputImage.texture == oldTexture)
            {
                outputImage.texture = previousOutputImageTexture;
                lastAssignedOutputImageTexture = previousOutputImageTexture;
            }
            RestorePreviewTexturesFor(oldTexture);
            outputTexture = null;
            if (outputCanvas != null && !previewOnly) outputCanvas.enabled = false;
            DestroyTexture(oldTexture);
            NotifyOutputTextureChanged(null);
        }

        private void RestoreOutputTextureReferences()
        {
            if (outputTexture == null) return;
            var previousTexture = outputTexture;
            if (outputCamera != null && outputCamera.targetTexture == lastAssignedCameraTargetTexture)
                outputCamera.targetTexture = previousCameraTargetTexture;
            if (outputImage != null && outputImage.texture == outputTexture)
                outputImage.texture = previousOutputImageTexture;
            RestorePreviewTexturesFor(outputTexture);
            outputTexture = null;
            NotifyOutputTextureChanged(null);
            DestroyTexture(previousTexture);
        }

        private void ReleaseOutputTexture()
        {
            if (outputTexture == null) return;
            DestroyTexture(outputTexture);
            outputTexture = null;
        }

        private void AssignTextureToRegisteredPreviews(RenderTexture texture)
        {
            for (var i = previewRegistrations.Count - 1; i >= 0; i--)
            {
                var registration = previewRegistrations[i];
                if (registration.Target == null)
                {
                    previewRegistrations.RemoveAt(i);
                    continue;
                }
                registration.Target.texture = texture;
                registration.LastAssignedTexture = texture;
            }
        }

        private void RestorePreviewTexturesFor(Texture texture)
        {
            foreach (var registration in previewRegistrations)
                if (registration.Target != null && registration.Target.texture == texture)
                    registration.Target.texture = registration.PreviousTexture;
        }

        private void RestoreRegisteredPreviewTextures()
        {
            foreach (var registration in previewRegistrations) RestorePreviewRegistration(registration);
            previewRegistrations.Clear();
        }

        private static void RestorePreviewRegistration(PreviewRegistration registration)
        {
            if (registration.Target != null && registration.Target.texture == registration.LastAssignedTexture)
                registration.Target.texture = registration.PreviousTexture;
        }

        private PreviewRegistration FindPreviewRegistration(UnityEngine.UI.RawImage target)
        {
            foreach (var registration in previewRegistrations)
                if (registration.Target == target) return registration;
            return null;
        }

        private bool IsRecursivePreviewTarget(UnityEngine.UI.RawImage target)
        {
            if (outputCanvas != null && target.transform.IsChildOf(outputCanvas.transform)) return true;
            if (outputCamera == null) return false;

            var canvas = target.GetComponentInParent<Canvas>();
            if (canvas == null) return false;
            if (canvas.renderMode == RenderMode.ScreenSpaceCamera)
                return canvas.worldCamera == outputCamera;
            if (canvas.renderMode == RenderMode.WorldSpace)
                return (outputCamera.cullingMask & (1 << target.gameObject.layer)) != 0;
            return false;
        }

        private void GetPreviewDimensions(out int width, out int height)
        {
            var displays = UnityEngine.Display.displays;
            if (displays != null && displays.Length > 0 && displays[0].renderingWidth > 0 && displays[0].renderingHeight > 0)
            {
                width = displays[0].renderingWidth;
                height = displays[0].renderingHeight;
                return;
            }
            width = editorPreviewResolution.x;
            height = editorPreviewResolution.y;
        }

        private bool TryGetDisplayDimensions(int displayIndex, out int width, out int height)
        {
            var displays = UnityEngine.Display.displays;
            if (displays != null && displayIndex >= 0 && displayIndex < displays.Length)
            {
                width = displays[displayIndex].renderingWidth;
                height = displays[displayIndex].renderingHeight;
                return width > 0 && height > 0;
            }
            width = height = 0;
            return false;
        }

        private void UpdateDisplayOptionLabel(int displayIndex, int width, int height)
        {
            var optionIndex = availableDisplayIndices.IndexOf(displayIndex);
            if (optionIndex < 0 || optionIndex >= displayDropdown.options.Count) return;
            var option = displayDropdown.options[optionIndex];
            var label = FormatDisplayLabel(displayIndex, width, height);
            if (option.text == label) return;
            option.text = label;
            displayDropdown.RefreshShownValue();
        }

        private void UpdateDropdownPreviewLabel(int width, int height)
        {
            if (displayDropdown == null || displayDropdown.options.Count == 0) return;
            displayDropdown.options[0].text = editorPreviewMode
                ? $"Editor Preview — {width} × {height}"
                : $"副ディスプレイなし — Preview {width} × {height}";
            displayDropdown.RefreshShownValue();
        }

        private void RestoreDropdown()
        {
            if (displayDropdown == null) return;
            displayDropdown.ClearOptions();
            if (previousDropdownOptions != null) displayDropdown.AddOptions(previousDropdownOptions);
            displayDropdown.SetValueWithoutNotify(previousDropdownOptions != null && previousDropdownOptions.Count > 0
                ? Mathf.Clamp(previousDropdownValue, 0, previousDropdownOptions.Count - 1)
                : 0);
            displayDropdown.interactable = previousDropdownInteractable;
        }

        private void ValidateOperatorCanvas(List<DisplayOutputValidationIssue> issues, Canvas canvas, string targetName)
        {
            if (canvas == null)
            {
                AddIssue(issues, DisplayOutputIssueSeverity.Error, $"{targetName}の親にCanvasがありません。");
                return;
            }
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                AddIssue(issues, DisplayOutputIssueSeverity.Error, $"{targetName}の操作CanvasはScreen Space - Overlayにしてください。");
            if (canvas.targetDisplay != 0)
                AddIssue(issues, DisplayOutputIssueSeverity.Error, $"{targetName}の操作CanvasはDisplay 0に配置してください。");
            if (canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null && targetName == "Dropdown")
                AddIssue(issues, DisplayOutputIssueSeverity.Error, "Dropdownの操作CanvasにGraphicRaycasterがありません。");

            var unmaskedLayers = new HashSet<int>();
            foreach (var child in canvas.GetComponentsInChildren<Transform>(true))
                if (operatorUiLayerMask.value == 0 || (operatorUiLayerMask.value & (1 << child.gameObject.layer)) == 0)
                    unmaskedLayers.Add(child.gameObject.layer);
            foreach (var layer in unmaskedLayers)
            {
                var layerName = LayerMask.LayerToName(layer);
                AddIssue(issues, DisplayOutputIssueSeverity.Error,
                    $"操作UIのLayer '{(string.IsNullOrEmpty(layerName) ? layer.ToString() : layerName)}' を操作UI Layer Maskに含めてください。");
            }
        }

        private static void AddIssue(List<DisplayOutputValidationIssue> issues, DisplayOutputIssueSeverity severity, string message)
        {
            issues.Add(new DisplayOutputValidationIssue(severity, message));
        }

        private void SetError(string message)
        {
            State = DisplayOutputState.Error;
            SetStatus(message);
        }

        private void SetStatus(string message)
        {
            statusMessage = message;
            if (statusText != null) statusText.text = message;
        }

        private void Report(string code, string message)
        {
            services?.Report(code, message);
        }

        private void NotifyOutputTextureChanged(RenderTexture texture)
        {
            var handlers = OutputTextureChanged;
            if (handlers == null) return;
            foreach (Action<RenderTexture> handler in handlers.GetInvocationList())
            {
                try { handler(texture); }
                catch (Exception exception) { services?.Report("DISPLAY_TEXTURE_LISTENER", "An OutputTextureChanged listener threw an exception.", exception: exception); }
            }
        }

        private static void DestroyTexture(RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            if (Application.isPlaying) Destroy(texture);
            else DestroyImmediate(texture);
        }

        private sealed class PreviewRegistration
        {
            public PreviewRegistration(UnityEngine.UI.RawImage target, Texture previousTexture)
            {
                Target = target;
                PreviousTexture = previousTexture;
                LastAssignedTexture = previousTexture;
            }

            public UnityEngine.UI.RawImage Target { get; }
            public Texture PreviousTexture { get; }
            public Texture LastAssignedTexture { get; set; }
        }
    }
}
