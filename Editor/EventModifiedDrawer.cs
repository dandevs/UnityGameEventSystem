using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EventSystem2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws EventModified&lt;T&gt; fields: foldout header with a live value badge (play mode),
/// the native [SerializeReference] pipeline list underneath, per-modifier live handle
/// counts while playing, and an Add Modifier menu that discovers concrete EventModifier
/// subclasses via TypeCache. Lives in the plugin (needs only plugin types + UnityEditor).
/// </summary>
[CustomPropertyDrawer(typeof(EventModified), true)]
public class EventModifiedDrawer : PropertyDrawer {
    private static readonly GUIContent PipelineLabel = new("Pipeline");
    private static readonly GUIContent AddLabel = new("Add Modifier…");

    private static GUIStyle _badgeStyle;
    private static GUIStyle BadgeStyle =>
        _badgeStyle ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
        var spacing = EditorGUIUtility.standardVerticalSpacing;
        var height = EditorGUIUtility.singleLineHeight;

        if (!property.isExpanded)
            return height;

        var pipeline = property.FindPropertyRelative("_pipeline");

        if (pipeline == null)
            return height;

        height += spacing + EditorGUI.GetPropertyHeight(pipeline, PipelineLabel, true);

        if (Application.isPlaying && LiveInstance(property) != null)
            height += spacing + EditorGUIUtility.singleLineHeight;   // live handle counts

        return height + spacing + EditorGUIUtility.singleLineHeight; // Add Modifier button
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
        var spacing = EditorGUIUtility.standardVerticalSpacing;
        var line = EditorGUIUtility.singleLineHeight;
        var live = LiveInstance(property);

        var badgeWidth = DrawValueBadge(position, live);
        var foldRect = new Rect(position.x, position.y, position.width - badgeWidth, line);
        property.isExpanded = EditorGUI.Foldout(foldRect, property.isExpanded, label, true);

        if (!property.isExpanded)
            return;

        var pipeline = property.FindPropertyRelative("_pipeline");

        if (pipeline == null) {
            var msg = new Rect(position.x, position.y + line + spacing, position.width, line);
            EditorGUI.LabelField(msg, "Field is null — initialize it (new EventModified<T>(...)).",
                EditorStyles.miniLabel);
            return;
        }

        var y = position.y + line + spacing;

        var listRect = new Rect(position.x, y, position.width,
            EditorGUI.GetPropertyHeight(pipeline, PipelineLabel, true));
        y = listRect.yMax + spacing;

        EditorGUI.indentLevel++;
        EditorGUI.PropertyField(listRect, pipeline, PipelineLabel, true);
        EditorGUI.indentLevel--;

        if (Application.isPlaying && live != null) {
            var countsRect = new Rect(listRect.x + EditorGUIUtility.labelWidth, y,
                listRect.width - EditorGUIUtility.labelWidth, line);
            y = countsRect.yMax + spacing;
            DrawLiveCounts(countsRect, live);
        }

        var buttonRect = new Rect(position.x, y, position.width, line);
        if (GUI.Button(buttonRect, AddLabel, EditorStyles.miniButton))
            OpenAddMenu(buttonRect, pipeline);
    }

    /// <summary>Right-aligned "Value: x" badge on the header line; returns the width it used.</summary>
    private static float DrawValueBadge(Rect header, EventModified live) {
        if (!Application.isPlaying || live == null)
            return 0f;

        var text = $"Value: {live.BoxedLatest ?? "null"}";
        var width = BadgeStyle.CalcSize(new GUIContent(text)).x + 8f;

        EditorGUI.LabelField(new Rect(header.xMax - width, header.y, width, header.height), text, BadgeStyle);
        return width;
    }

    /// <summary>Per-modifier live handle counts, play mode only.</summary>
    private static void DrawLiveCounts(Rect rect, EventModified live) {
        var counts = new StringBuilder();

        foreach (var modifier in live.Pipeline) {
            counts.Append(modifier.GetType().Name);
            counts.Append(" x");
            counts.Append(modifier.LiveHandleCount);
            counts.Append("   ");
        }

        EditorGUI.LabelField(rect, counts.ToString().TrimEnd(), EditorStyles.miniLabel);
    }

    private static void OpenAddMenu(Rect dropdownRect, SerializedProperty pipeline) {
        var menu = new GenericMenu();
        var found = false;

        foreach (var type in GetAddableModifierTypes()) {
            found = true;
            menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(type.Name)), false,
                () => AddModifier(pipeline, type));
        }

        if (!found)
            menu.AddDisabledItem(new GUIContent("No concrete EventModifier types found"));

        menu.DropDown(dropdownRect);
    }

    /// <summary>
    /// Concrete EventModifier subclasses the Add menu offers: non-abstract, closed
    /// (non-generic-definition), [Serializable], parameterless ctor — i.e. exactly the
    /// types that survive a [SerializeReference] pipeline round-trip.
    /// </summary>
    public static IEnumerable<Type> GetAddableModifierTypes() =>
        TypeCache.GetTypesDerivedFrom<EventModifier>()
            .Where(IsAddable)
            .OrderBy(t => t.Name);

    private static bool IsAddable(Type type) =>
        !type.IsAbstract
        && !type.IsGenericTypeDefinition
        && type.IsDefined(typeof(SerializableAttribute), false)
        && type.GetConstructor(Type.EmptyTypes) != null;

    /// <summary>Appends a new modifier instance to the pipeline (undo-able).</summary>
    public static void AddModifier(SerializedProperty pipeline, Type type) {
        object instance;

        try {
            instance = Activator.CreateInstance(type);
        }
        catch (Exception e) {
            Debug.LogWarning($"EventModifiedDrawer: cannot construct {type.Name} ({e.GetType().Name}) — needs a parameterless ctor.");
            return;
        }

        pipeline.arraySize++;
        pipeline.GetArrayElementAtIndex(pipeline.arraySize - 1).managedReferenceValue = instance;
        pipeline.serializedObject.ApplyModifiedProperties();
    }

    /// <summary>The live .NET instance behind the field (not a serialization copy) — via drawer FieldInfo.</summary>
    private EventModified LiveInstance(SerializedProperty property) {
        var target = property.serializedObject.targetObject;
        return target != null ? fieldInfo?.GetValue(target) as EventModified : null;
    }
}
