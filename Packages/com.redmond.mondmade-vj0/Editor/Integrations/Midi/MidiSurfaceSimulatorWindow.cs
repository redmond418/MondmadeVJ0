using System;
using System.Collections.Generic;
using MondmadeVJ0;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MondmadeVJ0.Editor
{
    public sealed class MidiSurfaceSimulatorWindow : EditorWindow
    {
        private const string ApcDriverId = "apc-mini-mk2";
        private const string ApcProductName = "APC mini mk2";
        private const float GridCellSize = 34f;
        private const float GridGutter = 8f;
        private static readonly string[] LedColorNames = { "Off", "Red", "Amber", "Green", "Cyan", "Blue", "Magenta", "White" };
        private static readonly Color[] LedColors =
        {
            Color.black,
            new Color(1f, 0.04f, 0.02f),
            new Color(1f, 0.42f, 0.02f),
            new Color(0.04f, 1f, 0.08f),
            new Color(0.02f, 1f, 0.9f),
            new Color(0.02f, 0.18f, 1f),
            new Color(1f, 0.02f, 0.8f),
            Color.white
        };

        [SerializeField] private MidiHub hub;
        [SerializeField] private string surfaceId;
        [SerializeField] private string outputControlId;
        [SerializeField] private int outputColorIndex = 3;
        [NonSerialized] private MidiHub editorHub;
        [NonSerialized] private MidiHub editorHubSource;
        [NonSerialized] private RuntimeServices editorServices;
        [NonSerialized] private string editorSurfaceId;
        [NonSerialized] private string heldControlId;
        [NonSerialized] private int heldGuiControl;
        [NonSerialized] private double nextRepaintTime;
        private readonly Dictionary<string, MidiLedStyle> outputPreviews = new Dictionary<string, MidiLedStyle>(StringComparer.Ordinal);

        [MenuItem("Mondmade VJ0/MIDI/APC mini mk2 Simulator")]
        private static void OpenWindow() => Open();

        public static void Open(MidiHub source = null)
        {
            var window = GetWindow<MidiSurfaceSimulatorWindow>("APC mini mk2 Simulator");
            window.minSize = new Vector2(640f, 520f);
            if (source != null)
            {
                window.hub = source;
                window.surfaceId = null;
            }
            window.Show();
            window.Repaint();
        }

        private void OnEnable()
        {
            if (hub == null) hub = FindSceneHub();
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            ReleaseHeldControl();
            DisposeEditorHub();
        }

        private void OnLostFocus() => ReleaseHeldControl();

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Virtual MIDI Surface", EditorStyles.boldLabel);
            hub = (MidiHub)EditorGUILayout.ObjectField("MidiHub", hub, typeof(MidiHub), true);
            if (hub == null)
            {
                EditorGUILayout.HelpBox("Select a MidiHub in the scene. Add the virtual APC mini mk2 surface here, then connect your Routes to that surface.", MessageType.Info);
                return;
            }

            var surfaces = GetApcSurfaces(hub);
            if (surfaces.Count == 0)
            {
                EditorGUILayout.HelpBox("This MidiHub has no APC mini mk2 surface yet. Add a virtual surface to use the simulator without opening any MIDI ports.", MessageType.Info);
                using (new EditorGUI.DisabledScope(Application.isPlaying))
                    if (GUILayout.Button("Add virtual APC mini mk2 surface")) AddVirtualSurface();
                if (Application.isPlaying)
                    EditorGUILayout.HelpBox("Stop Play Mode before adding a surface. The new surface will be available the next time you enter Play Mode.", MessageType.Warning);
                return;
            }

            DrawSurfacePicker(surfaces);
            var selectedSurface = FindSurface(hub, surfaceId);
            if (selectedSurface == null || selectedSurface.Profile == null) return;

            EnsureEditorHub();
            var activeHub = Application.isPlaying ? hub : editorHub;
            if (activeHub == null)
            {
                EditorGUILayout.HelpBox("The virtual MIDI listener could not be initialized.", MessageType.Error);
                return;
            }

            var isVirtualSurface = selectedSurface.ProductName.IndexOf("(Virtual", StringComparison.OrdinalIgnoreCase) >= 0;
            var status = Application.isPlaying
                ? isVirtualSurface ? "Virtual (no MIDI device required)" : hub.GetSurfaceStatus(surfaceId)
                : "Virtual (no MIDI ports opened)";
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(selectedSurface.ProductName, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(Application.isPlaying ? status : "Edit Mode", EditorStyles.miniLabel, GUILayout.Width(170f));
            }
            if (Application.isPlaying && string.Equals(hub.GetSurfaceStatus(surfaceId), "Not initialized", StringComparison.Ordinal))
                EditorGUILayout.HelpBox("Add this MidiHub to MondmadeVJ0Runtime.Modules to send simulator input through scene Routes.", MessageType.Warning);

            DrawPadGrid(activeHub, selectedSurface.Profile);
            DrawTrackButtons(activeHub, selectedSurface.Profile);
            DrawFaders(activeHub, selectedSurface.Profile);
            EditorGUILayout.Space(6f);
            DrawOutputPreview(selectedSurface.Profile);

            if (Application.isPlaying)
                EditorGUILayout.HelpBox("Mouse input is queued through MidiHub, so active Routes receive the same control events as a physical surface. LED colors show Route feedback unless a local preview color is selected.", MessageType.None);
            else
                EditorGUILayout.HelpBox("Edit Mode uses an isolated temporary MidiHub. Input and LED preview can be checked here; enter Play Mode to drive scene Routes and view their LED feedback.", MessageType.None);
        }

        private void DrawSurfacePicker(List<MidiSurfaceInstance> surfaces)
        {
            var labels = new string[surfaces.Count];
            var selected = 0;
            for (var i = 0; i < surfaces.Count; i++)
            {
                var item = surfaces[i];
                labels[i] = $"{item.ProductName} #{item.DeviceOrdinal + 1} [{item.InstanceId.Substring(0, Math.Min(6, item.InstanceId.Length))}]";
                if (item.InstanceId == surfaceId) selected = i;
            }
            var chosen = EditorGUILayout.Popup("Surface", selected, labels);
            var nextSurfaceId = surfaces[Mathf.Clamp(chosen, 0, surfaces.Count - 1)].InstanceId;
            if (nextSurfaceId == surfaceId) return;
            ReleaseHeldControl();
            surfaceId = nextSurfaceId;
            outputControlId = null;
            DisposeEditorHub();
        }

        private void DrawPadGrid(MidiHub activeHub, MidiSurfaceProfile profile)
        {
            EditorGUILayout.LabelField("PAD MATRIX   /   SCENE LAUNCH", EditorStyles.miniBoldLabel);
            for (var y = 7; y >= 0; y--)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (var x = 0; x < 8; x++)
                    {
                        var pad = profile.Find($"pad_{x}_{y}");
                        if (pad == null) { GUILayout.Space(GridCellSize); continue; }
                        DrawMomentaryButton(activeHub, pad, new GUIContent($"{x + 1},{y + 1}"), new Vector2(GridCellSize, GridCellSize));
                    }

                    GUILayout.Space(GridGutter);
                    var scene = profile.Find($"scene_{y + 1}");
                    if (scene == null) GUILayout.Space(GridCellSize);
                    else DrawMomentaryButton(activeHub, scene, new GUIContent((y + 1).ToString()), new Vector2(GridCellSize, GridCellSize));
                }
            }
        }

        private void DrawTrackButtons(MidiHub activeHub, MidiSurfaceProfile profile)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("TRACK BUTTONS / SHIFT", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                for (var i = 0; i < 8; i++)
                {
                    var track = profile.Find($"track_{i + 1}");
                    if (track == null) GUILayout.Space(GridCellSize);
                    else DrawMomentaryButton(activeHub, track, new GUIContent((i + 1).ToString()), new Vector2(GridCellSize, GridCellSize));
                }
                GUILayout.Space(GridGutter);
                var shift = profile.Find("shift");
                if (shift != null)
                    DrawMomentaryButton(activeHub, shift, new GUIContent("S", "Shift"), new Vector2(GridCellSize, GridCellSize));
                else GUILayout.Space(GridCellSize);
            }
        }

        private void DrawFaders(MidiHub activeHub, MidiSurfaceProfile profile)
        {
            EditorGUILayout.LabelField("FADERS", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                for (var i = 0; i < 9; i++)
                {
                    var fader = profile.Find($"fader_{i + 1}");
                    if (fader == null) { GUILayout.Space(40f); continue; }
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(GridCellSize)))
                    {
                        var reference = new MidiControlReference(surfaceId, fader.id);
                        var state = activeHub.GetState(reference);
                        var range = fader.maximum - fader.minimum;
                        var normalized = Mathf.Abs(range) < 0.00001f ? 0f : Mathf.Clamp01((state.Value - fader.minimum) / range);
                        var sliderRect = GUILayoutUtility.GetRect(28f, 118f, GUILayout.Width(GridCellSize), GUILayout.Height(118f));
                        EditorGUI.DrawRect(sliderRect, new Color(0.12f, 0.13f, 0.15f));
                        var next = GUI.VerticalSlider(new Rect(sliderRect.x + 4f, sliderRect.y + 5f, sliderRect.width - 8f, sliderRect.height - 10f), normalized, 1f, 0f);
                        if (!Mathf.Approximately(next, normalized)) QueueInput(activeHub, fader, next);
                        EditorGUILayout.LabelField(i == 8 ? "M" : (i + 1).ToString(), EditorStyles.centeredGreyMiniLabel, GUILayout.Width(GridCellSize));
                        EditorGUILayout.LabelField(normalized.ToString("0.00"), EditorStyles.centeredGreyMiniLabel, GUILayout.Width(GridCellSize));
                    }
                    if (i == 7) GUILayout.Space(GridGutter);
                }
            }
        }

        private void DrawOutputPreview(MidiSurfaceProfile profile)
        {
            var ledControls = new List<MidiSurfaceControl>();
            foreach (var control in profile.Controls)
                if (control != null && control.hasLed) ledControls.Add(control);
            if (ledControls.Count == 0) return;

            EditorGUILayout.LabelField("LED OUTPUT PREVIEW", EditorStyles.boldLabel);
            var labels = new string[ledControls.Count];
            var selected = 0;
            for (var i = 0; i < ledControls.Count; i++)
            {
                labels[i] = $"{ledControls[i].label} ({ledControls[i].id})";
                if (ledControls[i].id == outputControlId) selected = i;
            }
            selected = EditorGUILayout.Popup("Test control", selected, labels);
            outputControlId = ledControls[Mathf.Clamp(selected, 0, ledControls.Count - 1)].id;
            using (new EditorGUILayout.HorizontalScope())
            {
                for (var i = 0; i < LedColors.Length; i++)
                {
                    var rect = GUILayoutUtility.GetRect(34f, 30f, GUILayout.Width(44f), GUILayout.Height(30f));
                    var selectedColor = i == outputColorIndex;
                    EditorGUI.DrawRect(rect, selectedColor ? Color.white : new Color(0.22f, 0.22f, 0.22f));
                    var fillRect = new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f);
                    EditorGUI.DrawRect(fillRect, LedColors[i] * 0.85f);
                    EditorGUI.LabelField(rect, GUIContent.none, EditorStyles.miniButton);
                    GUI.Label(fillRect, LedColorNames[i], EditorStyles.centeredGreyMiniLabel);
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && rect.Contains(Event.current.mousePosition))
                    {
                        outputColorIndex = i;
                        var key = OutputPreviewKey(surfaceId, outputControlId);
                        outputPreviews[key] = new MidiLedStyle { color = LedColors[i], brightness = i == 0 ? 0f : 1f, effect = MidiLedEffect.Solid };
                        Event.current.Use();
                        Repaint();
                    }
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Clear preview", GUILayout.Width(100f))) outputPreviews.Remove(OutputPreviewKey(surfaceId, outputControlId));
            }
        }

        private void DrawMomentaryButton(MidiHub activeHub, MidiSurfaceControl control, GUIContent label, Vector2 size)
        {
            var rect = GUILayoutUtility.GetRect(size.x, size.y, GUILayout.Width(size.x), GUILayout.Height(size.y));
            var reference = new MidiControlReference(surfaceId, control.id);
            var state = activeHub.GetState(reference);
            var style = GetVisibleLedStyle(activeHub, control);
            var pressed = state.Pressed || string.Equals(heldControlId, control.id, StringComparison.Ordinal);
            var baseColor = style.brightness <= 0f ? new Color(0.19f, 0.2f, 0.22f) : Color.Lerp(new Color(0.19f, 0.2f, 0.22f), style.color, Mathf.Clamp01(style.brightness));
            DrawButtonFace(rect, label, baseColor, pressed);
            HandleMomentaryInput(rect, activeHub, control);
        }

        private void HandleMomentaryInput(Rect rect, MidiHub activeHub, MidiSurfaceControl control)
        {
            var current = Event.current;
            var guiControl = GUIUtility.GetControlID(FocusType.Passive, rect);
            if (current.type == EventType.MouseDown && current.button == 0 && rect.Contains(current.mousePosition))
            {
                ReleaseHeldControl();
                heldControlId = control.id;
                heldGuiControl = guiControl;
                GUIUtility.hotControl = guiControl;
                QueueInput(activeHub, control, 1f);
                current.Use();
                Repaint();
            }
            else if (current.type == EventType.MouseUp && heldControlId == control.id && GUIUtility.hotControl == guiControl)
            {
                QueueInput(activeHub, control, 0f);
                heldControlId = null;
                heldGuiControl = 0;
                GUIUtility.hotControl = 0;
                current.Use();
                Repaint();
            }
        }

        private static void DrawButtonFace(Rect rect, GUIContent label, Color fill, bool pressed)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.miniButton);
            var inset = new Rect(rect.x + 3f, rect.y + 3f, rect.width - 6f, rect.height - 6f);
            EditorGUI.DrawRect(inset, fill);
            if (pressed) EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2f), new Color(1f, 1f, 1f, 0.9f));
            var textStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                normal = { textColor = fill.grayscale > 0.45f ? Color.black : Color.white },
                fontSize = 9
            };
            GUI.Label(inset, label, textStyle);
        }

        private MidiLedStyle GetVisibleLedStyle(MidiHub activeHub, MidiSurfaceControl control)
        {
            var key = OutputPreviewKey(surfaceId, control.id);
            if (outputPreviews.TryGetValue(key, out var preview)) return preview;
            return activeHub.GetEditorLedStyle(new MidiControlReference(surfaceId, control.id));
        }

        private void QueueInput(MidiHub activeHub, MidiSurfaceControl control, float normalizedValue)
        {
            if (!activeHub.QueueEditorInput(new MidiControlReference(surfaceId, control.id), normalizedValue))
                ShowNotification(new GUIContent("MIDI Surface is not initialized"));
        }

        private void EnsureEditorHub()
        {
            if (Application.isPlaying || hub == null) return;
            if (editorHub != null && editorHubSource == hub && editorSurfaceId == surfaceId) return;
            DisposeEditorHub();

            var go = new GameObject("Mondmade VJ0 APC Simulator") { hideFlags = HideFlags.HideAndDontSave };
            editorHub = go.AddComponent<MidiHub>();
            editorHub.hideFlags = HideFlags.HideAndDontSave;
            EditorUtility.CopySerialized(hub, editorHub);

            var serialized = new SerializedObject(editorHub);
            var surfaces = serialized.FindProperty("surfaces");
            for (var i = 0; surfaces != null && i < surfaces.arraySize; i++)
            {
                var surface = surfaces.GetArrayElementAtIndex(i);
                surface.FindPropertyRelative("inputBackend").enumValueIndex = (int)MidiInputBackend.Minis;
                surface.FindPropertyRelative("productName").stringValue = "Mondmade VJ0 Editor Simulator " + surfaceId;
                surface.FindPropertyRelative("outputPortOrdinal").intValue = -1;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();

            editorServices = new RuntimeServices();
            editorHub.Initialize(editorServices);
            editorHubSource = hub;
            editorSurfaceId = surfaceId;
        }

        private void DisposeEditorHub()
        {
            var current = editorHub;
            var services = editorServices;
            editorHub = null;
            editorServices = null;
            editorHubSource = null;
            editorSurfaceId = null;
            if (current != null && services != null)
            {
                try { current.Shutdown(services); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
            if (current != null) DestroyImmediate(current.gameObject);
        }

        private void OnEditorUpdate()
        {
            if (editorHub != null && editorServices != null && !Application.isPlaying)
            {
                try { editorHub.DrainInputQueue(editorServices, 4096); }
                catch (Exception exception) { Debug.LogException(exception); DisposeEditorHub(); }
            }
            if (EditorApplication.timeSinceStartup < nextRepaintTime) return;
            nextRepaintTime = EditorApplication.timeSinceStartup + 0.05d;
            Repaint();
        }

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change is PlayModeStateChange.ExitingEditMode or PlayModeStateChange.ExitingPlayMode)
            {
                ReleaseHeldControl();
                DisposeEditorHub();
            }
            Repaint();
        }

        private void ReleaseHeldControl()
        {
            if (string.IsNullOrEmpty(heldControlId)) return;
            var selectedHub = Application.isPlaying ? hub : editorHub;
            var surface = FindSurface(selectedHub, surfaceId);
            var control = surface?.Profile?.Find(heldControlId);
            if (selectedHub != null && control != null) QueueInput(selectedHub, control, 0f);
            heldControlId = null;
            if (GUIUtility.hotControl == heldGuiControl) GUIUtility.hotControl = 0;
            heldGuiControl = 0;
        }

        private void AddVirtualSurface()
        {
            if (hub == null || Application.isPlaying) return;
            if (!hub.gameObject.scene.IsValid())
            {
                ShowNotification(new GUIContent("Select a MidiHub from an open scene"));
                return;
            }
            var profile = FindApcProfile();
            if (profile == null)
            {
                profile = MidiSurfaceProfile.CreateApcMiniMk2Profile();
                EnsureDevMidiFolder();
                var path = AssetDatabase.GenerateUniqueAssetPath("Assets/MondmadeVJ0.Dev/Midi/APC-mini-mk2-Simulator.asset");
                AssetDatabase.CreateAsset(profile, path);
                AssetDatabase.SaveAssets();
            }

            Undo.RecordObject(hub, "Add virtual APC mini mk2 surface");
            var serialized = new SerializedObject(hub);
            var surfaces = serialized.FindProperty("surfaces");
            var index = surfaces.arraySize;
            surfaces.InsertArrayElementAtIndex(index);
            var surface = surfaces.GetArrayElementAtIndex(index);
            var id = Guid.NewGuid().ToString("N");
            surface.FindPropertyRelative("instanceId").stringValue = id;
            surface.FindPropertyRelative("profile").objectReferenceValue = profile;
            surface.FindPropertyRelative("productName").stringValue = $"{ApcProductName} (Virtual {id.Substring(0, 6)})";
            surface.FindPropertyRelative("deviceOrdinal").intValue = index;
            surface.FindPropertyRelative("inputBackend").enumValueIndex = (int)MidiInputBackend.Minis;
            surface.FindPropertyRelative("rawInputPortOrdinal").intValue = -1;
            surface.FindPropertyRelative("outputPortOrdinal").intValue = -1;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(hub);
            PrefabUtility.RecordPrefabInstancePropertyModifications(hub);
            if (hub.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(hub.gameObject.scene);

            surfaceId = id;
            outputControlId = null;
            DisposeEditorHub();
            GUI.changed = true;
        }

        private static MidiSurfaceProfile FindApcProfile()
        {
            var guids = AssetDatabase.FindAssets("t:MidiSurfaceProfile");
            foreach (var guid in guids)
            {
                var candidate = AssetDatabase.LoadAssetAtPath<MidiSurfaceProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (candidate != null && candidate.DriverId == ApcDriverId) return candidate;
            }
            return null;
        }

        private static void EnsureDevMidiFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/MondmadeVJ0.Dev"))
                AssetDatabase.CreateFolder("Assets", "MondmadeVJ0.Dev");
            if (!AssetDatabase.IsValidFolder("Assets/MondmadeVJ0.Dev/Midi"))
                AssetDatabase.CreateFolder("Assets/MondmadeVJ0.Dev", "Midi");
        }

        private static List<MidiSurfaceInstance> GetApcSurfaces(MidiHub target)
        {
            var result = new List<MidiSurfaceInstance>();
            foreach (var item in target.Surfaces)
                if (item?.Profile != null && item.Profile.DriverId == ApcDriverId) result.Add(item);
            return result;
        }

        private static MidiSurfaceInstance FindSurface(MidiHub target, string id)
        {
            if (target == null || string.IsNullOrEmpty(id)) return null;
            foreach (var item in target.Surfaces)
                if (item != null && item.InstanceId == id) return item;
            return null;
        }

        private static MidiHub FindSceneHub()
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<MidiHub>())
                if (candidate != null && candidate.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(candidate) &&
                    (candidate.hideFlags & HideFlags.DontSave) == 0) return candidate;
            return null;
        }

        private static string OutputPreviewKey(string selectedSurfaceId, string controlId) => selectedSurfaceId + "/" + controlId;
    }
}
