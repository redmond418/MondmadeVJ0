using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MondmadeVJ0;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MondmadeVJ0.Editor
{
    [CustomEditor(typeof(RouteHost))]
    public sealed class RouteHostEditor : UnityEditor.Editor
    {
        private static string clipboardJson;
        private static string clipboardType;
        private static object clipboardReferenceSnapshot;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var routes = serializedObject.FindProperty("routes");
            var routeList = new VisualElement();
            var runtimePanel = new IMGUIContainer(DrawRuntimeStatus);
            var validationPanel = new IMGUIContainer(DrawValidation);
            root.Add(new HelpBox("Value routes evaluate a Source once, then pass the retained value through Modifiers to each Output. Trigger routes carry events only.", HelpBoxMessageType.Info));
            root.Add(validationPanel);
            root.Add(runtimePanel);
            root.Add(routeList);
            root.Add(new Button(() => ShowTypeMenu(RouteTypes(), "Add route", type => AddRoute(type, Rebuild))) { text = "Add Route" });

            void Rebuild()
            {
                serializedObject.UpdateIfRequiredOrScript();
                routeList.Clear();
                for (var index = 0; index < routes.arraySize; index++)
                {
                    var routeProperty = routes.GetArrayElementAtIndex(index);
                    routeList.Add(BuildRouteCard(routeProperty, index, Rebuild));
                }
            }

            Rebuild();
            root.Bind(serializedObject);
            return root;
        }

        private VisualElement BuildRouteCard(SerializedProperty route, int index, Action rebuild)
        {
            var card = new Box();
            card.style.marginTop = 5;
            var routeObject = route.managedReferenceValue as IRoute;
            if (routeObject == null)
            {
                var missing = string.IsNullOrEmpty(route.managedReferenceFullTypename) ? "Empty route entry" : "Missing managed-reference type: " + route.managedReferenceFullTypename;
                card.Add(new HelpBox(missing + ". The serialized entry is preserved; choose a new type only if you intend to replace it.", HelpBoxMessageType.Warning));
                var recovery = new Button(() => ShowTypeMenu(RouteTypes(), "Replace route", type => { route.managedReferenceValue = Activator.CreateInstance(type); ApplyAndRebuild(rebuild); })) { text = "Replace with route type…" };
                card.Add(recovery);
                return card;
            }

            var title = new VisualElement();
            title.style.flexDirection = FlexDirection.Row;
            title.style.alignItems = Align.Center;
            var foldout = new Foldout { text = $"{routeObject.Name}  ·  {routeObject.ValueType.Name}", value = true };
            foldout.style.flexGrow = 1;
            title.Add(foldout);
            title.Add(new Button(() => MoveRoute(index, -1, rebuild)) { text = "↑" });
            title.Add(new Button(() => MoveRoute(index, 1, rebuild)) { text = "↓" });
            title.Add(new Button(() => CopyRoute(route)) { text = "Copy" });
            title.Add(new Button(() => PasteRoute(index, rebuild)) { text = "Paste" });
            title.Add(new Button(() => DuplicateRoute(index, rebuild)) { text = "Duplicate" });
            title.Add(new Button(() => DeleteRoute(index, rebuild)) { text = "Delete" });
            card.Add(title);

            var contents = new VisualElement();
            contents.style.marginLeft = 14;
            var name = route.FindPropertyRelative("routeName");
            var enabled = route.FindPropertyRelative("enabled");
            if (name != null) contents.Add(new PropertyField(name, "Name"));
            if (enabled != null) contents.Add(new PropertyField(enabled, "Enabled"));

            var source = route.FindPropertyRelative("source");
            contents.Add(BuildNodeSection(source, "Source", SourceTypes(routeObject), null, rebuild));
            var modifierInterface = routeObject is TriggerRoute ? typeof(ITriggerModifier) : typeof(IValueModifier<>).MakeGenericType(routeObject.ValueType);
            contents.Add(BuildNodeList(route.FindPropertyRelative("modifiers"), "Modifiers", NodeTypes(modifierInterface), rebuild));
            var outputInterface = routeObject is TriggerRoute ? typeof(ITriggerOutput) : typeof(IValueOutput<>).MakeGenericType(routeObject.ValueType);
            contents.Add(BuildNodeList(route.FindPropertyRelative("outputs"), "Outputs", NodeTypes(outputInterface), rebuild));
            foldout.Add(contents);
            card.Add(foldout);
            return card;
        }

        private VisualElement BuildNodeSection(SerializedProperty nodeProperty, string title, IReadOnlyList<Type> types, int? index, Action rebuild)
        {
            var section = new Box();
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.Add(new Label(title) { style = { minWidth = 85 } });
            if (nodeProperty?.managedReferenceValue == null)
                row.Add(new Button(() => ShowTypeMenu(types, "Add " + title, type => { nodeProperty.managedReferenceValue = Activator.CreateInstance(type); ApplyAndRebuild(rebuild); })) { text = "Add " + title });
            else
            {
                row.Add(new Label(ShortTypeName(nodeProperty.managedReferenceValue.GetType())) { style = { flexGrow = 1 } });
                row.Add(new Button(() => CopyNode(nodeProperty)) { text = "Copy" });
                row.Add(new Button(() => PasteNode(nodeProperty, rebuild)) { text = "Paste" });
                row.Add(new Button(() => ShowTypeMenu(types, "Change " + title, type => { nodeProperty.managedReferenceValue = Activator.CreateInstance(type); ApplyAndRebuild(rebuild); })) { text = "Change" });
                row.Add(new Button(() => { nodeProperty.managedReferenceValue = null; ApplyAndRebuild(rebuild); }) { text = "Remove" });
            }
            section.Add(row);
            if (nodeProperty?.managedReferenceValue != null)
                section.Add(new PropertyField(nodeProperty));
            else if (!string.IsNullOrEmpty(nodeProperty?.managedReferenceFullTypename))
                section.Add(new HelpBox("Missing node type preserved: " + nodeProperty.managedReferenceFullTypename, HelpBoxMessageType.Warning));
            return section;
        }

        private VisualElement BuildNodeList(SerializedProperty list, string title, IReadOnlyList<Type> types, Action rebuild)
        {
            var section = new Box();
            section.Add(new Label(title) { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            if (list == null) return section;
            for (var i = 0; i < list.arraySize; i++)
            {
                var element = list.GetArrayElementAtIndex(i);
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                var type = element.managedReferenceValue?.GetType();
                row.Add(new Label(type == null ? "Missing node type" : ShortTypeName(type)) { style = { flexGrow = 1 } });
                var elementIndex = i;
                row.Add(new Button(() => MoveNode(list.propertyPath, elementIndex, -1, rebuild)) { text = "↑" });
                row.Add(new Button(() => MoveNode(list.propertyPath, elementIndex, 1, rebuild)) { text = "↓" });
                row.Add(new Button(() => CopyNode(element)) { text = "Copy" });
                row.Add(new Button(() => PasteNodeAt(list.propertyPath, elementIndex, rebuild)) { text = "Paste" });
                row.Add(new Button(() => DuplicateNode(list.propertyPath, elementIndex, rebuild)) { text = "Duplicate" });
                row.Add(new Button(() => DeleteNode(list.propertyPath, elementIndex, rebuild)) { text = "Delete" });
                section.Add(row);
                if (type == null && !string.IsNullOrEmpty(element.managedReferenceFullTypename))
                    section.Add(new HelpBox("Missing type preserved: " + element.managedReferenceFullTypename, HelpBoxMessageType.Warning));
                else if (type != null)
                    section.Add(new PropertyField(element));
            }
            section.Add(new Button(() => ShowTypeMenu(types, "Add " + title, type => AddNode(list.propertyPath, type, rebuild))) { text = "Add " + title.TrimEnd('s') });
            return section;
        }

        private void DrawRuntimeStatus()
        {
            if (!Application.isPlaying) return;
            var host = (RouteHost)target;
            EditorGUILayout.LabelField("Runtime route status", EditorStyles.boldLabel);
            foreach (var route in host.Routes)
            {
                if (route == null) continue;
                var valueProperty = route.GetType().GetProperty("CurrentValue");
                var hasValueProperty = route.GetType().GetProperty("HasCurrentValue");
                var hasValue = hasValueProperty != null && (bool)hasValueProperty.GetValue(route);
                var value = hasValue && valueProperty != null ? valueProperty.GetValue(route)?.ToString() : route.Status;
                EditorGUILayout.LabelField(route.Name, $"{route.Status} · {value}");
            }
        }

        private void DrawValidation()
        {
            var errors = MondmadeVJ0Validation.Validate((RouteHost)target);
            if (errors.Count == 0) EditorGUILayout.HelpBox("Route structure is valid.", MessageType.Info);
            else
            {
                EditorGUILayout.HelpBox($"Validation found {errors.Count} issue(s):", MessageType.Error);
                foreach (var error in errors) EditorGUILayout.LabelField("• " + error, EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void AddRoute(Type type, Action rebuild)
        {
            serializedObject.Update();
            var list = serializedObject.FindProperty("routes");
            var index = list.arraySize++;
            list.GetArrayElementAtIndex(index).managedReferenceValue = Activator.CreateInstance(type);
            ApplyAndRebuild(rebuild);
        }

        private void MoveRoute(int index, int direction, Action rebuild)
        {
            var list = serializedObject.FindProperty("routes");
            var destination = index + direction;
            if (destination < 0 || destination >= list.arraySize) return;
            serializedObject.Update();
            list.MoveArrayElement(index, destination);
            ApplyAndRebuild(rebuild);
        }

        private void DuplicateRoute(int index, Action rebuild)
        {
            var list = serializedObject.FindProperty("routes");
            serializedObject.Update();
            var clone = CloneManagedReference(list.GetArrayElementAtIndex(index).managedReferenceValue);
            RegenerateIdentity(clone, true);
            list.InsertArrayElementAtIndex(index + 1);
            list.GetArrayElementAtIndex(index + 1).managedReferenceValue = clone;
            ApplyAndRebuild(rebuild);
        }

        private void CopyRoute(SerializedProperty route) => CopyManagedReference(route.managedReferenceValue);

        private void PasteRoute(int index, Action rebuild)
        {
            if (!CanPaste(typeof(IRoute))) return;
            var list = serializedObject.FindProperty("routes");
            serializedObject.Update();
            var clone = PasteManagedReference();
            if (!(clone is IRoute)) return;
            RegenerateIdentity(clone, true);
            list.GetArrayElementAtIndex(index).managedReferenceValue = clone;
            ApplyAndRebuild(rebuild);
        }

        private void DeleteRoute(int index, Action rebuild)
        {
            serializedObject.Update();
            var list = serializedObject.FindProperty("routes");
            list.DeleteArrayElementAtIndex(index);
            ApplyAndRebuild(rebuild);
        }

        private void AddNode(string path, Type type, Action rebuild)
        {
            serializedObject.Update();
            var list = serializedObject.FindProperty(path);
            var index = list.arraySize++;
            list.GetArrayElementAtIndex(index).managedReferenceValue = Activator.CreateInstance(type);
            ApplyAndRebuild(rebuild);
        }

        private void MoveNode(string path, int index, int direction, Action rebuild)
        {
            serializedObject.Update();
            var list = serializedObject.FindProperty(path);
            var destination = index + direction;
            if (destination < 0 || destination >= list.arraySize) return;
            list.MoveArrayElement(index, destination);
            ApplyAndRebuild(rebuild);
        }

        private void DuplicateNode(string path, int index, Action rebuild)
        {
            serializedObject.Update();
            var list = serializedObject.FindProperty(path);
            var clone = CloneManagedReference(list.GetArrayElementAtIndex(index).managedReferenceValue);
            RegenerateIdentity(clone, false);
            list.InsertArrayElementAtIndex(index + 1);
            list.GetArrayElementAtIndex(index + 1).managedReferenceValue = clone;
            ApplyAndRebuild(rebuild);
        }

        private void CopyNode(SerializedProperty property) => CopyManagedReference(property.managedReferenceValue);

        private void PasteNode(SerializedProperty property, Action rebuild)
        {
            if (!CanPaste(typeof(RouteNode))) return;
            serializedObject.Update();
            var clone = PasteManagedReference();
            if (!(clone is RouteNode)) return;
            RegenerateIdentity(clone, false);
            property.managedReferenceValue = clone;
            ApplyAndRebuild(rebuild);
        }

        private void PasteNodeAt(string path, int index, Action rebuild)
        {
            if (!CanPaste(typeof(RouteNode))) return;
            serializedObject.Update();
            var property = serializedObject.FindProperty(path).GetArrayElementAtIndex(index);
            var clone = PasteManagedReference();
            if (!(clone is RouteNode)) return;
            RegenerateIdentity(clone, false);
            property.managedReferenceValue = clone;
            ApplyAndRebuild(rebuild);
        }

        private void DeleteNode(string path, int index, Action rebuild)
        {
            serializedObject.Update();
            serializedObject.FindProperty(path).DeleteArrayElementAtIndex(index);
            ApplyAndRebuild(rebuild);
        }

        private void ApplyAndRebuild(Action rebuild)
        {
            Undo.RecordObject(target, "Edit Mondmade VJ0 routes");
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            if (rebuild != null) rebuild();
            else Repaint();
        }

        private static void CopyManagedReference(object value)
        {
            if (value == null) return;
            clipboardType = value.GetType().AssemblyQualifiedName;
            clipboardJson = JsonUtility.ToJson(value);
            clipboardReferenceSnapshot = CloneManagedReference(value);
        }

        private static bool CanPaste(Type contract)
        {
            var type = Type.GetType(clipboardType);
            return type != null && contract.IsAssignableFrom(type);
        }

        private static object PasteManagedReference()
        {
            var type = Type.GetType(clipboardType);
            if (type == null) return null;
            var clone = Activator.CreateInstance(type);
            JsonUtility.FromJsonOverwrite(clipboardJson, clone);
            if (clipboardReferenceSnapshot != null)
                PreserveUnityObjectReferences(clipboardReferenceSnapshot, clone, new HashSet<object>());
            return clone;
        }

        private static object CloneManagedReference(object value)
        {
            if (value == null) return null;
            var json = JsonUtility.ToJson(value);
            var clone = Activator.CreateInstance(value.GetType());
            JsonUtility.FromJsonOverwrite(json, clone);
            PreserveUnityObjectReferences(value, clone, new HashSet<object>());
            return clone;
        }

        private static void PreserveUnityObjectReferences(object source, object destination, HashSet<object> visited)
        {
            if (source == null || destination == null || source is UnityEngine.Object || !visited.Add(source)) return;
            for (var type = source.GetType(); type != null && type != typeof(object); type = type.BaseType)
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.IsStatic || field.IsNotSerialized || (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null && field.GetCustomAttribute<SerializeReference>() == null)) continue;
                var sourceValue = field.GetValue(source);
                if (sourceValue == null) continue;
                var destinationValue = field.GetValue(destination);
                if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType))
                {
                    field.SetValue(destination, sourceValue);
                    continue;
                }
                if (sourceValue is Array sourceArray)
                {
                    var destinationArray = destinationValue as Array;
                    if (destinationArray == null || destinationArray.Length != sourceArray.Length)
                        destinationArray = Array.CreateInstance(field.FieldType.GetElementType(), sourceArray.Length);
                    for (var index = 0; index < sourceArray.Length; index++)
                    {
                        var item = sourceArray.GetValue(index);
                        if (item is UnityEngine.Object) destinationArray.SetValue(item, index);
                        else if (item != null && destinationArray.GetValue(index) != null)
                            PreserveUnityObjectReferences(item, destinationArray.GetValue(index), visited);
                        else if (item != null && item.GetType().IsValueType)
                        {
                            var copy = item;
                            PreserveUnityObjectReferences(item, copy, visited);
                            destinationArray.SetValue(copy, index);
                        }
                    }
                    field.SetValue(destination, destinationArray);
                    continue;
                }
                if (sourceValue is System.Collections.IList sourceList && destinationValue is System.Collections.IList destinationList)
                {
                    var count = Math.Min(sourceList.Count, destinationList.Count);
                    for (var index = 0; index < count; index++)
                    {
                        var item = sourceList[index];
                        if (item is UnityEngine.Object) destinationList[index] = item;
                        else if (item != null && destinationList[index] != null)
                            PreserveUnityObjectReferences(item, destinationList[index], visited);
                    }
                    continue;
                }
                if (destinationValue != null && (field.GetCustomAttribute<SerializeReference>() != null || sourceValue.GetType().IsSerializable))
                {
                    var copy = destinationValue;
                    PreserveUnityObjectReferences(sourceValue, copy, visited);
                    if (field.FieldType.IsValueType) field.SetValue(destination, copy);
                }
            }
        }

        private static void RegenerateIdentity(object value, bool route)
        {
            if (value == null) return;
            if (route) value.GetType().GetMethod("RegenerateId")?.Invoke(value, null);
            if (value is RouteNode node) node.RegenerateId();
            foreach (var field in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.GetCustomAttribute<SerializeReference>() == null) continue;
                if (field.GetValue(value) is RouteNode child) RegenerateIdentity(child, false);
                else if (field.GetValue(value) is System.Collections.IEnumerable list)
                    foreach (var item in list) if (item is RouteNode itemNode) RegenerateIdentity(itemNode, false);
            }
        }

        private static IReadOnlyList<Type> RouteTypes() => AllTypes().Where(type => typeof(IRoute).IsAssignableFrom(type) && !type.IsAbstract && !type.ContainsGenericParameters).ToArray();
        private static IReadOnlyList<Type> SourceTypes(IRoute route) => NodeTypes(route is TriggerRoute ? typeof(ITriggerSource) : typeof(IValueSource<>).MakeGenericType(route.ValueType));
        private static IReadOnlyList<Type> NodeTypes(Type contract) => AllTypes().Where(type => contract.IsAssignableFrom(type) && typeof(RouteNode).IsAssignableFrom(type) && !type.IsAbstract && !type.ContainsGenericParameters).ToArray();

        private static IEnumerable<Type> AllTypes()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException exception) { types = exception.Types.Where(type => type != null).ToArray(); }
                catch { continue; }
                foreach (var type in types)
                    if (type.IsClass && type.IsDefined(typeof(SerializableAttribute), false)) yield return type;
            }
        }

        private static string ShortTypeName(Type type) => type.Name.Replace("`1", string.Empty);

        private static void ShowTypeMenu(IEnumerable<Type> types, string title, Action<Type> selected)
        {
            RouteNodeTypePickerWindow.Open(title, types.OrderBy(value => value.Name, StringComparer.Ordinal).ToArray(), selected);
        }
    }

    internal sealed class RouteNodeTypePickerWindow : EditorWindow
    {
        private string pickerTitle;
        private Type[] types = Array.Empty<Type>();
        private Action<Type> selected;
        private string search = string.Empty;
        private Vector2 scroll;

        public static void Open(string title, Type[] types, Action<Type> selected)
        {
            var window = CreateInstance<RouteNodeTypePickerWindow>();
            window.pickerTitle = title;
            window.types = types ?? Array.Empty<Type>();
            window.selected = selected;
            window.titleContent = new GUIContent(title);
            window.minSize = new Vector2(340f, 320f);
            var main = EditorGUIUtility.GetMainWindowPosition();
            window.position = new Rect(main.x + (main.width - 380f) * 0.5f, main.y + (main.height - 460f) * 0.5f, 380f, 460f);
            window.ShowUtility();
            window.Focus();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(pickerTitle, EditorStyles.boldLabel);
            search = EditorGUILayout.TextField("Search", search);
            var filtered = types.Where(type => string.IsNullOrEmpty(search)
                || type.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                || (!string.IsNullOrEmpty(type.Namespace) && type.Namespace.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var type in filtered)
            {
                if (!GUILayout.Button(type.Name, EditorStyles.miniButton)) continue;
                selected?.Invoke(type);
                Close();
                GUIUtility.ExitGUI();
            }
            if (filtered.Length == 0) EditorGUILayout.HelpBox("No matching types.", MessageType.Info);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField($"{filtered.Length} of {types.Length} types", EditorStyles.miniLabel);
        }
    }

    [CustomEditor(typeof(MondmadeVJ0Runtime))]
    public sealed class MondmadeVJ0RuntimeEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(new PropertyField(serializedObject.FindProperty("modules"), "Runtime modules"));
            root.Add(new PropertyField(serializedObject.FindProperty("routeHosts"), "Route hosts"));
            root.Add(new IMGUIContainer(DrawRuntimeControls));
            root.Bind(serializedObject);
            return root;
        }

        private void DrawRuntimeControls()
        {
            var runtime = (MondmadeVJ0Runtime)target;
            if (!Application.isPlaying || runtime.Services == null) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Retry faulted routes")) runtime.RetryFaultedRoutes();
                if (GUILayout.Button("Reset route state")) runtime.ResetAllState();
                if (GUILayout.Button("Open Monitor")) RuntimeMonitorWindow.Open(runtime);
            }
            var diagnostics = runtime.Services.GetDiagnostics();
            EditorGUILayout.LabelField($"Diagnostics: {diagnostics.Count} · Dropped: {runtime.Services.DroppedDiagnosticCount}", EditorStyles.miniBoldLabel);
        }
    }

    public sealed class RuntimeMonitorWindow : EditorWindow
    {
        private MondmadeVJ0Runtime runtime;
        private Vector2 scroll;

        public static void Open(MondmadeVJ0Runtime runtime)
        {
            var window = GetWindow<RuntimeMonitorWindow>("Mondmade VJ0 Monitor");
            window.runtime = runtime;
            window.Show();
        }

        private void OnGUI()
        {
            runtime = (MondmadeVJ0Runtime)EditorGUILayout.ObjectField("Runtime", runtime, typeof(MondmadeVJ0Runtime), true);
            if (runtime == null || runtime.Services == null)
            {
                EditorGUILayout.HelpBox("Enter Play mode and assign a MondmadeVJ0Runtime to inspect live state.", MessageType.Info);
                return;
            }
            if (GUILayout.Button("Retry faulted routes")) runtime.RetryFaultedRoutes();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Runtime modules", EditorStyles.boldLabel);
            foreach (var module in runtime.ConfiguredModules)
            {
                if (module == null) continue;
                var values = ReadModuleSummary(module);
                EditorGUILayout.LabelField(module.GetType().Name, values);
            }
            EditorGUILayout.Space();
            foreach (var host in runtime.RouteHosts)
            {
                if (host == null) continue;
                EditorGUILayout.LabelField(host.name, EditorStyles.boldLabel);
                foreach (var route in host.Routes)
                {
                    if (route == null) continue;
                    EditorGUILayout.LabelField(route.Name, route.Status + " · " + CurrentValue(route));
                    if (route is ValueRoute<float> f) DrawOutputs(route, f.Outputs.Count, f.GetOutputStatus);
                    else if (route is ValueRoute<int> i) DrawOutputs(route, i.Outputs.Count, i.GetOutputStatus);
                    else if (route is ValueRoute<bool> b) DrawOutputs(route, b.Outputs.Count, b.GetOutputStatus);
                    else if (route is ValueRoute<Vector2> v2) DrawOutputs(route, v2.Outputs.Count, v2.GetOutputStatus);
                    else if (route is ValueRoute<Vector3> v3) DrawOutputs(route, v3.Outputs.Count, v3.GetOutputStatus);
                    else if (route is ValueRoute<Vector4> v4) DrawOutputs(route, v4.Outputs.Count, v4.GetOutputStatus);
                    else if (route is ValueRoute<Color> color) DrawOutputs(route, color.Outputs.Count, color.GetOutputStatus);
                }
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Diagnostics", EditorStyles.boldLabel);
            foreach (var diagnostic in runtime.Services.GetDiagnostics().Reverse())
                EditorGUILayout.HelpBox($"{diagnostic.TimeUtc:HH:mm:ss} [{diagnostic.Code}] {diagnostic.Message}", diagnostic.Exception == null ? MessageType.Warning : MessageType.Error);
            EditorGUILayout.EndScrollView();
        }

        private void OnEnable() => EditorApplication.update += RefreshWhilePlaying;
        private void OnDisable() => EditorApplication.update -= RefreshWhilePlaying;
        private void RefreshWhilePlaying() { if (Application.isPlaying && runtime != null) Repaint(); }

        private static string ReadModuleSummary(object module)
        {
            var names = new[] { "Status", "SyncState", "EffectiveBpm", "BeatPosition", "ReceivedCount", "SentCount", "DroppedCount", "MissedPulses", "MalformedCount", "P99LatenessMilliseconds", "MaximumLatenessMilliseconds", "MaximumDeadlineDriftMilliseconds" };
            var values = new List<string>();
            foreach (var name in names)
            {
                var property = module.GetType().GetProperty(name);
                if (property == null || property.GetIndexParameters().Length != 0) continue;
                try { values.Add(name + "=" + property.GetValue(module)); }
                catch { }
            }
            return values.Count == 0 ? "Ready" : string.Join(" · ", values);
        }

        private static void DrawOutputs(IRoute route, int count, Func<int, string> status)
        {
            for (var index = 0; index < count; index++) EditorGUILayout.LabelField("  Output " + (index + 1), status(index), EditorStyles.miniLabel);
        }

        private static string CurrentValue(IRoute route)
        {
            var property = route.GetType().GetProperty("CurrentValue");
            var hasValue = route.GetType().GetProperty("HasCurrentValue");
            if (property == null || hasValue == null || !(bool)hasValue.GetValue(route)) return route.Status;
            return property.GetValue(route)?.ToString() ?? "null";
        }
    }

    internal static class MondmadeVJ0Validation
    {
        public static List<string> Validate(RouteHost host)
        {
            var errors = new List<string>();
            if (host == null) return errors;
            var routeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var route in host.Routes)
            {
                if (route == null) { errors.Add($"{host.name}: null route or missing serialized type."); continue; }
                if (string.IsNullOrWhiteSpace(route.Name)) errors.Add($"{host.name}: route {route.Id} has no name.");
                if (!routeIds.Add(route.Id)) errors.Add($"{host.name}/{route.Name}: duplicate route id {route.Id}.");
                var source = GetRouteSource(route);
                if (source == null) errors.Add($"{host.name}/{route.Name}: Source is not assigned.");
                var nodeIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var node in EnumerateNodes(route))
                {
                    if (node == null) { errors.Add($"{host.name}/{route.Name}: missing node or type."); continue; }
                    if (!nodeIds.Add(node.NodeId)) errors.Add($"{host.name}/{route.Name}: duplicate node id {node.NodeId}.");
                    ValidateNode(host, route, node, errors);
                }
            }
            ValidateSignalCycles(GetSceneHosts(host), errors);
            return errors;
        }

        private static IEnumerable<RouteHost> GetSceneHosts(RouteHost host)
        {
            var scene = host.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded) return new[] { host };
            var hosts = new List<RouteHost>();
            foreach (var root in scene.GetRootGameObjects()) hosts.AddRange(root.GetComponentsInChildren<RouteHost>(true));
            return hosts;
        }

        internal static object GetRouteSource(IRoute route)
        {
            var property = route.GetType().GetProperty("Source");
            return property?.GetValue(route);
        }

        internal static IEnumerable<RouteNode> EnumerateNodes(IRoute route)
        {
            if (route == null) yield break;
            var visited = new HashSet<RouteNode>();
            if (GetRouteSource(route) is RouteNode source)
                foreach (var node in EnumerateNodeTree(source, visited)) yield return node;
            foreach (var propertyName in new[] { "Modifiers", "Outputs" })
            {
                var property = route.GetType().GetProperty(propertyName);
                if (!(property?.GetValue(route) is System.Collections.IEnumerable values)) continue;
                foreach (var value in values)
                    if (value is RouteNode node)
                        foreach (var child in EnumerateNodeTree(node, visited)) yield return child;
                    else if (value == null) yield return null;
            }
        }

        private static IEnumerable<RouteNode> EnumerateNodeTree(RouteNode node, HashSet<RouteNode> visited)
        {
            if (node == null) { yield return null; yield break; }
            if (!visited.Add(node)) yield break;
            yield return node;
            for (var type = node.GetType(); type != null && type != typeof(object); type = type.BaseType)
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsStatic || field.IsNotSerialized || field.GetCustomAttribute<SerializeReference>() == null) continue;
                    var value = field.GetValue(node);
                    if (value is RouteNode child)
                    {
                        foreach (var nested in EnumerateNodeTree(child, visited)) yield return nested;
                    }
                    else if (value is System.Collections.IEnumerable values)
                    {
                        foreach (var item in values)
                            if (item is RouteNode nestedNode)
                                foreach (var nested in EnumerateNodeTree(nestedNode, visited)) yield return nested;
                            else if (item == null) yield return null;
                    }
                    else if (value == null) yield return null;
                }
        }

        private static void ValidateNode(RouteHost host, IRoute route, RouteNode node, List<string> errors)
        {
            for (var type = node.GetType(); type != null && type != typeof(object); type = type.BaseType)
            {
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsStatic || field.IsNotSerialized) continue;
                    var value = field.GetValue(node);
                    if (field.GetCustomAttribute<SerializeReference>() != null && value == null)
                        errors.Add($"{host.name}/{route.Name}/{type.Name}: connected node '{field.Name}' is missing.");
                    if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType) && value == null && IsRequiredTargetField(field.Name))
                        errors.Add($"{host.name}/{route.Name}/{type.Name}: required reference '{field.Name}' is missing.");
                    if (value is Array array && typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType.GetElementType()))
                        for (var i = 0; i < array.Length; i++) if (array.GetValue(i) == null) errors.Add($"{host.name}/{route.Name}/{type.Name}: reference '{field.Name}[{i}]' is missing.");
                    if (value is System.Collections.ICollection collection && collection.Count == 0 && IsRequiredArrayField(field.Name))
                        errors.Add($"{host.name}/{route.Name}/{type.Name}: '{field.Name}' requires at least one entry.");
                    if (field.FieldType == typeof(string) && value is string text && IsRequiredStringField(field.Name) && string.IsNullOrWhiteSpace(text))
                        errors.Add($"{host.name}/{route.Name}/{type.Name}: '{field.Name}' is empty.");
                    ValidateNumericField(host, route, node, type, field, value, errors);
                }
            }
            ValidateAotAccessor(host, route, node, errors);
        }

        private static void ValidateNumericField(RouteHost host, IRoute route, RouteNode node, Type declaringType, FieldInfo field, object value, List<string> errors)
        {
            if (value == null) return;
            var min = field.GetCustomAttribute<MinAttribute>();
            var range = field.GetCustomAttribute<RangeAttribute>();
            var numeric = field.FieldType == typeof(float) || field.FieldType == typeof(double) || field.FieldType == typeof(int);
            if (!numeric) return;
            var number = Convert.ToDouble(value);
            if (double.IsNaN(number) || double.IsInfinity(number))
            {
                var unlimitedSmoothSpeed = double.IsPositiveInfinity(number)
                    && field.Name == "maxSpeed"
                    && declaringType.Name.StartsWith("SmoothDamp", StringComparison.Ordinal);
                if (unlimitedSmoothSpeed) return;
                errors.Add($"{host.name}/{route.Name}/{declaringType.Name}: '{field.Name}' must be finite.");
                return;
            }
            if (min != null && number < min.min) errors.Add($"{host.name}/{route.Name}/{declaringType.Name}: '{field.Name}' must be at least {min.min}.");
            if (range != null && (number < range.min || number > range.max)) errors.Add($"{host.name}/{route.Name}/{declaringType.Name}: '{field.Name}' must be in [{range.min}, {range.max}].");
            var maximumField = declaringType.GetField("maximum", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                            ?? declaringType.GetField("max", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (maximumField != null && (field.Name == "minimum" || field.Name == "min") && IsScalarNumeric(maximumField.FieldType))
            {
                var maximum = Convert.ToDouble(maximumField.GetValue(node));
                if (number > maximum) errors.Add($"{host.name}/{route.Name}/{declaringType.Name}: minimum must not exceed maximum.");
            }
        }

        private static void ValidateAotAccessor(RouteHost host, IRoute route, RouteNode node, List<string> errors)
        {
            for (var type = node.GetType(); type != null; type = type.BaseType)
            {
                if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(ComponentMemberOutput<>)) continue;
                var target = type.GetProperty("Target")?.GetValue(node) as Component;
                var member = type.GetProperty("MemberName")?.GetValue(node) as string;
                if (target == null || string.IsNullOrWhiteSpace(member)) return;
                var valueType = type.GetGenericArguments()[0];
                var key = ComponentAccessorGenerator.Key(target.GetType().AssemblyQualifiedName, member, valueType.AssemblyQualifiedName);
                if (!ComponentAccessorGenerator.GeneratedKeys().Contains(key))
                    errors.Add($"{host.name}/{route.Name}/{node.GetType().Name}: generate the IL2CPP accessor for {target.GetType().Name}.{member}.");
                return;
            }
        }

        private static bool IsRequiredTargetField(string field) => field is "target" or "signal" or "profile" or "action";
        private static bool IsRequiredArrayField(string field) => field is "objects" or "clips" or "textures" or "controls" or "actions" or "inputs";
        private static bool IsRequiredStringField(string field) => field is "memberName" or "propertyName" or "parameterName" or "eventName" or "stateName" or "componentTypeName";
        private static bool IsScalarNumeric(Type type) => type == typeof(float) || type == typeof(double) || type == typeof(int);

        private static void ValidateSignalCycles(IEnumerable<RouteHost> hosts, List<string> errors)
        {
            var edges = new Dictionary<SharedSignalBase, HashSet<SharedSignalBase>>();
            foreach (var host in hosts)
            foreach (var route in host.Routes)
            {
                if (route == null) continue;
                var nodes = EnumerateNodes(route).Where(node => node != null).ToArray();
                var reads = nodes.Select(FindSignal).Where(signal => signal != null).Distinct().ToArray();
                var writes = nodes.OfType<ISharedSignalOutput>().Select(output => output.SignalOutput).Where(signal => signal != null).Distinct().ToArray();
                foreach (var read in reads)
                foreach (var write in writes)
                {
                    if (!edges.TryGetValue(read, out var destinations)) edges.Add(read, destinations = new HashSet<SharedSignalBase>());
                    destinations.Add(write);
                }
            }
            var visiting = new HashSet<SharedSignalBase>();
            var visited = new HashSet<SharedSignalBase>();
            foreach (var signal in edges.Keys) if (HasCycle(signal, edges, visiting, visited)) { errors.Add($"Scene '{hosts.FirstOrDefault()?.gameObject.scene.name}': Shared Signal feedback cycle detected at '{signal.name}'."); return; }
        }

        private static bool HasCycle(SharedSignalBase node, Dictionary<SharedSignalBase, HashSet<SharedSignalBase>> edges, HashSet<SharedSignalBase> visiting, HashSet<SharedSignalBase> visited)
        {
            if (visiting.Contains(node)) return true;
            if (!visited.Add(node)) return false;
            visiting.Add(node);
            if (edges.TryGetValue(node, out var next)) foreach (var child in next) if (HasCycle(child, edges, visiting, visited)) return true;
            visiting.Remove(node);
            return false;
        }

        private static SharedSignalBase FindSignal(object node)
        {
            if (node == null) return null;
            var property = node.GetType().GetProperty("Signal", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var value = property != null ? property.GetValue(node) : node.GetType().GetField("Signal", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(node);
            return value as SharedSignalBase;
        }
    }

    [InitializeOnLoad]
    internal static class MondmadeVJ0PlayValidation
    {
        static MondmadeVJ0PlayValidation() => EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode) return;
            var allErrors = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var host in root.GetComponentsInChildren<RouteHost>(true))
                        foreach (var error in MondmadeVJ0Validation.Validate(host)) allErrors.Add(error);
            }
            if (allErrors.Count == 0) return;
            foreach (var error in allErrors) Debug.LogError("[MondmadeVJ0 Validation] " + error);
            EditorUtility.DisplayDialog("Mondmade VJ0 Validation", $"Found {allErrors.Count} route issue(s). Details are in the Console. Play mode will continue so existing projects can be diagnosed.", "OK");
        }
    }
}
