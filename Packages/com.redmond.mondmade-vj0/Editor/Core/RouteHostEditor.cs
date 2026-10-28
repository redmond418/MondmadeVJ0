using System;
using System.Collections;
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
        private static readonly Dictionary<Type, bool> customPropertyDrawerCache = new Dictionary<Type, bool>();
        private Action rebuildInspector;

        private void OnEnable() => Undo.undoRedoPerformed += OnUndoRedoPerformed;

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            rebuildInspector = null;
        }

        private void OnUndoRedoPerformed()
        {
            if (target == null || rebuildInspector == null) return;
            serializedObject.UpdateIfRequiredOrScript();
            rebuildInspector();
            Repaint();
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var routeList = new VisualElement();
            var runtimePanel = new IMGUIContainer(DrawRuntimeStatus);
            var validationPanel = new IMGUIContainer(DrawValidation);
            root.Add(new HelpBox("Value routes evaluate a Source once, then pass the retained value through Modifiers to each Output. Trigger routes carry events only.", HelpBoxMessageType.Info));
            root.Add(validationPanel);
            root.Add(runtimePanel);
            root.Add(routeList);
            root.Add(CreateTypePickerButton("Add Route", "Add route", RouteTypes(), type => AddRoute(type, Rebuild)));

            void Rebuild()
            {
                serializedObject.UpdateIfRequiredOrScript();
                routeList.Unbind();
                routeList.Clear();
                var routes = serializedObject.FindProperty("routes");
                for (var index = 0; index < routes.arraySize; index++)
                {
                    var routeProperty = routes.GetArrayElementAtIndex(index);
                    routeList.Add(BuildRouteCard(routeProperty, index, Rebuild));
                }
                routeList.Bind(serializedObject);
                validationPanel.MarkDirtyRepaint();
                runtimePanel.MarkDirtyRepaint();
            }

            rebuildInspector = Rebuild;
            root.Bind(serializedObject);
            Rebuild();
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
                var recovery = CreateTypePickerButton("Replace with route type…", "Replace route", RouteTypes(), type => { route.managedReferenceValue = Activator.CreateInstance(type); ApplyAndRebuild(rebuild); });
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
                row.Add(CreateTypePickerButton("Add " + title, "Add " + title, types, type => { nodeProperty.managedReferenceValue = Activator.CreateInstance(type); ApplyAndRebuild(rebuild); }));
            else
            {
                row.Add(new Label(ShortTypeName(nodeProperty.managedReferenceValue.GetType())) { style = { flexGrow = 1 } });
                row.Add(new Button(() => CopyNode(nodeProperty)) { text = "Copy" });
                row.Add(new Button(() => PasteNode(nodeProperty, types, rebuild)) { text = "Paste" });
                row.Add(CreateTypePickerButton("Change", "Change " + title, types, type => { nodeProperty.managedReferenceValue = Activator.CreateInstance(type); ApplyAndRebuild(rebuild); }));
                row.Add(new Button(() => { nodeProperty.managedReferenceValue = null; ApplyAndRebuild(rebuild); }) { text = "Remove" });
            }
            section.Add(row);
            if (nodeProperty?.managedReferenceValue != null)
            {
                nodeProperty.isExpanded = true;
                section.Add(BuildNodeContents(nodeProperty, rebuild));
            }
            else if (!string.IsNullOrEmpty(nodeProperty?.managedReferenceFullTypename))
                section.Add(new HelpBox("Missing node type preserved: " + nodeProperty.managedReferenceFullTypename, HelpBoxMessageType.Warning));
            return section;
        }

        private VisualElement BuildNodeContents(SerializedProperty nodeProperty, Action rebuild)
        {
            var node = nodeProperty?.managedReferenceValue;
            if (node == null) return new HelpBox("No node is assigned.", HelpBoxMessageType.Warning);
            nodeProperty.isExpanded = true;
            if (HasCustomPropertyDrawer(node.GetType())) return new PropertyField(nodeProperty);
            return BuildManagedObjectContents(nodeProperty, rebuild, new HashSet<long>());
        }

        private VisualElement BuildManagedObjectContents(SerializedProperty objectProperty, Action rebuild, HashSet<long> visitedReferences)
        {
            var contents = new VisualElement();
            var value = objectProperty.managedReferenceValue;
            if (value == null) return new HelpBox("The serialized reference is empty.", HelpBoxMessageType.Warning);
            if (!visitedReferences.Add(objectProperty.managedReferenceId))
                return new HelpBox("This managed reference is already shown above. Cycles and shared node instances cannot be expanded recursively.", HelpBoxMessageType.Warning);

            if (value is MapValueSourceNode mapSource)
            {
                var description = MapValueSourceDescription(mapSource);
                if (!string.IsNullOrEmpty(description)) contents.Add(new HelpBox(description, HelpBoxMessageType.Info));
            }

            var visibleChildCount = 0;
            foreach (var child in DirectChildren(objectProperty))
            {
                if (child.name == "nodeId") continue;
                visibleChildCount++;
                if (child.propertyType == SerializedPropertyType.ManagedReference)
                    contents.Add(BuildManagedReferenceField(child, child.displayName, rebuild, visitedReferences));
                else if (IsManagedReferenceList(child))
                    contents.Add(BuildManagedReferenceList(child, rebuild, visitedReferences));
                else
                    contents.Add(new PropertyField(child));
            }
            if (visibleChildCount == 0 && !(value is MapValueSourceNode))
                contents.Add(new HelpBox("This node has no editable serialized properties.", HelpBoxMessageType.Info));
            return contents;
        }

        private VisualElement BuildManagedReferenceField(SerializedProperty referenceProperty, string label, Action rebuild, HashSet<long> visitedReferences, string listPath = null, int listIndex = -1)
        {
            var container = new VisualElement();
            var contract = ResolveSerializedPropertyType(referenceProperty);
            var candidateTypes = contract == null ? Array.Empty<Type>() : NodeTypes(contract);
            var path = referenceProperty.propertyPath;
            var value = referenceProperty.managedReferenceValue;
            var hasMissingType = value == null && !string.IsNullOrEmpty(referenceProperty.managedReferenceFullTypename);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            var labelText = label;
            if (contract != null) labelText += " (" + ReferenceContractLabel(contract) + ")";
            row.Add(new Label(labelText) { style = { minWidth = 130 } });
            if (value != null) row.Add(new Label(ShortTypeName(value.GetType())) { style = { flexGrow = 1 } });
            else if (hasMissingType) row.Add(new Label("Missing type") { style = { flexGrow = 1 } });
            else row.Add(new VisualElement { style = { flexGrow = 1 } });

            Action<Type> assign = type => AssignManagedReference(path, contract, type, rebuild);
            var picker = CreateTypePickerButton(value == null ? (hasMissingType ? "Replace" : "Select") : "Change", "Select " + ReferenceContractLabel(contract), candidateTypes, assign);
            picker.SetEnabled(contract != null && candidateTypes.Count > 0);
            row.Add(picker);

            if (value != null) row.Add(new Button(() => CopyNode(referenceProperty)) { text = "Copy" });
            var paste = new Button(() => PasteManagedReferenceInto(path, contract, rebuild)) { text = "Paste" };
            paste.SetEnabled(contract != null);
            row.Add(paste);

            if (!string.IsNullOrEmpty(listPath) && listIndex >= 0)
            {
                var currentIndex = listIndex;
                row.Add(new Button(() => MoveNode(listPath, currentIndex, -1, rebuild)) { text = "↑" });
                row.Add(new Button(() => MoveNode(listPath, currentIndex, 1, rebuild)) { text = "↓" });
                row.Add(new Button(() => DuplicateNode(listPath, currentIndex, rebuild)) { text = "Duplicate" });
            }
            var remove = string.IsNullOrEmpty(listPath)
                ? (Action)(() => RemoveManagedReference(path, rebuild))
                : () => DeleteNode(listPath, listIndex, rebuild);
            row.Add(new Button(remove) { text = string.IsNullOrEmpty(listPath) ? "Remove" : "Delete" });
            container.Add(row);

            if (hasMissingType)
                container.Add(new HelpBox("Missing serialized type preserved: " + referenceProperty.managedReferenceFullTypename + ". Replace or remove it only if intended.", HelpBoxMessageType.Warning));
            else if (contract != null && candidateTypes.Count == 0)
                container.Add(new HelpBox("No compatible serializable node types are available for " + ReferenceContractLabel(contract) + ".", HelpBoxMessageType.Warning));

            if (value != null)
            {
                referenceProperty.isExpanded = true;
                var nested = HasCustomPropertyDrawer(value.GetType())
                    ? (VisualElement)new PropertyField(referenceProperty)
                    : BuildManagedObjectContents(referenceProperty, rebuild, visitedReferences);
                nested.style.marginLeft = 14;
                nested.style.marginTop = 2;
                container.Add(nested);
            }
            return container;
        }

        private VisualElement BuildManagedReferenceList(SerializedProperty listProperty, Action rebuild, HashSet<long> visitedReferences)
        {
            var container = new Box();
            var elementType = GetCollectionElementType(ResolveSerializedPropertyType(listProperty));
            if (elementType == null)
            {
                container.Add(new HelpBox("Could not determine the serialized reference list element type.", HelpBoxMessageType.Error));
                return container;
            }

            var label = Singularize(listProperty.displayName);
            container.Add(new Label(listProperty.displayName + " (" + listProperty.arraySize + ")") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            var candidates = NodeTypes(elementType);
            for (var index = 0; index < listProperty.arraySize; index++)
            {
                var element = listProperty.GetArrayElementAtIndex(index);
                container.Add(BuildManagedReferenceField(element, label + " " + (index + 1), rebuild, visitedReferences, listProperty.propertyPath, index));
            }

            var listPath = listProperty.propertyPath;
            var addButton = CreateTypePickerButton("Add " + label, "Add " + label, candidates, type => AddNode(listPath, type, rebuild));
            addButton.SetEnabled(candidates.Count > 0);
            container.Add(addButton);
            if (candidates.Count == 0)
                container.Add(new HelpBox("No compatible serializable node types are available for " + ReferenceContractLabel(elementType) + ".", HelpBoxMessageType.Warning));
            return container;
        }

        private void AssignManagedReference(string path, Type contract, Type type, Action rebuild)
        {
            if (contract == null || type == null || !contract.IsAssignableFrom(type)) return;
            serializedObject.Update();
            var property = serializedObject.FindProperty(path);
            if (property == null) return;
            property.managedReferenceValue = Activator.CreateInstance(type);
            ApplyAndRebuild(rebuild);
        }

        private void PasteManagedReferenceInto(string path, Type contract, Action rebuild)
        {
            if (!CanPaste(contract)) return;
            serializedObject.Update();
            var property = serializedObject.FindProperty(path);
            if (property == null) return;
            var clone = PasteManagedReference();
            if (clone == null || !contract.IsInstanceOfType(clone)) return;
            RegenerateIdentity(clone, false);
            property.managedReferenceValue = clone;
            ApplyAndRebuild(rebuild);
        }

        private void RemoveManagedReference(string path, Action rebuild)
        {
            serializedObject.Update();
            var property = serializedObject.FindProperty(path);
            if (property == null) return;
            property.managedReferenceValue = null;
            ApplyAndRebuild(rebuild);
        }

        private bool IsManagedReferenceList(SerializedProperty property)
        {
            if (property == null || !property.isArray || property.propertyType == SerializedPropertyType.String) return false;
            var elementType = GetCollectionElementType(ResolveSerializedPropertyType(property));
            return elementType != null && (elementType.IsInterface || elementType.IsAbstract)
                && ResolveSerializedPropertyFieldInfo(property)?.GetCustomAttribute<SerializeReference>() != null;
        }

        private static Type ResolveSerializedPropertyType(SerializedProperty property)
        {
            var target = property?.serializedObject?.targetObject;
            if (target == null || string.IsNullOrEmpty(property.propertyPath)) return null;
            var currentObject = (object)target;
            var currentType = target.GetType();
            var pathParts = property.propertyPath.Split('.');
            for (var index = 0; index < pathParts.Length; index++)
            {
                var part = pathParts[index];
                if (part == "Array")
                {
                    var elementType = GetCollectionElementType(currentType);
                    if (elementType == null || index + 1 >= pathParts.Length) return null;
                    var dataPart = pathParts[++index];
                    var start = dataPart.IndexOf('[', StringComparison.Ordinal);
                    var end = dataPart.IndexOf(']', StringComparison.Ordinal);
                    if (start < 0 || end <= start || !int.TryParse(dataPart.Substring(start + 1, end - start - 1), out var elementIndex)) return null;
                    currentObject = GetCollectionItem(currentObject, elementIndex);
                    if (index == pathParts.Length - 1) return elementType;
                    currentType = currentObject?.GetType() ?? elementType;
                    continue;
                }

                var field = FindField(currentType, part);
                if (field == null) return null;
                if (index == pathParts.Length - 1) return field.FieldType;
                currentObject = currentObject == null ? null : field.GetValue(currentObject);
                currentType = currentObject?.GetType() ?? field.FieldType;
            }
            return currentType;
        }

        private static FieldInfo ResolveSerializedPropertyFieldInfo(SerializedProperty property)
        {
            var target = property?.serializedObject?.targetObject;
            if (target == null || string.IsNullOrEmpty(property.propertyPath)) return null;
            var currentObject = (object)target;
            var currentType = target.GetType();
            FieldInfo lastField = null;
            var pathParts = property.propertyPath.Split('.');
            for (var index = 0; index < pathParts.Length; index++)
            {
                var part = pathParts[index];
                if (part == "Array")
                {
                    var elementType = GetCollectionElementType(currentType);
                    if (elementType == null || index + 1 >= pathParts.Length) return null;
                    var dataPart = pathParts[++index];
                    var start = dataPart.IndexOf('[', StringComparison.Ordinal);
                    var end = dataPart.IndexOf(']', StringComparison.Ordinal);
                    if (start < 0 || end <= start || !int.TryParse(dataPart.Substring(start + 1, end - start - 1), out var elementIndex)) return null;
                    currentObject = GetCollectionItem(currentObject, elementIndex);
                    currentType = currentObject?.GetType() ?? elementType;
                    continue;
                }

                lastField = FindField(currentType, part);
                if (lastField == null) return null;
                if (index == pathParts.Length - 1) return lastField;
                currentObject = currentObject == null ? null : lastField.GetValue(currentObject);
                currentType = currentObject?.GetType() ?? lastField.FieldType;
            }
            return lastField;
        }

        private static object GetCollectionItem(object collection, int index)
        {
            if (collection is IList list && index >= 0 && index < list.Count) return list[index];
            if (collection is Array array && index >= 0 && index < array.Length) return array.GetValue(index);
            return null;
        }

        private static Type GetCollectionElementType(Type collectionType)
        {
            if (collectionType == null) return null;
            if (collectionType.IsArray) return collectionType.GetElementType();
            var enumerable = collectionType.GetInterfaces().Concat(new[] { collectionType })
                .FirstOrDefault(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            return enumerable?.GetGenericArguments()[0];
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

        private static bool HasCustomPropertyDrawer(Type propertyType)
        {
            if (propertyType == null) return false;
            if (customPropertyDrawerCache.TryGetValue(propertyType, out var cached)) return cached;
            foreach (var drawerType in TypeCache.GetTypesDerivedFrom<PropertyDrawer>())
            foreach (var attribute in drawerType.GetCustomAttributes<CustomPropertyDrawer>(false))
            {
                var fields = attribute.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var typeField = fields.FirstOrDefault(field => field.FieldType == typeof(Type));
                var childrenField = fields.FirstOrDefault(field => field.FieldType == typeof(bool));
                var drawerTarget = typeField?.GetValue(attribute) as Type;
                if (drawerTarget == propertyType || (childrenField?.GetValue(attribute) is bool useForChildren && useForChildren && drawerTarget != null && drawerTarget.IsAssignableFrom(propertyType)))
                {
                    customPropertyDrawerCache[propertyType] = true;
                    return true;
                }
            }
            customPropertyDrawerCache[propertyType] = false;
            return false;
        }

        private static string ReferenceContractLabel(Type contract)
        {
            if (contract == null) return "Source";
            if (contract == typeof(ITriggerSource)) return "Trigger Source";
            if (contract == typeof(ITriggerModifier)) return "Trigger Modifier";
            if (contract == typeof(ITriggerOutput)) return "Trigger Output";
            if (contract.IsGenericType)
            {
                var generic = contract.GetGenericTypeDefinition();
                var valueType = ValueTypeLabel(contract.GetGenericArguments()[0]);
                if (generic == typeof(IValueSource<>)) return valueType + " Source";
                if (generic == typeof(IValueModifier<>)) return valueType + " Modifier";
                if (generic == typeof(IValueOutput<>)) return valueType + " Output";
            }
            return ValueTypeLabel(contract);
        }

        private static string Singularize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "Item";
            if (value.EndsWith("ies", StringComparison.OrdinalIgnoreCase)) return value.Substring(0, value.Length - 3) + "y";
            if (value.EndsWith("s", StringComparison.OrdinalIgnoreCase)) return value.Substring(0, value.Length - 1);
            return value;
        }

        private static string MapValueSourceDescription(MapValueSourceNode source)
        {
            if (source is FloatToIndexSource) return "Returns the number of thresholds less than or equal to the input float. Thresholds must be finite and strictly increasing.";
            if (source is FloatToVector2Source) return "Copies each input float f to both components: Vector2(f, f).";
            if (source is FloatToVector3Source) return "Copies each input float f to all components: Vector3(f, f, f).";
            if (source is FloatToVector4Source) return "Copies each input float f to all components: Vector4(f, f, f, f).";
            if (source is FloatToVector2ComponentSource) return "Replaces the selected component of Fixed Components with the input float.";
            if (source is FloatToVector3ComponentSource) return "Replaces the selected component of Fixed Components with the input float.";
            if (source is FloatToVector4ComponentSource) return "Replaces the selected component of Fixed Components with the input float.";
            return "Reads a nested " + ValueTypeLabel(source.InputValueType) + " Source and converts it to " + ValueTypeLabel(source.OutputValueType) + ".";
        }

        private static string ValueTypeLabel(Type type)
        {
            if (type == typeof(float)) return "float";
            if (type == typeof(int)) return "int";
            if (type == typeof(bool)) return "bool";
            return type.Name;
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
                row.Add(new Button(() => PasteNodeAt(list.propertyPath, elementIndex, types, rebuild)) { text = "Paste" });
                row.Add(new Button(() => DuplicateNode(list.propertyPath, elementIndex, rebuild)) { text = "Duplicate" });
                row.Add(new Button(() => DeleteNode(list.propertyPath, elementIndex, rebuild)) { text = "Delete" });
                section.Add(row);
                if (type == null && !string.IsNullOrEmpty(element.managedReferenceFullTypename))
                    section.Add(new HelpBox("Missing type preserved: " + element.managedReferenceFullTypename, HelpBoxMessageType.Warning));
                else if (type != null)
                {
                    element.isExpanded = true;
                    section.Add(BuildNodeContents(element, rebuild));
                }
            }
            section.Add(CreateTypePickerButton("Add " + title.TrimEnd('s'), "Add " + title, types, type => AddNode(list.propertyPath, type, rebuild)));
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
            DeleteManagedReferenceListElement(list, index);
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

        private void PasteNode(SerializedProperty property, IReadOnlyList<Type> types, Action rebuild)
        {
            if (!CanPaste(types)) return;
            serializedObject.Update();
            var clone = PasteManagedReference();
            if (clone == null || !types.Any(type => type.IsInstanceOfType(clone))) return;
            RegenerateIdentity(clone, false);
            property.managedReferenceValue = clone;
            ApplyAndRebuild(rebuild);
        }

        private void PasteNodeAt(string path, int index, IReadOnlyList<Type> types, Action rebuild)
        {
            if (!CanPaste(types)) return;
            serializedObject.Update();
            var property = serializedObject.FindProperty(path).GetArrayElementAtIndex(index);
            var clone = PasteManagedReference();
            if (clone == null || !types.Any(type => type.IsInstanceOfType(clone))) return;
            RegenerateIdentity(clone, false);
            property.managedReferenceValue = clone;
            ApplyAndRebuild(rebuild);
        }

        private void DeleteNode(string path, int index, Action rebuild)
        {
            serializedObject.Update();
            DeleteManagedReferenceListElement(serializedObject.FindProperty(path), index);
            ApplyAndRebuild(rebuild);
        }

        private static void DeleteManagedReferenceListElement(SerializedProperty list, int index)
        {
            if (list == null || !list.isArray || index < 0 || index >= list.arraySize) return;

            // DeleteArrayElementAtIndex can report an out-of-bounds access for
            // SerializeReference elements after it has already removed them.
            // Move the selected item to the tail, then resize the list once.
            var lastIndex = list.arraySize - 1;
            if (index != lastIndex) list.MoveArrayElement(index, lastIndex);
            list.arraySize = lastIndex;
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
            if (contract == null) return false;
            var type = Type.GetType(clipboardType);
            return type != null && contract.IsAssignableFrom(type);
        }

        private static bool CanPaste(IEnumerable<Type> types)
        {
            var type = Type.GetType(clipboardType);
            return type != null && types != null && types.Any(candidate => candidate == type);
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
            for (var type = value.GetType(); type != null && type != typeof(object); type = type.BaseType)
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
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

        private static Button CreateTypePickerButton(string buttonText, string title, IEnumerable<Type> types, Action<Type> selected)
        {
            var sortedTypes = types.OrderBy(value => value.Name, StringComparer.Ordinal).ToArray();
            var button = new Button { text = buttonText };
            button.clicked += () => RouteNodeTypePickerPopup.Open(button.worldBound, title, sortedTypes, selected);
            return button;
        }
    }

    internal sealed class RouteNodeTypePickerPopup : PopupWindowContent
    {
        private readonly string pickerTitle;
        private readonly Type[] types;
        private readonly Action<Type> selected;
        private string search = string.Empty;
        private Vector2 scroll;

        private RouteNodeTypePickerPopup(string title, Type[] types, Action<Type> selected)
        {
            pickerTitle = title;
            this.types = types ?? Array.Empty<Type>();
            this.selected = selected;
        }

        public static void Open(Rect activatorRect, string title, Type[] types, Action<Type> selected)
            => UnityEditor.PopupWindow.Show(activatorRect, new RouteNodeTypePickerPopup(title, types, selected));

        public override Vector2 GetWindowSize() => new Vector2(380f, 460f);

        public override void OnGUI(Rect rect)
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
                editorWindow.Close();
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
        private string detailedRouteId;
        private bool showMidiHistory = true;
        private bool showTransportHistory = true;
        private bool showPerformance = true;
        private readonly List<RuntimeRouteTrace> routeHistory = new List<RuntimeRouteTrace>(128);
        private readonly List<RuntimeMidiTrace> midiHistory = new List<RuntimeMidiTrace>(128);
        private readonly List<RuntimeTransportTrace> transportHistory = new List<RuntimeTransportTrace>(128);
        private double lastRateSampleTime;
        private long lastRawMidi, lastLogicalMidi, lastLedOut, lastOscIn, lastOscOut;
        private double rawMidiPerSecond, logicalMidiPerSecond, ledOutPerSecond, oscInPerSecond, oscOutPerSecond;

        public static void Open(MondmadeVJ0Runtime runtime)
        {
            var window = GetWindow<RuntimeMonitorWindow>("Mondmade VJ0 Monitor");
            window.SetRuntime(runtime);
            window.SelectInitialRoute();
            window.Show();
        }

        private void OnGUI()
        {
            var selectedRuntime = (MondmadeVJ0Runtime)EditorGUILayout.ObjectField("Runtime", runtime, typeof(MondmadeVJ0Runtime), true);
            if (selectedRuntime != runtime) { SetRuntime(selectedRuntime); SelectInitialRoute(); }
            if (runtime == null || runtime.Services == null)
            {
                EditorGUILayout.HelpBox("Enter Play mode and assign a MondmadeVJ0Runtime to inspect live state.", MessageType.Info);
                return;
            }
            if (GUILayout.Button("Retry faulted routes")) runtime.RetryFaultedRoutes();
            var services = runtime.Services;
            using (new EditorGUILayout.HorizontalScope())
            {
                var capture = EditorGUILayout.ToggleLeft("Capture Monitor history / timing", services.MonitorCaptureEnabled);
                if (capture != services.MonitorCaptureEnabled) services.MonitorCaptureEnabled = capture;
                GUILayout.Label($"History: values {services.RouteTraceCount:N0} · MIDI {services.RawMidiInputCount + services.LogicalMidiInputCount + services.MidiLedOutputCount:N0} · transport {services.TransportTapCount + services.TransportClockPulseCount:N0}", EditorStyles.miniLabel);
            }
            EditorGUILayout.LabelField($"Selected detail Routes: {services.SelectedRouteCount}/{RuntimeServices.MonitorRouteSelectionLimit} · dropped history: {services.DroppedRouteHistoryCount + services.DroppedMidiHistoryCount + services.DroppedTransportHistoryCount:N0}", EditorStyles.miniLabel);
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
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var isSelected = route.Id == detailedRouteId;
                        var selected = GUILayout.Toggle(isSelected, "Trace", EditorStyles.miniButton, GUILayout.Width(48));
                        if (selected != isSelected) SelectRoute(route.Id, selected);
                        EditorGUILayout.LabelField(route.Name, route.Status + " · " + CurrentValue(route));
                    }
                    if (route is TriggerRoute trigger) DrawOutputs(route, trigger.Outputs.Count, trigger.GetOutputStatus);
                    else if (route is ValueRoute<float> f) DrawOutputs(route, f.Outputs.Count, f.GetOutputStatus);
                    else if (route is ValueRoute<int> i) DrawOutputs(route, i.Outputs.Count, i.GetOutputStatus);
                    else if (route is ValueRoute<bool> b) DrawOutputs(route, b.Outputs.Count, b.GetOutputStatus);
                    else if (route is ValueRoute<Vector2> v2) DrawOutputs(route, v2.Outputs.Count, v2.GetOutputStatus);
                    else if (route is ValueRoute<Vector3> v3) DrawOutputs(route, v3.Outputs.Count, v3.GetOutputStatus);
                    else if (route is ValueRoute<Vector4> v4) DrawOutputs(route, v4.Outputs.Count, v4.GetOutputStatus);
                    else if (route is ValueRoute<Color> color) DrawOutputs(route, color.Outputs.Count, color.GetOutputStatus);
                    if (route.Id == detailedRouteId) DrawRouteHistory(route.Id, services);
                }
            }

            EditorGUILayout.Space();
            DrawMidiHistory(services);
            DrawTransportHistory(services);
            DrawPerformance(services);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Diagnostics", EditorStyles.boldLabel);
            foreach (var diagnostic in runtime.Services.GetDiagnostics().Reverse())
                EditorGUILayout.HelpBox($"{diagnostic.TimeUtc:HH:mm:ss} [{diagnostic.Code}] {diagnostic.Message}", diagnostic.Exception == null ? MessageType.Warning : MessageType.Error);
            EditorGUILayout.EndScrollView();
        }

        private void OnEnable() => EditorApplication.update += RefreshWhilePlaying;
        private void OnDisable()
        {
            EditorApplication.update -= RefreshWhilePlaying;
            if (runtime != null && runtime.Services != null && !string.IsNullOrEmpty(detailedRouteId))
                runtime.Services.SetRouteTraceSelected(detailedRouteId, false);
            detailedRouteId = null;
        }

        private void RefreshWhilePlaying()
        {
            if (!Application.isPlaying || runtime == null || runtime.Services == null) return;
            UpdateRates(runtime.Services);
            Repaint();
        }

        private void SelectInitialRoute()
        {
            if (runtime == null || runtime.Services == null || !string.IsNullOrEmpty(detailedRouteId)) return;
            foreach (var host in runtime.RouteHosts)
            {
                if (host == null) continue;
                foreach (var route in host.Routes)
                    if (route != null && route.Status == "Active") { SelectRoute(route.Id, true); return; }
            }
        }

        private void SetRuntime(MondmadeVJ0Runtime value)
        {
            if (runtime != null && runtime.Services != null && !string.IsNullOrEmpty(detailedRouteId))
                runtime.Services.SetRouteTraceSelected(detailedRouteId, false);
            runtime = value;
            detailedRouteId = null;
        }

        private void SelectRoute(string routeId, bool selected)
        {
            if (runtime == null || runtime.Services == null) return;
            if (!selected)
            {
                runtime.Services.SetRouteTraceSelected(routeId, false);
                if (detailedRouteId == routeId) detailedRouteId = null;
                return;
            }
            if (!string.IsNullOrEmpty(detailedRouteId)) runtime.Services.SetRouteTraceSelected(detailedRouteId, false);
            if (runtime.Services.SetRouteTraceSelected(routeId, true)) detailedRouteId = routeId;
            else ShowNotification(new GUIContent("At most 32 Routes can be traced at once."));
        }

        private void DrawRouteHistory(string routeId, RuntimeServices services)
        {
            services.CopyRouteHistory(routeHistory, routeId, 128);
            EditorGUILayout.LabelField("Raw → Modifiers → Final → Outputs", EditorStyles.miniBoldLabel);
            if (routeHistory.Count == 0)
            {
                EditorGUILayout.LabelField("Waiting for the next value…", EditorStyles.miniLabel);
                return;
            }
            for (var i = Math.Max(0, routeHistory.Count - 32); i < routeHistory.Count; i++)
            {
                var item = routeHistory[i];
                var stage = item.Stage == RuntimeTraceStage.Modifier || item.Stage == RuntimeTraceStage.TriggerModifier
                    ? $"{item.Stage}[{item.Index + 1}] {item.NodeName}" : item.Stage.ToString();
                var outcome = string.IsNullOrEmpty(item.Outcome) ? string.Empty : " · " + item.Outcome;
                var processing = item.ProcessingMilliseconds <= 0d ? string.Empty : $" · {item.ProcessingMilliseconds:F4} ms";
                EditorGUILayout.LabelField($"{stage}: {item.Value?.ToString() ?? "null"}{outcome}{processing}", EditorStyles.miniLabel);
            }
        }

        private void DrawMidiHistory(RuntimeServices services)
        {
            showMidiHistory = EditorGUILayout.Foldout(showMidiHistory, "MIDI Monitor", true, EditorStyles.foldoutHeader);
            if (!showMidiHistory) return;
            EditorGUILayout.LabelField($"IN raw {rawMidiPerSecond:F1}/s · logical {logicalMidiPerSecond:F1}/s · OUT LED queued {ledOutPerSecond:F1}/s", EditorStyles.miniLabel);
            services.CopyMidiHistory(midiHistory, 96);
            for (var i = Math.Max(0, midiHistory.Count - 24); i < midiHistory.Count; i++)
            {
                var item = midiHistory[i];
                if (item.Kind == RuntimeMidiEventKind.RawInput)
                    EditorGUILayout.LabelField($"IN Raw · {item.SurfaceName ?? item.SurfaceId} · ch {item.Channel + 1} · {RawAddressName(item.AddressType)} {item.Number} · {item.Value:F3}", EditorStyles.miniLabel);
                else if (item.Kind == RuntimeMidiEventKind.LogicalInput)
                    EditorGUILayout.LabelField($"IN {item.SurfaceName ?? item.SurfaceId}/{item.Label ?? item.ControlId} · {item.Value:F3} · {LogicalButtonState(item)}", EditorStyles.miniLabel);
                else
                    EditorGUILayout.LabelField($"OUT queued · {item.SurfaceName ?? item.SurfaceId}/{item.ControlId} · ch {item.Channel + 1} note {item.Number} · RGB({item.Red},{item.Green},{item.Blue}) · {item.Brightness:P0} · {LedEffectName(item.Effect)}", EditorStyles.miniLabel);
            }
        }

        private void DrawTransportHistory(RuntimeServices services)
        {
            showTransportHistory = EditorGUILayout.Foldout(showTransportHistory, "Transport Monitor", true, EditorStyles.foldoutHeader);
            if (!showTransportHistory) return;
            var transport = services.Transport;
            if (transport != null)
                EditorGUILayout.LabelField($"{ReadText(transport, "ClockSource")} · {ReadText(transport, "SyncState")} · {transport.EffectiveBpm:F2} BPM · Beat {transport.BeatPosition:F3} · Phase {transport.BeatPhase:F3} · Bar {transport.BarIndex}", EditorStyles.miniLabel);
            services.CopyTransportHistory(transportHistory, 96);
            for (var i = Math.Max(0, transportHistory.Count - 24); i < transportHistory.Count; i++)
            {
                var item = transportHistory[i];
                EditorGUILayout.LabelField($"{item.Kind} · {item.Bpm:F2} BPM · Beat {item.BeatPosition:F3}" + (string.IsNullOrEmpty(item.Detail) ? string.Empty : " · " + item.Detail), EditorStyles.miniLabel);
            }
        }

        private void DrawPerformance(RuntimeServices services)
        {
            showPerformance = EditorGUILayout.Foldout(showPerformance, "Performance Monitor", true, EditorStyles.foldoutHeader);
            if (!showPerformance) return;
            var activeRoutes = 0;
            var sources = 0;
            var modifiers = 0;
            foreach (var host in runtime.RouteHosts)
            {
                if (host == null) continue;
                foreach (var route in host.Routes)
                {
                    if (route == null || route.Status != "Active") continue;
                    activeRoutes++;
                    sources++;
                    var property = route.GetType().GetProperty("Modifiers");
                    if (property?.GetValue(route) is System.Collections.ICollection collection) modifiers += collection.Count;
                }
            }
            EditorGUILayout.LabelField($"Active Routes {activeRoutes} · Sources {sources} · Modifiers {modifiers} · Frame-updated callbacks {runtime.FrameSubscriberCount}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Route processing {services.RouteProcessingMeanMilliseconds:F4} ms mean · {services.RouteProcessingP99Milliseconds:F4} ms p99 · {services.RouteProcessingMaximumMilliseconds:F4} ms max · {services.RouteProcessingCount:N0} samples", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Frame callback processing {services.FrameCallbackMeanMilliseconds:F4} ms mean · {services.FrameCallbackP99Milliseconds:F4} ms p99 · {services.FrameCallbackMaximumMilliseconds:F4} ms max · {services.LastFrameCallbackCount} callbacks", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"MIDI IN raw/logical {rawMidiPerSecond:F1}/{logicalMidiPerSecond:F1} per second · LED OUT {ledOutPerSecond:F1}/s · OSC IN/OUT {oscInPerSecond:F1}/{oscOutPerSecond:F1}/s", EditorStyles.miniLabel);
        }

        private void UpdateRates(RuntimeServices services)
        {
            var now = EditorApplication.timeSinceStartup;
            var raw = services.RawMidiInputCount;
            var logical = services.LogicalMidiInputCount;
            var led = services.MidiLedOutputCount;
            long oscIn = 0, oscOut = 0;
            foreach (var module in runtime.ConfiguredModules)
            {
                if (module == null) continue;
                oscIn += ReadLong(module, "ReceivedCount");
                oscOut += ReadLong(module, "SentCount");
            }
            if (lastRateSampleTime > 0d)
            {
                var elapsed = now - lastRateSampleTime;
                if (elapsed >= 0.5d)
                {
                    rawMidiPerSecond = (raw - lastRawMidi) / elapsed;
                    logicalMidiPerSecond = (logical - lastLogicalMidi) / elapsed;
                    ledOutPerSecond = (led - lastLedOut) / elapsed;
                    oscInPerSecond = (oscIn - lastOscIn) / elapsed;
                    oscOutPerSecond = (oscOut - lastOscOut) / elapsed;
                }
            }
            lastRateSampleTime = now;
            lastRawMidi = raw; lastLogicalMidi = logical; lastLedOut = led; lastOscIn = oscIn; lastOscOut = oscOut;
        }

        private static long ReadLong(object target, string propertyName)
        {
            try { return Convert.ToInt64(target.GetType().GetProperty(propertyName)?.GetValue(target) ?? 0L); }
            catch { return 0L; }
        }

        private static string ReadText(object target, string propertyName)
        {
            try { return target.GetType().GetProperty(propertyName)?.GetValue(target)?.ToString() ?? "—"; }
            catch { return "—"; }
        }

        private static string LogicalButtonState(RuntimeMidiTrace item)
            => item.PressedDown ? "Pressed" : item.Released ? "Released" : item.Pressed ? "Held" : "Value";

        private static string LedEffectName(int effect)
            => effect == 0 ? "Solid" : effect == 1 ? "Blink" : effect == 2 ? "Pulse" : "Effect " + effect;

        private static string RawAddressName(int addressType)
            => addressType == 0 ? "Note" : addressType == 1 ? "CC" : addressType == 2 ? "Poly Aftertouch"
                : addressType == 3 ? "Channel Pressure" : addressType == 4 ? "Pitch Bend" : "Address " + addressType;

        private static string ReadModuleSummary(object module)
        {
            var names = new[] { "Status", "SyncState", "EffectiveBpm", "BeatPosition", "ReceivedCount", "SentCount", "SentPulses", "DroppedCount", "DroppedNormalMessages", "DroppedClockMessages", "DroppedOutputMessages", "MissedPulses", "MalformedCount", "OutputBackend", "OutputScheduleLeadMilliseconds", "OutputTimingMetricBasis", "OutputBackendDiagnostic", "P99LatenessMilliseconds", "MaximumLatenessMilliseconds", "MaximumDeadlineDriftMilliseconds" };
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
            var sceneHosts = GetSceneHosts(host).ToArray();
            var routeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var route in host.Routes)
            {
                if (route == null) { errors.Add($"{host.name}: null route or missing serialized type."); continue; }
                if (string.IsNullOrWhiteSpace(route.Name)) errors.Add($"{host.name}: route {route.Id} has no name.");
                if (string.IsNullOrWhiteSpace(route.Id)) errors.Add($"{host.name}/{route.Name}: route has no stable id.");
                else if (!routeIds.Add(route.Id)) errors.Add($"{host.name}/{route.Name}: duplicate route id {route.Id}.");
                if (!(GetRouteOutputs(route) is System.Collections.ICollection outputs) || outputs.Count == 0)
                    errors.Add($"{host.name}/{route.Name}: no Output is assigned.");
                var source = GetRouteSource(route);
                if (source == null) errors.Add($"{host.name}/{route.Name}: Source is not assigned.");
                var nodeIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var node in EnumerateNodes(route))
                {
                    if (node == null) { errors.Add($"{host.name}/{route.Name}: missing node or type."); continue; }
                    if (string.IsNullOrWhiteSpace(node.NodeId)) errors.Add($"{host.name}/{route.Name}/{node.GetType().Name}: node has no stable id.");
                    else if (!nodeIds.Add(node.NodeId)) errors.Add($"{host.name}/{route.Name}: duplicate node id {node.NodeId}.");
                    ValidateNode(host, route, node, errors);
                }
            }
            ValidateSceneRouteIds(sceneHosts, errors);
            ValidateOutputConflicts(sceneHosts, errors);
            ValidateSignalCycles(sceneHosts, errors);
            return errors;
        }

        private static void ValidateSceneRouteIds(IEnumerable<RouteHost> hosts, List<string> errors)
        {
            var routeIds = new Dictionary<string, RouteHost>(StringComparer.Ordinal);
            foreach (var candidateHost in hosts)
            foreach (var route in candidateHost.Routes)
            {
                if (route == null || string.IsNullOrWhiteSpace(route.Id)) continue;
                if (routeIds.TryGetValue(route.Id, out var existingHost))
                {
                    if (existingHost != candidateHost)
                        errors.Add($"{candidateHost.name}/{route.Name}: duplicate route id {route.Id} also exists in {existingHost.name}.");
                }
                else routeIds.Add(route.Id, candidateHost);
            }
        }

        private static IEnumerable<RouteHost> GetSceneHosts(RouteHost host)
        {
            var scene = host.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded) return new[] { host };
            var hosts = new List<RouteHost>();
            foreach (var root in scene.GetRootGameObjects()) hosts.AddRange(root.GetComponentsInChildren<RouteHost>(true));
            return hosts;
        }

        private static void ValidateOutputConflicts(IEnumerable<RouteHost> hosts, List<string> errors)
        {
            var writers = new Dictionary<string, string>(StringComparer.Ordinal);
            var reported = new HashSet<string>(StringComparer.Ordinal);
            foreach (var host in hosts)
            foreach (var route in host.Routes)
            {
                if (route == null || !(GetRouteOutputs(route) is System.Collections.IEnumerable outputs)) continue;
                var outputIndex = 0;
                foreach (var candidate in outputs)
                {
                    var currentIndex = outputIndex++;
                    if (!(candidate is RouteNode output)) continue;
                    var current = $"{host.name}/{route.Name}/Output {currentIndex + 1} ({output.GetType().Name})";
                    foreach (var writer in GetOutputConflictWriters(output))
                    {
                        if (writers.TryGetValue(writer.Key, out var first))
                        {
                            var message = $"{current}: conflicts with {first}; both write {writer.Description}. Merge the input into one Route.";
                            if (reported.Add(message)) errors.Add(message);
                        }
                        else writers.Add(writer.Key, current);
                    }
                }
            }
        }

        private static IEnumerable<OutputConflictWriter> GetOutputConflictWriters(RouteNode output)
        {
            if (output == null) yield break;
            var typeName = output.GetType().Name;

            if (TryGetTransformOutputFootprint(output, typeName, out var transform, out var property, out var axis))
            {
                foreach (var writer in TransformFootprint(transform, property, axis)) yield return writer;
                yield break;
            }

            UnityEngine.Object target = null;
            var slot = 0;
            var kind = string.Empty;
            if (typeName.StartsWith("Renderer", StringComparison.Ordinal))
            {
                target = GetSerializedValue(output, "target") as Renderer;
                slot = Convert.ToInt32(GetSerializedValue(output, "materialIndex") ?? 0);
                kind = "renderer";
            }
            else if (typeName.StartsWith("Material", StringComparison.Ordinal))
            {
                var mode = GetSerializedValue(output, "outputMode")?.ToString();
                if (mode == "Instance")
                {
                    target = GetSerializedValue(output, "renderer") as Renderer;
                    slot = Convert.ToInt32(GetSerializedValue(output, "materialIndex") ?? 0);
                    kind = "renderer";
                }
                else
                {
                    target = GetSerializedValue(output, "target") as Material;
                    kind = "material";
                }
            }
            else
            {
                var type = output.GetType();
                var isVolumeParameter = false;
                for (; type != null && type != typeof(object); type = type.BaseType)
                    if (type.IsGenericType && type.GetGenericTypeDefinition().Name == "VolumeParameterOutput`1")
                    { isVolumeParameter = true; break; }
                if (!isVolumeParameter) yield break;
                target = GetSerializedValue(output, "target") as UnityEngine.Object;
                var component = GetSerializedValue(output, "componentTypeName") as string;
                var parameter = GetSerializedValue(output, "parameterName") as string;
                if (target == null || string.IsNullOrWhiteSpace(component) || string.IsNullOrWhiteSpace(parameter)) yield break;
                var volumeKey = $"volume/{target.GetInstanceID()}/{component}/{parameter}";
                var displayComponent = component.Split(',')[0].Trim();
                yield return new OutputConflictWriter(volumeKey, $"Volume {target.name} {displayComponent}.{parameter}");
                yield break;
            }

            var propertyName = GetSerializedValue(output, "propertyName") as string;
            if (string.IsNullOrWhiteSpace(propertyName) || target == null) yield break;
            var key = $"{kind}/{target.GetInstanceID()}/{slot}/{propertyName}";
            var description = kind == "renderer"
                ? $"Renderer {target.name} slot {slot} property '{propertyName}'"
                : $"Material {target.name} property '{propertyName}'";
            yield return new OutputConflictWriter(key, description);
        }

        private static bool TryGetTransformOutputFootprint(RouteNode output, string typeName,
            out Transform target, out string property, out string axis)
        {
            target = null;
            property = null;
            axis = null;
            var channel = -1;
            var worldSpace = false;
            if (typeName == "TransformPositionVector3Output" || typeName == "TransformPositionAxisFloatOutput") channel = 0;
            else if (typeName == "TransformRotationVector3Output" || typeName == "TransformRotationAxisFloatOutput") channel = 1;
            else if (typeName == "TransformScaleVector3Output" || typeName == "TransformScaleAxisFloatOutput") channel = 2;
            if (channel >= 0)
            {
                target = GetSerializedValue(output, "target") as Transform;
                worldSpace = channel < 2 && Convert.ToBoolean(GetSerializedValue(output, "worldSpace") ?? false);
                property = channel == 0 ? (worldSpace ? "position" : "localPosition")
                    : channel == 1 ? (worldSpace ? "eulerAngles" : "localEulerAngles") : "localScale";
                if (typeName.EndsWith("AxisFloatOutput", StringComparison.Ordinal))
                {
                    var axisValue = GetSerializedValue(output, "axis")?.ToString();
                    if (axisValue != "X" && axisValue != "Y" && axisValue != "Z") return false;
                    axis = axisValue;
                }
                return target != null;
            }

            Type componentOutput = null;
            for (var type = output.GetType(); type != null && type != typeof(object); type = type.BaseType)
                if (type.IsGenericType && type.GetGenericTypeDefinition().Name == "ComponentMemberOutput`1")
                { componentOutput = type; break; }
            if (componentOutput == null || componentOutput.GetGenericArguments()[0] != typeof(Vector3)) return false;
            target = GetMemberValue(output, "Target") as Transform;
            if (target == null) return false;
            var memberName = GetMemberValue(output, "MemberName") as string;
            switch (memberName)
            {
                case "position": property = "position"; break;
                case "localPosition": property = "localPosition"; break;
                case "eulerAngles": property = "eulerAngles"; break;
                case "localEulerAngles": property = "localEulerAngles"; break;
                case "localScale": property = "localScale"; break;
                default: return false;
            }
            return true;
        }

        private static IEnumerable<OutputConflictWriter> TransformFootprint(Transform target, string property, string axis)
        {
            if (target == null || string.IsNullOrWhiteSpace(property)) yield break;
            var instanceId = target.GetInstanceID();
            var description = $"Transform {target.name}.{property}";
            if (axis != null)
            {
                yield return new OutputConflictWriter($"transform/{instanceId}/{property}/{axis}", description);
                yield break;
            }
            yield return new OutputConflictWriter($"transform/{instanceId}/{property}/X", description);
            yield return new OutputConflictWriter($"transform/{instanceId}/{property}/Y", description);
            yield return new OutputConflictWriter($"transform/{instanceId}/{property}/Z", description);
        }

        private readonly struct OutputConflictWriter
        {
            public readonly string Key;
            public readonly string Description;
            public OutputConflictWriter(string key, string description) { Key = key; Description = description; }
        }

        internal static object GetRouteSource(IRoute route)
        {
            var property = route.GetType().GetProperty("Source");
            return property?.GetValue(route);
        }

        private static object GetRouteOutputs(IRoute route)
        {
            var property = route.GetType().GetProperty("Outputs");
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
                    if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType) && value == null && IsRequiredTargetField(field.Name)
                        && !IsMaterialOutputReference(node, field.Name))
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
            ValidateSpecializedTarget(host, route, node, errors);
            ValidateMidiControlReferences(host, route, node, errors);
            if (ValidateComponentMember(host, route, node, errors)) ValidateAotAccessor(host, route, node, errors);
        }

        private static bool IsMaterialOutputReference(RouteNode node, string fieldName)
            => node != null && node.GetType().Name.StartsWith("Material", StringComparison.Ordinal)
                && (fieldName == "target" || fieldName == "renderer");

        private static bool ValidateComponentMember(RouteHost host, IRoute route, RouteNode node, List<string> errors)
        {
            Type outputBase = null;
            for (var type = node.GetType(); type != null; type = type.BaseType)
                if (type.IsGenericType && type.GetGenericTypeDefinition().Name == "ComponentMemberOutput`1")
                { outputBase = type; break; }
            if (outputBase == null) return true;

            var target = GetMemberValue(node, "Target") as Component;
            var memberName = GetMemberValue(node, "MemberName") as string;
            if (target == null || string.IsNullOrWhiteSpace(memberName)) return true;
            var valueType = outputBase.GetGenericArguments()[0];

            var registryMethod = typeof(ComponentAccessorRegistry).GetMethods(BindingFlags.Static | BindingFlags.Public)
                .FirstOrDefault(method => method.Name == "TryGet" && method.IsGenericMethodDefinition);
            if (registryMethod != null)
            {
                var arguments = new object[] { target.GetType(), memberName, null };
                if ((bool)registryMethod.MakeGenericMethod(valueType).Invoke(null, arguments)) return true;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            var field = target.GetType().GetField(memberName, flags);
            if (field != null)
            {
                if (field.FieldType == valueType && !field.IsInitOnly && !field.IsLiteral) return true;
                errors.Add($"{host.name}/{route.Name}/{node.GetType().Name}: public field '{target.GetType().Name}.{memberName}' is not a writable {valueType.Name}.");
                return false;
            }

            var property = target.GetType().GetProperty(memberName, flags);
            if (property != null && property.PropertyType == valueType && property.GetSetMethod(nonPublic: false) != null) return true;
            errors.Add($"{host.name}/{route.Name}/{node.GetType().Name}: '{target.GetType().Name}.{memberName}' is not a public writable {valueType.Name} member.");
            return false;
        }

        private static void ValidateSpecializedTarget(RouteHost host, IRoute route, RouteNode node, List<string> errors)
        {
            var typeName = node.GetType().Name;
            if (typeName.StartsWith("VFX", StringComparison.Ordinal))
            {
                var target = GetSerializedValue(node, "target");
                var propertyName = GetSerializedValue(node, "propertyName") as string;
                var methodName = typeName switch
                {
                    "VFXFloatOutput" => "HasFloat",
                    "VFXIntOutput" => "HasInt",
                    "VFXUIntOutput" => "HasUInt",
                    "VFXBoolOutput" => "HasBool",
                    "VFXVector2Output" => "HasVector2",
                    "VFXVector3Output" => "HasVector3",
                    "VFXVector4Output" => "HasVector4",
                    "VFXColorOutput" => "HasVector4",
                    "VFXMatrixOutput" => "HasMatrix4x4",
                    "VFXTextureOutput" => "HasTexture",
                    "VFXTextureIndexOutput" => "HasTexture",
                    "VFXTriggerEventOutput" => "HasEvent",
                    _ => null
                };
                var propertyNameToCheck = typeName == "VFXTriggerEventOutput"
                    ? GetSerializedValue(node, "eventName") as string
                    : propertyName;
                if (target == null || string.IsNullOrWhiteSpace(propertyNameToCheck) || string.IsNullOrEmpty(methodName)) return;

                try
                {
                    var hasProperty = target.GetType().GetMethod(methodName, new[] { typeof(int) });
                    if (hasProperty == null) return;
                    var propertyId = Shader.PropertyToID(propertyNameToCheck);
                    if (!(bool)hasProperty.Invoke(target, new object[] { propertyId }))
                        errors.Add($"{host.name}/{route.Name}/{typeName}: VFX property '{propertyNameToCheck}' is missing or has the wrong type.");
                }
                catch (TargetInvocationException exception)
                {
                    errors.Add($"{host.name}/{route.Name}/{typeName}: could not validate VFX property '{propertyName}': {exception.InnerException?.Message ?? exception.Message}");
                }
                catch (Exception exception)
                {
                    errors.Add($"{host.name}/{route.Name}/{typeName}: could not validate VFX property '{propertyName}': {exception.Message}");
                }
            }

            if (typeName.StartsWith("Renderer", StringComparison.Ordinal) || typeName.StartsWith("Material", StringComparison.Ordinal))
                ValidateShaderProperty(host, route, node, errors);
            if (typeName.StartsWith("Animator", StringComparison.Ordinal))
                ValidateAnimatorTarget(host, route, node, errors);
            if (typeName.StartsWith("Volume", StringComparison.Ordinal))
                ValidateVolumeTarget(host, route, node, errors);
        }

        private static void ValidateShaderProperty(RouteHost host, IRoute route, RouteNode node, List<string> errors)
        {
            var helpers = node.GetType().Assembly.GetType("MondmadeVJ0.RenderOutputHelpers", throwOnError: false);
            if (helpers == null) return;
            var typeName = node.GetType().Name;
            var rendererOutput = typeName.StartsWith("Renderer", StringComparison.Ordinal);
            var materialOutput = typeName.StartsWith("Material", StringComparison.Ordinal);
            var instanceMaterialOutput = materialOutput && GetSerializedValue(node, "outputMode")?.ToString() == "Instance";
            var useRenderer = rendererOutput || instanceMaterialOutput;
            var method = helpers.GetMethod(useRenderer ? "ValidateRenderer" : "ValidateProperty",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null) return;

            var target = instanceMaterialOutput ? GetSerializedValue(node, "renderer") : GetSerializedValue(node, "target");
            var propertyName = GetSerializedValue(node, "propertyName") as string;
            if (materialOutput && target == null)
            {
                errors.Add($"{host.name}/{route.Name}/{node.GetType().Name}: { (instanceMaterialOutput ? "Renderer" : "Shared Asset material") } is not assigned.");
                return;
            }
            if (target == null || string.IsNullOrWhiteSpace(propertyName)) return;

            var expectedProperty = FindProperty(node.GetType(), "PropertyType")?.GetValue(node);
            var parameters = method.GetParameters();
            var shaderPropertyType = parameters[useRenderer ? 3 : 2].ParameterType;
            if (expectedProperty == null && node.GetType().Name.EndsWith("TextureIndexOutput", StringComparison.Ordinal))
                expectedProperty = Enum.Parse(shaderPropertyType, "Texture");
            if (expectedProperty == null) return;

            object[] arguments = useRenderer
                ? new[] { target, GetSerializedValue(node, "materialIndex") ?? 0, propertyName, expectedProperty }
                : new[] { target, propertyName, expectedProperty, "Material" };
            try { method.Invoke(null, arguments); }
            catch (TargetInvocationException exception)
            {
                errors.Add($"{host.name}/{route.Name}/{node.GetType().Name}: {exception.InnerException?.Message ?? exception.Message}");
            }
            catch (Exception exception)
            {
                errors.Add($"{host.name}/{route.Name}/{node.GetType().Name}: could not validate shader property '{propertyName}': {exception.Message}");
            }
        }

        private static void ValidateAnimatorTarget(RouteHost host, IRoute route, RouteNode node, List<string> errors)
        {
            if (!(GetSerializedValue(node, "target") is Animator animator)) return;
            var typeName = node.GetType().Name;
            if (animator.runtimeAnimatorController == null)
            {
                errors.Add($"{host.name}/{route.Name}/{typeName}: Animator has no RuntimeAnimatorController.");
                return;
            }

            var isAnimatorParameterOutput = typeName == "AnimatorTriggerOutput";
            for (var type = node.GetType(); type != null && !isAnimatorParameterOutput; type = type.BaseType)
                isAnimatorParameterOutput = type.Name.StartsWith("AnimatorParameterOutput`", StringComparison.Ordinal);
            if (isAnimatorParameterOutput)
            {
                var parameterName = GetSerializedValue(node, "parameterName") as string;
                if (string.IsNullOrWhiteSpace(parameterName)) return;
                var expected = typeName == "AnimatorTriggerOutput"
                    ? "Trigger"
                    : FindProperty(node.GetType(), "ParameterType")?.GetValue(node)?.ToString();
                var hash = Animator.StringToHash(parameterName);
                var found = false;
                foreach (var parameter in animator.parameters)
                    if (parameter.nameHash == hash && parameter.type.ToString() == expected) { found = true; break; }
                if (!found) errors.Add($"{host.name}/{route.Name}/{typeName}: Animator parameter '{parameterName}' is missing or is not {expected}.");
                return;
            }

            if (typeName == "AnimatorLayerWeightFloatOutput" || typeName == "AnimatorStateTriggerOutput")
            {
                var layerName = GetSerializedValue(node, "layerName") as string;
                var layerIndex = !string.IsNullOrEmpty(layerName)
                    ? animator.GetLayerIndex(layerName)
                    : (int)(GetSerializedValue(node, "layerIndex") ?? 0);
                if (layerIndex < 0 || layerIndex >= animator.layerCount)
                {
                    errors.Add($"{host.name}/{route.Name}/{typeName}: Animator layer '{layerName}' does not exist.");
                    return;
                }
                if (typeName == "AnimatorStateTriggerOutput")
                {
                    var stateName = GetSerializedValue(node, "stateName") as string;
                    if (!string.IsNullOrWhiteSpace(stateName) && !animator.HasState(layerIndex, Animator.StringToHash(stateName)))
                        errors.Add($"{host.name}/{route.Name}/{typeName}: Animator state '{stateName}' does not exist on layer {layerIndex}.");
                }
            }
        }

        private static void ValidateVolumeTarget(RouteHost host, IRoute route, RouteNode node, List<string> errors)
        {
            Type outputBase = null;
            for (var type = node.GetType(); type != null; type = type.BaseType)
                if (type.IsGenericType && type.GetGenericTypeDefinition().Name == "VolumeParameterOutput`1")
                { outputBase = type; break; }
            if (outputBase == null || GetSerializedValue(node, "target") == null) return;

            var typeName = GetSerializedValue(node, "componentTypeName") as string;
            var parameterName = GetSerializedValue(node, "parameterName") as string;
            if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(parameterName)) return;
            var componentType = Type.GetType(typeName, throwOnError: false);
            if (componentType == null)
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    if ((componentType = assembly.GetType(typeName, throwOnError: false)) != null) break;
            if (componentType == null || !typeof(ScriptableObject).IsAssignableFrom(componentType))
            {
                errors.Add($"{host.name}/{route.Name}/{node.GetType().Name}: Volume component type '{typeName}' does not exist.");
                return;
            }

            var field = componentType.GetField(parameterName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var expectedType = outputBase.GetGenericArguments()[0];
            var parameterType = field?.FieldType;
            while (parameterType != null && !(parameterType.IsGenericType && parameterType.GetGenericTypeDefinition().Name == "VolumeParameter`1"))
                parameterType = parameterType.BaseType;
            if (field == null || parameterType == null || parameterType.GetGenericArguments()[0] != expectedType)
            {
                errors.Add($"{host.name}/{route.Name}/{node.GetType().Name}: Volume parameter '{componentType.Name}.{parameterName}' is missing or is not a {expectedType.Name} parameter.");
                return;
            }

            var target = GetSerializedValue(node, "target");
            var hasInstantiatedProfile = target?.GetType().GetMethod("HasInstantiatedProfile", Type.EmptyTypes);
            var profile = hasInstantiatedProfile != null && (bool)hasInstantiatedProfile.Invoke(target, null)
                ? GetMemberValue(target, "profile")
                : GetMemberValue(target, "sharedProfile");
            if (profile == null)
            {
                errors.Add($"{host.name}/{route.Name}/{node.GetType().Name}: Volume target has no profile.");
                return;
            }
            var hasComponent = GetMemberValue(profile, "components") is System.Collections.IEnumerable components
                && components.Cast<object>().Any(component => component != null && component.GetType() == componentType);
            var overrideMode = GetSerializedValue(node, "overrideMode")?.ToString();
            if (!hasComponent && overrideMode != "Automatically")
                errors.Add($"{host.name}/{route.Name}/{node.GetType().Name}: Volume profile does not contain {componentType.Name}.");
            else if (hasComponent)
            {
                var component = ((System.Collections.IEnumerable)GetMemberValue(profile, "components")).Cast<object>()
                    .FirstOrDefault(candidate => candidate != null && candidate.GetType() == componentType);
                if (field.GetValue(component) == null)
                    errors.Add($"{host.name}/{route.Name}/{node.GetType().Name}: Volume parameter '{componentType.Name}.{parameterName}' is null.");
            }
        }

        private static void ValidateMidiControlReferences(RouteHost host, IRoute route, RouteNode node, List<string> errors)
        {
            var controlFields = EnumerateSerializedFields(node.GetType())
                .Where(field => field.FieldType.Name == "MidiControlReference"
                    || field.FieldType.IsArray && field.FieldType.GetElementType()?.Name == "MidiControlReference")
                .ToArray();
            if (controlFields.Length == 0) return;

            var hub = FindConfiguredMidiHub(host);
            if (hub == null)
            {
                errors.Add($"{host.name}/{route.Name}/{node.GetType().Name}: no MidiHub module is configured for this RouteHost.");
                return;
            }

            foreach (var field in controlFields)
            {
                var fieldValue = field.GetValue(node);
                if (fieldValue is System.Collections.IEnumerable references && !(fieldValue is string))
                {
                    foreach (var reference in references)
                        ValidateMidiReference(host, route, node, field.Name, reference, hub, errors);
                }
                else ValidateMidiReference(host, route, node, field.Name, fieldValue, hub, errors);
            }
        }

        private static void ValidateMidiReference(RouteHost host, IRoute route, RouteNode node, string fieldName,
            object reference, Component hub, List<string> errors)
        {
            if (reference == null) return;
            var surfaceId = GetMemberValue(reference, "SurfaceInstanceId") as string;
            var controlId = GetMemberValue(reference, "ControlId") as string;
            if (!TryFindMidiControl(hub, surfaceId, controlId, out var definition))
            {
                errors.Add($"{host.name}/{route.Name}/{node.GetType().Name}: MIDI control '{surfaceId}/{controlId}' in '{fieldName}' does not exist in the configured MidiHub.");
                return;
            }

            var constraint = node.GetType().GetInterfaces().FirstOrDefault(type => type.Name == "IMidiControlTypeConstraint");
            var accepts = constraint?.GetMethod("AcceptsControlType");
            var controlType = GetMemberValue(definition, "type");
            if (accepts != null && !(bool)accepts.Invoke(node, new[] { (object)fieldName, controlType }))
                errors.Add($"{host.name}/{route.Name}/{node.GetType().Name}: MIDI control '{controlId}' has incompatible Control Type '{controlType}'.");

            if (node.GetType().Name.StartsWith("MidiLed", StringComparison.Ordinal)
                && !Convert.ToBoolean(GetMemberValue(definition, "hasLed")))
                errors.Add($"{host.name}/{route.Name}/{node.GetType().Name}: MIDI control '{controlId}' does not have an LED.");
        }

        private static Component FindConfiguredMidiHub(RouteHost host)
        {
            var scene = host.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded) return null;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var runtime in root.GetComponentsInChildren<MondmadeVJ0Runtime>(true))
            {
                var routeHosts = GetSerializedValue(runtime, "routeHosts") as RouteHost[];
                if (routeHosts == null || !routeHosts.Contains(host)) continue;
                if (!(GetSerializedValue(runtime, "modules") is MonoBehaviour[] modules)) continue;
                foreach (var module in modules)
                    if (module != null && module.GetType().Name == "MidiHub") return module;
            }
            return null;
        }

        private static bool TryFindMidiControl(Component hub, string surfaceId, string controlId, out object definition)
        {
            definition = null;
            if (!(GetMemberValue(hub, "Surfaces") is System.Collections.IEnumerable surfaces)) return false;
            foreach (var surface in surfaces)
            {
                if (surface == null || !string.Equals(GetMemberValue(surface, "InstanceId") as string, surfaceId, StringComparison.Ordinal)) continue;
                var profile = GetMemberValue(surface, "Profile");
                if (!(GetMemberValue(profile, "Controls") is System.Collections.IEnumerable controls)) return false;
                foreach (var control in controls)
                    if (control != null && string.Equals(GetMemberValue(control, "id") as string, controlId, StringComparison.Ordinal))
                    { definition = control; return true; }
                return false;
            }
            return false;
        }

        private static IEnumerable<FieldInfo> EnumerateSerializedFields(Type type)
        {
            for (; type != null && type != typeof(object); type = type.BaseType)
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!field.IsStatic && !field.IsNotSerialized) yield return field;
        }

        private static object GetSerializedValue(object target, string fieldName)
            => FindSerializedField(target?.GetType(), fieldName)?.GetValue(target);

        private static FieldInfo FindSerializedField(Type type, string fieldName)
        {
            for (; type != null && type != typeof(object); type = type.BaseType)
            {
                var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }

        private static PropertyInfo FindProperty(Type type, string propertyName)
        {
            for (; type != null && type != typeof(object); type = type.BaseType)
            {
                var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property != null) return property;
            }
            return null;
        }

        private static object GetMemberValue(object target, string memberName)
        {
            if (target == null) return null;
            var type = target.GetType();
            return type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target)
                ?? type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);
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
                var registryMethod = typeof(ComponentAccessorRegistry).GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .FirstOrDefault(method => method.Name == "TryGet" && method.IsGenericMethodDefinition);
                if (registryMethod != null)
                {
                    var arguments = new object[] { target.GetType(), member, null };
                    if ((bool)registryMethod.MakeGenericMethod(valueType).Invoke(null, arguments)) return;
                }
                var key = ComponentAccessorGenerator.Key(target.GetType().AssemblyQualifiedName, member, valueType.AssemblyQualifiedName);
                if (!ComponentAccessorGenerator.GeneratedKeys().Contains(key))
                    errors.Add($"{host.name}/{route.Name}/{node.GetType().Name}: generate the IL2CPP accessor for {target.GetType().Name}.{member}.");
                return;
            }
        }

        private static bool IsRequiredTargetField(string field) => field is "target" or "signal" or "Signal" or "profile" or "action";
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
