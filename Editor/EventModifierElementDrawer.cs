using System;
using EventSystem2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Labels managed-reference EventModifier list elements by their concrete type
/// ("Element 0" → "Delay") wherever they are drawn — EventModifiedDrawer's pipeline
/// list and plain [SerializeReference] fallback lists alike. Default drawing
/// (foldout, children, type picker, reorder) is fully preserved.
/// </summary>
[CustomPropertyDrawer(typeof(EventModifier), true)]
public class EventModifierElementDrawer : PropertyDrawer {
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
        if (label != null)
            ModifierLabels.RewriteLabel(property, label);

        EditorGUI.PropertyField(position, property, label, true);
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        => EditorGUI.GetPropertyHeight(property, label, true);
}

/// <summary>Single source of truth for modifier display names (list elements + Add dropdown).</summary>
internal static class ModifierLabels {
    /// <summary>Rewrites label.text in place for managed-reference EventModifier elements.</summary>
    public static void RewriteLabel(SerializedProperty property, GUIContent label) {
        if (property.propertyType != SerializedPropertyType.ManagedReference)
            return;

        label.text = property.managedReferenceValue == null
            ? "Null"   // native list "+" inserts null references — make them obvious
            : LabelFor(property.managedReferenceFullTypename);
    }

    /// <summary>"Assembly-CSharp DelayEventModifier" → "Delay".</summary>
    public static string LabelFor(string fullTypename) {
        var name = fullTypename.Substring(fullTypename.LastIndexOf(' ') + 1);   // strip assembly
        name = name.Substring(name.LastIndexOf('.') + 1);                       // strip namespace
        return Nicify(StripSuffix(name));
    }

    public static string LabelFor(Type type) => Nicify(StripSuffix(type.Name));

    /// <summary>"DelayEventModifier" → "Delay". Full type name stays greppable in the codebase.</summary>
    private static string StripSuffix(string name) {
        if (name.EndsWith("EventModifier", StringComparison.Ordinal))
            name = name[..^"EventModifier".Length];
        else if (name.EndsWith("Modifier", StringComparison.Ordinal))
            name = name[..^"Modifier".Length];

        return name;
    }

    private static string Nicify(string name) => ObjectNames.NicifyVariableName(name);
}
