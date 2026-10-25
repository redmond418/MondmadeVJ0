using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RtMidi;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MondmadeVJ0.Editor
{
    [CustomEditor(typeof(MidiHub))]
    public sealed class MidiHubEditor : UnityEditor.Editor
    {
        private string[] inputPorts = Array.Empty<string>();
        private string[] outputPorts = Array.Empty<string>();

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("MIDI Surface setup", EditorStyles.boldLabel);
            if (GUILayout.Button("Refresh MIDI ports")) RefreshPorts();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create APC mini mk2 Profile")) CreateProfile();
                if (GUILayout.Button("Add APC mini mk2 Surface")) AddApcSurface();
            }
            DrawPortList("Input", inputPorts);
            DrawPortList("Output", outputPorts);

            var hub = (MidiHub)target;
            if (Application.isPlaying)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
                foreach (var surface in hub.Surfaces)
                    if (surface != null) EditorGUILayout.LabelField($"{surface.ProductName} #{surface.DeviceOrdinal + 1}", hub.GetSurfaceStatus(surface.InstanceId));
                EditorGUILayout.LabelField("Dropped control messages", hub.DroppedNormalMessages.ToString());
                EditorGUILayout.LabelField("Dropped clock messages", hub.DroppedClockMessages.ToString());
                EditorGUILayout.LabelField("Dropped output messages", hub.DroppedOutputMessages.ToString());
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Panic: all LEDs off")) hub.PanicAllOff();
                    if (GUILayout.Button("Resume LED output")) hub.ResumeOutputs();
                }
            }
            else EditorGUILayout.HelpBox("Add this MidiHub component to MondmadeVJ0Runtime.Modules. APC surfaces use the raw RtMidi backend so same-name devices can be selected by port index.", MessageType.Info);
        }

        private void RefreshPorts()
        {
            inputPorts = MidiPortCatalog.Inputs();
            outputPorts = MidiPortCatalog.Outputs();
        }

        private static void DrawPortList(string label, string[] ports)
        {
            EditorGUILayout.LabelField($"{label} ports ({ports.Length})", EditorStyles.miniBoldLabel);
            if (ports.Length == 0) EditorGUILayout.LabelField("No ports detected.", EditorStyles.miniLabel);
            for (var i = 0; i < ports.Length; i++) EditorGUILayout.LabelField($"{i}: {ports[i]}", EditorStyles.miniLabel);
        }

        private static void CreateProfile()
        {
            EnsureDevMidiFolder();
            var path = EditorUtility.SaveFilePanelInProject("Create APC mini mk2 Profile", "APC-mini-mk2", "asset", "Choose a path for the APC mini mk2 profile.", "Assets/MondmadeVJ0.Dev/Midi");
            if (string.IsNullOrEmpty(path)) return;
            var profile = MidiSurfaceProfile.CreateApcMiniMk2Profile();
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = profile;
        }

        private void AddApcSurface()
        {
            RefreshPorts();
            EnsureDevMidiFolder();
            var profileGuids = AssetDatabase.FindAssets("t:MidiSurfaceProfile");
            MidiSurfaceProfile profile = null;
            foreach (var guid in profileGuids)
            {
                var candidate = AssetDatabase.LoadAssetAtPath<MidiSurfaceProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (candidate != null && candidate.DriverId == "apc-mini-mk2") { profile = candidate; break; }
            }
            if (profile == null)
            {
                profile = MidiSurfaceProfile.CreateApcMiniMk2Profile();
                AssetDatabase.CreateAsset(profile, "Assets/MondmadeVJ0.Dev/Midi/APC-mini-mk2.asset");
                AssetDatabase.SaveAssets();
            }

            serializedObject.Update();
            var array = serializedObject.FindProperty("surfaces");
            var usedInputs = new HashSet<int>();
            var usedOutputs = new HashSet<int>();
            var deviceOrdinal = 0;
            for (var i = 0; i < array.arraySize; i++)
            {
                var existing = array.GetArrayElementAtIndex(i);
                var existingProfile = existing.FindPropertyRelative("profile").objectReferenceValue as MidiSurfaceProfile;
                var existingProduct = existing.FindPropertyRelative("productName").stringValue;
                if (string.IsNullOrEmpty(existingProduct)) existingProduct = existingProfile == null ? string.Empty : existingProfile.ProductName;
                if (existingProduct.IndexOf(profile.ProductName, StringComparison.OrdinalIgnoreCase) < 0) continue;

                deviceOrdinal++;
                if ((MidiInputBackend)existing.FindPropertyRelative("inputBackend").enumValueIndex == MidiInputBackend.RtMidiRaw)
                {
                    var inputPort = existing.FindPropertyRelative("rawInputPortOrdinal").intValue;
                    if (inputPort >= 0) usedInputs.Add(inputPort);
                }
                var outputPort = existing.FindPropertyRelative("outputPortOrdinal").intValue;
                if (outputPort >= 0) usedOutputs.Add(outputPort);
            }

            var input = FindAvailablePort(inputPorts, "apc mini mk2", "control", usedInputs);
            var output = FindAvailablePort(outputPorts, "apc mini mk2", "control", usedOutputs);
            var index = array.arraySize;
            array.InsertArrayElementAtIndex(index);
            var surface = array.GetArrayElementAtIndex(index);
            surface.FindPropertyRelative("instanceId").stringValue = Guid.NewGuid().ToString("N");
            surface.FindPropertyRelative("profile").objectReferenceValue = profile;
            surface.FindPropertyRelative("productName").stringValue = profile.ProductName;
            surface.FindPropertyRelative("deviceOrdinal").intValue = deviceOrdinal;
            surface.FindPropertyRelative("inputBackend").enumValueIndex = (int)MidiInputBackend.RtMidiRaw;
            surface.FindPropertyRelative("rawInputPortOrdinal").intValue = input;
            surface.FindPropertyRelative("outputPortOrdinal").intValue = output;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        private static int FindAvailablePort(string[] ports, string productFragment, string preferredPortRole, ISet<int> usedOrdinals)
        {
            var fallback = -1;
            for (var i = 0; ports != null && i < ports.Length; i++)
            {
                var name = ports[i] ?? string.Empty;
                if (usedOrdinals != null && usedOrdinals.Contains(i)) continue;
                if (name.IndexOf(productFragment, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (name.IndexOf(preferredPortRole, StringComparison.OrdinalIgnoreCase) >= 0) return i;
                if (name.IndexOf("notes", StringComparison.OrdinalIgnoreCase) < 0 && fallback < 0) fallback = i;
            }
            return fallback;
        }

        internal static void EnsureDevMidiFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/MondmadeVJ0.Dev")) AssetDatabase.CreateFolder("Assets", "MondmadeVJ0.Dev");
            if (!AssetDatabase.IsValidFolder("Assets/MondmadeVJ0.Dev/Midi")) AssetDatabase.CreateFolder("Assets/MondmadeVJ0.Dev", "Midi");
        }
    }

    [CustomEditor(typeof(MidiSurfaceProfile))]
    public sealed class MidiSurfaceProfileEditor : UnityEditor.Editor
    {
        private int learnControlIndex;
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var profile = (MidiSurfaceProfile)target;
            var controls = profile.Controls;
            if (controls == null || controls.Count == 0) return;
            learnControlIndex = Mathf.Clamp(learnControlIndex, 0, controls.Count - 1);
            var labels = new string[controls.Count];
            for (var i = 0; i < labels.Length; i++) labels[i] = $"{controls[i].label} ({controls[i].id})";
            learnControlIndex = EditorGUILayout.Popup("Control to learn", learnControlIndex, labels);
            if (GUILayout.Button("Learn selected control"))
            {
                var hubs = UnityEngine.Object.FindObjectsByType<MidiHub>(FindObjectsSortMode.None);
                MidiHub matchingHub = null;
                foreach (var candidate in hubs)
                {
                    foreach (var surface in candidate.Surfaces)
                    {
                        if (surface?.Profile != profile) continue;
                        matchingHub = candidate;
                        break;
                    }
                    if (matchingHub != null) break;
                }
                if (matchingHub == null)
                    EditorUtility.DisplayDialog("MIDI Learn", "Add this profile to a MidiHub in MondmadeVJ0Runtime.Modules, then start listening in the Learn window. MIDI input is received in Edit Mode.", "OK");
                else MidiLearnWindow.OpenProfile(profile, learnControlIndex, matchingHub);
            }
        }
    }

    [CustomPropertyDrawer(typeof(MidiControlReference))]
    public sealed class MidiControlReferenceDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight * 2f + 4f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var surfaceId = property.FindPropertyRelative("surfaceInstanceId");
            var controlId = property.FindPropertyRelative("controlId");
            var hubs = UnityEngine.Object.FindObjectsByType<MidiHub>(FindObjectsSortMode.None);
            if (hubs.Length == 0)
            {
                EditorGUI.BeginProperty(position, label, property);
                var first = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
                EditorGUI.PropertyField(first, surfaceId, new GUIContent(label.text + " Surface"));
                var second = new Rect(position.x, position.y + EditorGUIUtility.singleLineHeight + 2f, position.width, EditorGUIUtility.singleLineHeight);
                EditorGUI.PropertyField(second, controlId, new GUIContent("Control ID"));
                EditorGUI.EndProperty();
                return;
            }

            var surfaceNames = new List<string>();
            var surfaceValues = new List<(string id, MidiHub hub, MidiSurfaceInstance surface)>();
            foreach (var hub in hubs)
                foreach (var surface in hub.Surfaces)
                    if (surface?.Profile != null) { surfaceNames.Add($"{surface.ProductName} [{surface.InstanceId.Substring(0, Math.Min(6, surface.InstanceId.Length))}]"); surfaceValues.Add((surface.InstanceId, hub, surface)); }
            if (surfaceNames.Count == 0)
            {
                EditorGUI.LabelField(position, label, new GUIContent("No configured MIDI surfaces"));
                return;
            }

            var selectedSurface = surfaceValues.FindIndex(value => value.id == surfaceId.stringValue);
            if (selectedSurface < 0) selectedSurface = 0;
            var firstLine = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            var labelWidth = EditorGUIUtility.labelWidth;
            var prefix = new Rect(firstLine.x, firstLine.y, labelWidth, firstLine.height);
            EditorGUI.LabelField(prefix, label);
            var learnRect = new Rect(firstLine.xMax - 52f, firstLine.y, 52f, firstLine.height);
            var popupRect = new Rect(prefix.xMax, firstLine.y, Mathf.Max(50f, firstLine.width - labelWidth - 56f), firstLine.height);
            var previousSurface = selectedSurface;
            selectedSurface = EditorGUI.Popup(popupRect, selectedSurface, surfaceNames.ToArray());
            var chosen = surfaceValues[selectedSurface];
            if (selectedSurface != previousSurface || string.IsNullOrEmpty(surfaceId.stringValue) || surfaceValues.FindIndex(value => value.id == surfaceId.stringValue) < 0)
                surfaceId.stringValue = chosen.id;
            var controls = chosen.surface.Profile.Controls;
            var compatible = GetCompatibleControls(property, controls);
            var secondLine = new Rect(position.x + labelWidth, position.y + EditorGUIUtility.singleLineHeight + 2f, position.width - labelWidth, EditorGUIUtility.singleLineHeight);
            if (compatible.Count == 0)
            {
                EditorGUI.LabelField(secondLine, new GUIContent("Control"), new GUIContent("No compatible MIDI controls"));
                if (GUI.Button(learnRect, "Learn")) MidiLearnWindow.OpenReference(property.serializedObject.targetObject, property.propertyPath, chosen.hub, chosen.id);
                return;
            }

            var selectedControl = compatible.FindIndex(control => control.id == controlId.stringValue);
            var controlIsKnown = selectedControl >= 0;
            var labels = new List<string>(compatible.Count + (controlIsKnown ? 0 : 1));
            if (!controlIsKnown) labels.Add(string.IsNullOrEmpty(controlId.stringValue) ? "<Select control>" : $"<Missing or incompatible: {controlId.stringValue}>");
            foreach (var control in compatible) labels.Add($"{control.label} ({control.id})");
            var selectedLabel = controlIsKnown ? selectedControl : 0;
            var changedLabel = EditorGUI.Popup(secondLine, selectedLabel, labels.ToArray());
            if (changedLabel != selectedLabel)
            {
                var controlIndex = controlIsKnown ? changedLabel : changedLabel - 1;
                if (controlIndex >= 0 && controlIndex < compatible.Count) controlId.stringValue = compatible[controlIndex].id;
            }
            if (GUI.Button(learnRect, "Learn")) MidiLearnWindow.OpenReference(property.serializedObject.targetObject, property.propertyPath, chosen.hub, chosen.id);
        }

        internal static List<MidiSurfaceControl> GetCompatibleControls(SerializedProperty property, IReadOnlyList<MidiSurfaceControl> controls)
        {
            var owner = FindConstraintOwner(property, out var memberName);
            var result = new List<MidiSurfaceControl>();
            for (var i = 0; i < controls.Count; i++)
            {
                var control = controls[i];
                if (control != null && (owner == null || owner.AcceptsControlType(memberName, control.type))) result.Add(control);
            }
            return result;
        }

        private static IMidiControlTypeConstraint FindConstraintOwner(SerializedProperty property, out string memberName)
        {
            memberName = null;
            IMidiControlTypeConstraint owner = null;
            object current = property.serializedObject.targetObject;
            var parts = property.propertyPath.Split('.');
            foreach (var part in parts)
            {
                if (current == null) return null;
                if (part == "Array") continue;
                if (part.StartsWith("data[", StringComparison.Ordinal) && part.EndsWith("]", StringComparison.Ordinal))
                {
                    if (current is not IList list || !int.TryParse(part.Substring(5, part.Length - 6), out var index) || index < 0 || index >= list.Count) return null;
                    current = list[index];
                    if (current is IMidiControlTypeConstraint itemConstraint)
                    {
                        owner = itemConstraint;
                        memberName = null;
                    }
                    continue;
                }

                if (owner != null && memberName == null) memberName = part;
                var field = FindField(current.GetType(), part);
                if (field == null) return null;
                current = field.GetValue(current);
                if (current is IMidiControlTypeConstraint constraint)
                {
                    owner = constraint;
                    memberName = null;
                }
            }
            return owner;
        }

        private static FieldInfo FindField(Type type, string name)
        {
            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                var field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }
    }

    public sealed class MidiLearnWindow : EditorWindow
    {
        [SerializeField]
        private MidiHub hub;
        [SerializeField]
        private string surfaceId;
        [SerializeField]
        private UnityEngine.Object targetObject;
        [SerializeField]
        private string propertyPath;
        [SerializeField]
        private MidiSurfaceProfile profile;
        [SerializeField]
        private int profileControlIndex = -1;
        [NonSerialized]
        private bool listening;
        [NonSerialized]
        private MidiHub listeningHub;
        [NonSerialized]
        private GameObject editorInputObject;
        [NonSerialized]
        private MidiHub editorInputHub;
        [NonSerialized]
        private RuntimeServices editorInputServices;
        [NonSerialized]
        private bool pumpingEditorInput;
        [NonSerialized]
        private bool editorInputCleanupPending;
        [SerializeField]
        private string lastMessage = "Select a surface and start listening.";

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static void OpenReference(UnityEngine.Object owner, string path, MidiHub sourceHub, string selectedSurface)
        {
            var window = GetWindow<MidiLearnWindow>("MIDI Learn");
            window.StopListening();
            window.targetObject = owner;
            window.propertyPath = path;
            window.profile = null;
            window.profileControlIndex = -1;
            window.hub = sourceHub;
            window.surfaceId = selectedSurface;
            window.Show();
        }

        public static void OpenProfile(MidiSurfaceProfile targetProfile, int controlIndex, MidiHub sourceHub)
        {
            var window = GetWindow<MidiLearnWindow>("MIDI Learn");
            window.StopListening();
            window.profile = targetProfile;
            window.profileControlIndex = controlIndex;
            window.targetObject = targetProfile;
            window.propertyPath = null;
            window.hub = sourceHub;
            window.surfaceId = null;
            foreach (var surface in sourceHub == null ? Array.Empty<MidiSurfaceInstance>() : sourceHub.Surfaces)
            {
                if (surface?.Profile != targetProfile) continue;
                window.surfaceId = surface.InstanceId;
                break;
            }
            window.Show();
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            StopListening();
        }

        private void OnGUI()
        {
            var selectedHub = (MidiHub)EditorGUILayout.ObjectField("MidiHub", hub, typeof(MidiHub), true);
            if (selectedHub != hub)
            {
                StopListening();
                hub = selectedHub;
                surfaceId = null;
            }
            if (hub == null)
            {
                EditorGUILayout.HelpBox("Select a MidiHub configured in MondmadeVJ0Runtime.Modules.", MessageType.Warning);
                return;
            }
            var surfaces = new List<MidiSurfaceInstance>();
            foreach (var surface in hub.Surfaces)
                if (surface?.Profile != null && (profile == null || surface.Profile == profile)) surfaces.Add(surface);
            if (surfaces.Count == 0)
            {
                EditorGUILayout.HelpBox(profile == null
                    ? "The selected MidiHub has no configured surface."
                    : "No configured surface on this MidiHub uses the selected profile.", MessageType.Warning);
                return;
            }
            var labels = new string[surfaces.Count];
            var statusHub = editorInputHub != null ? editorInputHub : hub;
            var selected = 0;
            for (var i = 0; i < surfaces.Count; i++)
            {
                labels[i] = $"{surfaces[i].ProductName} ({statusHub.GetSurfaceStatus(surfaces[i].InstanceId)})";
                if (surfaces[i].InstanceId == surfaceId) selected = i;
            }
            selected = EditorGUILayout.Popup("Surface", selected, labels);
            surfaceId = surfaces[selected].InstanceId;
            if (profile != null && profileControlIndex >= 0 && profileControlIndex < profile.Controls.Count)
                EditorGUILayout.LabelField("Profile control", $"{profile.Controls[profileControlIndex].label} ({profile.Controls[profileControlIndex].id})");
            else EditorGUILayout.LabelField("Route reference", targetObject == null ? "Not assigned" : propertyPath);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (listening)
                { if (GUILayout.Button("Cancel")) { StopListening(); lastMessage = "MIDI Learn cancelled."; } }
                else if (GUILayout.Button("Start listening")) StartListening();
            }
            var statusMessage = listening
                ? $"Listening in {(Application.isPlaying ? "Play Mode" : "Edit Mode")}. Move the control you want to assign."
                : lastMessage;
            EditorGUILayout.HelpBox(statusMessage, MessageType.Info);
        }

        private void StartListening()
        {
            StopListening();
            if (hub == null) { lastMessage = "Select a MidiHub before listening."; Repaint(); return; }
            try
            {
                if (Application.isPlaying)
                {
                    listeningHub = hub;
                }
                else
                {
                    CreateEditorInputHub();
                    listeningHub = editorInputHub;
                    EditorApplication.update += PumpEditorInput;
                }
                listeningHub.ControlChanged += OnControlChanged;
                if (profile != null) listeningHub.RawInputObserved += OnRawInput;
                listening = true;
                lastMessage = "Waiting for a MIDI message.";
            }
            catch (Exception exception)
            {
                StopListening();
                lastMessage = "Could not start MIDI Learn: " + exception.Message;
            }
            Repaint();
        }

        private void CreateEditorInputHub()
        {
            editorInputObject = new GameObject("Mondmade VJ0 MIDI Learn Input") { hideFlags = HideFlags.HideAndDontSave };
            editorInputHub = editorInputObject.AddComponent<MidiHub>();
            editorInputHub.hideFlags = HideFlags.HideAndDontSave;
            EditorUtility.CopySerialized(hub, editorInputHub);

            var serialized = new SerializedObject(editorInputHub);
            var surfaces = serialized.FindProperty("surfaces");
            if (surfaces == null || !surfaces.isArray)
                throw new InvalidOperationException("The selected MidiHub has no serialized surface list.");
            for (var i = 0; i < surfaces.arraySize; i++)
            {
                var outputPort = surfaces.GetArrayElementAtIndex(i).FindPropertyRelative("outputPortOrdinal");
                if (outputPort == null)
                    throw new InvalidOperationException("A MIDI surface is missing its output port setting.");
                outputPort.intValue = -1;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();

            editorInputServices = new RuntimeServices();
            editorInputHub.Register(editorInputServices);
            editorInputHub.Initialize(editorInputServices);
        }

        private void PumpEditorInput()
        {
            var activeHub = editorInputHub;
            var services = editorInputServices;
            if (!listening || activeHub == null || services == null) return;
            pumpingEditorInput = true;
            try
            {
                activeHub.DrainInputQueue(services, 4096);
                activeHub.Tick(services, 0d);
            }
            catch (Exception exception)
            {
                lastMessage = "Editor MIDI input failed: " + exception.Message;
                StopListening();
            }
            finally
            {
                pumpingEditorInput = false;
                if (editorInputCleanupPending) DisposeEditorInputHub();
            }
        }

        private void StopListening()
        {
            if (listeningHub != null)
            {
                listeningHub.ControlChanged -= OnControlChanged;
                listeningHub.RawInputObserved -= OnRawInput;
            }
            listeningHub = null;
            listening = false;
            EditorApplication.update -= PumpEditorInput;
            if (editorInputHub != null)
            {
                if (pumpingEditorInput) editorInputCleanupPending = true;
                else DisposeEditorInputHub();
            }
            Repaint();
        }

        private void DisposeEditorInputHub()
        {
            var inputHub = editorInputHub;
            var inputObject = editorInputObject;
            var services = editorInputServices;
            editorInputHub = null;
            editorInputObject = null;
            editorInputServices = null;
            editorInputCleanupPending = false;
            try { if (inputHub != null && services != null) inputHub.Shutdown(services); }
            catch (Exception exception) { Debug.LogException(exception); }
            if (inputObject != null) DestroyImmediate(inputObject);
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode)
            {
                StopListening();
                lastMessage = "Play mode changed. Start listening again when ready.";
            }
        }

        private void OnControlChanged(MidiControlChange change)
        {
            if (!listening || change.Reference.SurfaceInstanceId != surfaceId) return;
            if (profile != null) return;
            if (targetObject == null || string.IsNullOrEmpty(propertyPath)) { lastMessage = $"Received {change.Reference.ControlId}, but the target reference is gone."; StopListening(); Repaint(); return; }
            var serialized = new SerializedObject(targetObject);
            serialized.Update();
            var property = serialized.FindProperty(propertyPath);
            if (property == null) { lastMessage = "The target property is no longer available."; StopListening(); Repaint(); return; }
            var learnedSurface = property.FindPropertyRelative("surfaceInstanceId");
            var learnedControl = property.FindPropertyRelative("controlId");
            if (learnedSurface == null || learnedControl == null)
            { lastMessage = "The target MIDI reference fields are no longer available."; StopListening(); Repaint(); return; }
            Undo.RecordObject(targetObject, "Learn MIDI control");
            learnedSurface.stringValue = change.Reference.SurfaceInstanceId;
            learnedControl.stringValue = change.Reference.ControlId;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(targetObject);
            lastMessage = $"Assigned {change.Reference.ControlId}.";
            StopListening();
            InternalEditorUtility.RepaintAllViews();
            Repaint();
        }

        private void OnRawInput(MidiRawMessage message)
        {
            if (!listening || message.SurfaceInstanceId != surfaceId || profile == null || profileControlIndex < 0) return;
            if (profileControlIndex >= profile.Controls.Count || profile.Find(profile.Controls[profileControlIndex].id) == null)
            { lastMessage = "The selected profile control is no longer available."; StopListening(); Repaint(); return; }
            Undo.RecordObject(profile, "Learn MIDI address");
            if (listeningHub == null || !listeningHub.TrySetLearnedAddress(message.SurfaceInstanceId, profile.Controls[profileControlIndex].id, message.AddressType, message.Channel, message.Number))
            { lastMessage = "The selected logical control is no longer present on this surface."; StopListening(); Repaint(); return; }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            lastMessage = $"Learned {message.AddressType}, channel {message.Channel + 1}, number {message.Number}.";
            StopListening();
            InternalEditorUtility.RepaintAllViews();
            Repaint();
        }
    }

    internal static class MidiPortCatalog
    {
        public static string[] Inputs() => ReadPorts(true);
        public static string[] Outputs() => ReadPorts(false);
        private static string[] ReadPorts(bool input)
        {
            try
            {
                if (input)
                {
                    using var device = MidiIn.Create();
                    var result = new string[device.PortCount];
                    for (var i = 0; i < result.Length; i++) result[i] = device.GetPortName(i);
                    return result;
                }
                else
                {
                    using var device = MidiOut.Create();
                    var result = new string[device.PortCount];
                    for (var i = 0; i < result.Length; i++) result[i] = device.GetPortName(i);
                    return result;
                }
            }
            catch (Exception exception) { Debug.LogWarning($"[MondmadeVJ0] Could not enumerate MIDI ports: {exception.Message}"); return Array.Empty<string>(); }
        }
    }
}
