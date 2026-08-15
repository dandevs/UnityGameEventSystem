using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EventPipelines;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>
/// Draws EventModified&lt;T&gt; fields: foldout header with a live value badge (play mode),
/// the native [SerializeReference] pipeline list underneath, per-modifier live handle
/// counts while playing, and an Add Modifier menu that discovers concrete EventModifier
/// subclasses via TypeCache. Lives in the plugin (needs only plugin types + UnityEditor).
/// </summary>
[CustomPropertyDrawer(typeof(EventModified), true)]
public class EventModifiedDrawer : PropertyDrawer
{
    private static readonly GUIContent PipelineLabel = new("Pipeline");
    private static readonly GUIContent AddLabel = new("Add Modifier…");

    private static GUIStyle _badgeStyle;
    private static GUIStyle BadgeStyle =>
        _badgeStyle ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };

    private static GUIStyle _countsStyle;
    private static GUIStyle CountsStyle =>
        _countsStyle ??= new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var spacing = EditorGUIUtility.standardVerticalSpacing;
        var height = EditorGUIUtility.singleLineHeight;

        if (!property.isExpanded)
            return height;

        var pipeline = property.FindPropertyRelative("_pipeline");

        if (pipeline == null)
            return height;

        height += spacing + EditorGUI.GetPropertyHeight(pipeline, PipelineLabel, true);

        if (Application.isPlaying && LiveInstance(property) is { } live)
            height += spacing + CountsHeight(live);   // live handle counts (wraps)

        return height + spacing + EditorGUIUtility.singleLineHeight; // Add Modifier button
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var spacing = EditorGUIUtility.standardVerticalSpacing;
        var line = EditorGUIUtility.singleLineHeight;
        var live = LiveInstance(property);

        var badgeWidth = DrawValueBadge(position, live);
        var foldRect = new Rect(position.x, position.y, position.width - badgeWidth, line);
        property.isExpanded = EditorGUI.Foldout(foldRect, property.isExpanded, label, true);

        if (!property.isExpanded)
            return;

        var pipeline = property.FindPropertyRelative("_pipeline");

        if (pipeline == null)
        {
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

        if (Application.isPlaying && live != null)
        {
            var countsRect = new Rect(listRect.x + EditorGUIUtility.labelWidth, y,
                listRect.width - EditorGUIUtility.labelWidth, CountsHeight(live));
            y = countsRect.yMax + spacing;
            GUI.Label(countsRect, LiveCountsText(live), CountsStyle);
        }

        var buttonRect = new Rect(position.x, y, position.width, line);
        if (GUI.Button(buttonRect, AddLabel, EditorStyles.miniButton))
            OpenAddMenu(buttonRect, pipeline);
    }

    /// <summary>Right-aligned "Value: x" badge on the header line; returns the width it used.</summary>
    private static float DrawValueBadge(Rect header, EventModified live)
    {
        if (!Application.isPlaying || live == null)
            return 0f;

        var text = $"Value: {live.BoxedLatest ?? "null"}";
        var width = BadgeStyle.CalcSize(new GUIContent(text)).x + 8f;

        EditorGUI.LabelField(new Rect(header.xMax - width, header.y, width, header.height), text, BadgeStyle);
        return width;
    }

    /// <summary>
    /// Per-modifier live handle counts as one string (play mode only). Null elements are
    /// shown as "Null" — they are skipped at runtime, but stay visible here.
    /// </summary>
    private static string LiveCountsText(EventModified live)
    {
        var counts = new StringBuilder();

        foreach (var modifier in live.Pipeline)
        {
            counts.Append(modifier == null
                ? "Null"
                : $"{ModifierLabels.LabelFor(modifier.GetType())} x{modifier.LiveHandleCount}");
            counts.Append("   ");
        }

        return counts.Length == 0 ? "no modifiers" : counts.ToString().TrimEnd();
    }

    /// <summary>Height for the counts block — wraps once the inspector runs out of width.</summary>
    private static float CountsHeight(EventModified live)
    {
        var height = CountsStyle.CalcHeight(new GUIContent(LiveCountsText(live)), CountsWidth());
        return Mathf.Max(height, EditorGUIUtility.singleLineHeight);
    }

    /// <summary>
    /// Width estimate for the wrap calc. Deliberately UNDER-estimates (view width includes
    /// the scrollbar; fallback for non-GUI contexts) so line count over-estimates —
    /// over-allocated height wastes a pixel, under-allocated height clips text.
    /// </summary>
    private static float CountsWidth()
    {
        var view = EditorGUIUtility.currentViewWidth;
        if (view <= 0f)
            view = 320f;
        return Mathf.Max(60f, view - 16f - EditorGUIUtility.labelWidth);
    }

    private static void OpenAddMenu(Rect dropdownRect, SerializedProperty pipeline) =>
        new ModifierDropdown(pipeline).Show(dropdownRect);

    /// <summary>Searchable, keyboard-navigable picker (Unity's own dropdown style), grouped by pattern.</summary>
    private class ModifierDropdown : AdvancedDropdown
    {
        private readonly SerializedProperty _pipeline;

        private static readonly AdvancedDropdownState State = new();   // remembers expansion between opens

        public ModifierDropdown(SerializedProperty pipeline) : base(State) => _pipeline = pipeline;

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Add Modifier");
            var perEvent = new AdvancedDropdownItem("Per-Event");
            var stream = new AdvancedDropdownItem("Stream (Persistent)");

            foreach (var type in GetAddableModifierTypes())
            {
                var item = new ModifierItem(type, LabelFor(type));

                if (IsStreamModifier(type))
                    stream.AddChild(item);
                else
                    perEvent.AddChild(item);
            }

            if (perEvent.childList.Count > 0) root.AddChild(perEvent);
            if (stream.childList.Count > 0) root.AddChild(stream);

            if (root.childList.Count == 0)
                root.AddChild(new AdvancedDropdownItem("No concrete EventModifier types found") { enabled = false });

            return root;
        }

        /// <summary>
        /// True if the modifier derives from EventModifierPersistent&lt;,&gt;. Deliberately a base-chain
        /// walk: open-generic IsAssignableFrom returns false on this runtime (Unity 6 / Core semantics).
        /// </summary>
        private static bool IsStreamModifier(Type type)
        {
            for (var baseType = type.BaseType; baseType != null; baseType = baseType.BaseType)
                if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(EventModifierPersistent<,>))
                    return true;

            return false;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is ModifierItem modifier)
                AddModifier(_pipeline, modifier.Type);
        }

        /// <summary>
        /// Carries the payload type. AdvancedDropdownItem.id is unusable for lookups — AddChild
        /// rewrites child ids internally — so the type travels on a subclass instead.
        /// </summary>
        private class ModifierItem : AdvancedDropdownItem
        {
            public readonly Type Type;

            public ModifierItem(Type type, string label) : base(label) => Type = type;
        }

        /// <summary>Display names are shared with list-element labels — see ModifierLabels.</summary>
        private static string LabelFor(Type type) => ModifierLabels.LabelFor(type);
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
    public static void AddModifier(SerializedProperty pipeline, Type type)
    {
        object instance;

        try
        {
            instance = Activator.CreateInstance(type);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"EventModifiedDrawer: cannot construct {type.Name} ({e.GetType().Name}) — needs a parameterless ctor.");
            return;
        }

        pipeline.arraySize++;
        pipeline.GetArrayElementAtIndex(pipeline.arraySize - 1).managedReferenceValue = instance;
        pipeline.serializedObject.ApplyModifiedProperties();
    }

    /// <summary>The live .NET instance behind the field (not a serialization copy) — via drawer FieldInfo.</summary>
    private EventModified LiveInstance(SerializedProperty property)
    {
        var target = property.serializedObject.targetObject;
        return target != null ? fieldInfo?.GetValue(target) as EventModified : null;
    }
}
