using System.Text;
using EventSystem2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws EventModified&lt;T&gt; fields: foldout header with a live value badge (play mode),
/// the native [SerializeReference] pipeline list underneath, and per-modifier live handle
/// counts while playing. Lives in the plugin (only needs plugin types + UnityEditor).
/// </summary>
[CustomPropertyDrawer(typeof(EventModified), true)]
public class EventModifiedDrawer : PropertyDrawer {
    private static readonly GUIContent PipelineLabel = new("Pipeline");

    private static GUIStyle _badgeStyle;
    private static GUIStyle BadgeStyle =>
        _badgeStyle ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
        var height = EditorGUIUtility.singleLineHeight;

        if (!property.isExpanded)
            return height;

        var pipeline = property.FindPropertyRelative("_pipeline");

        if (pipeline == null)
            return height;

        height += EditorGUIUtility.standardVerticalSpacing
            + EditorGUI.GetPropertyHeight(pipeline, PipelineLabel, true);

        if (Application.isPlaying)
            height += EditorGUIUtility.standardVerticalSpacing + EditorGUIUtility.singleLineHeight;

        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
        var header = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        var live = LiveInstance(property);

        var badgeWidth = DrawValueBadge(header, live);
        var foldRect = new Rect(header.x, header.y, header.width - badgeWidth, header.height);
        property.isExpanded = EditorGUI.Foldout(foldRect, property.isExpanded, label, true);

        if (!property.isExpanded)
            return;

        var pipeline = property.FindPropertyRelative("_pipeline");

        if (pipeline == null) {
            var msg = new Rect(position.x, header.yMax + EditorGUIUtility.standardVerticalSpacing,
                position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(msg, "Field is null — initialize it (new EventModified<T>(...)).",
                EditorStyles.miniLabel);
            return;
        }

        var listRect = new Rect(position.x, header.yMax + EditorGUIUtility.standardVerticalSpacing,
            position.width, EditorGUI.GetPropertyHeight(pipeline, PipelineLabel, true));

        EditorGUI.indentLevel++;
        EditorGUI.PropertyField(listRect, pipeline, PipelineLabel, true);
        EditorGUI.indentLevel--;

        DrawLiveCounts(listRect, live);
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

    /// <summary>Per-modifier live handle counts, below the list, play mode only.</summary>
    private static void DrawLiveCounts(Rect listRect, EventModified live) {
        if (!Application.isPlaying || live == null)
            return;

        var counts = new StringBuilder();

        foreach (var modifier in live.Pipeline) {
            counts.Append(modifier.GetType().Name);
            counts.Append(" x");
            counts.Append(modifier.LiveHandleCount);
            counts.Append("   ");
        }

        var rect = new Rect(listRect.x + EditorGUIUtility.labelWidth,
            listRect.yMax + EditorGUIUtility.standardVerticalSpacing,
            listRect.width - EditorGUIUtility.labelWidth, EditorGUIUtility.singleLineHeight);

        EditorGUI.LabelField(rect, counts.ToString().TrimEnd(), EditorStyles.miniLabel);
    }

    /// <summary>The live .NET instance behind the field (not a serialization copy) — via drawer FieldInfo.</summary>
    private EventModified LiveInstance(SerializedProperty property) {
        var target = property.serializedObject.targetObject;
        return target != null ? fieldInfo?.GetValue(target) as EventModified : null;
    }
}
