using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace EventSystem2 {
    public class EventListenerModifierSystem : MonoBehaviour, IEventListenerModifierSystem, ISerializationCallbackReceiver {
        [Searchable, DictionaryDrawerSettings(DisplayMode = DictionaryDisplayOptions.Foldout, KeyLabel = "Type"), ShowInInspector]
        public Dictionary<Type, EventModifierContainer> modifiersDict = new();

        [SerializeField, HideInInspector]
        private List<ModifierSerializationHelper> serializedModifiers = new();

        public void Dispatch<T>(T @event) {
            if (!modifiersDict.TryGetValue(typeof(T), out var container)) {
                InvokeEvent(@event);
                return;
            }

            if (container.modifiers.Count > 0)
                container.modifiers[0].Push(in @event);
            else
                InvokeEvent(@event);
        }

        [Button, PropertyOrder(-10)]
        public void AddType(Type type) {
            if (modifiersDict.ContainsKey(type))
                return;

            var genericContainerType = typeof(EventModifierContainer<>).MakeGenericType(type);
            var container = (EventModifierContainer)Activator.CreateInstance(genericContainerType);
            modifiersDict[type] = container;
        }

        public void AddType<T>() {
            if (modifiersDict.ContainsKey(typeof(T)))
                return;

            var container = EventModifierContainer<T>.CreateInstance();
            modifiersDict[typeof(T)] = container;
        }

        public void Continue<T>(in T @event, EventModifier modifier) {
            if (!modifiersDict.TryGetValue(typeof(T), out var container)) {
                Debug.LogWarning($"({modifier}) No modifiers found for event {typeof(T)}.");
                return;
            }

            var index = container.modifiers.IndexOf(modifier);

            if (index == -1)
                return;

            if (index + 1 < container.modifiers.Count) 
                container.modifiers[index + 1].Push(in @event);
            else
                InvokeEvent(@event);
        }

        private void InvokeEvent<T>(T @event) {
            if (modifiersDict.TryGetValue(typeof(T), out var container)) {
                if (container is EventModifierContainer<T> typedContainer) 
                    typedContainer.onEvent.Invoke(@event);
            }

            var list = ComponentList<IEventListener<T>>.list;
            GetComponentsInChildren(false, list);

            foreach (var listener in list)
                listener.OnEvent(@event);
        }

        public void OnAfterDeserialize() {
            modifiersDict.Clear();

            foreach (var item in serializedModifiers) {
                var type = Type.GetType(item.typeName);

                if (type != null) 
                    modifiersDict[type] = item.container;
            }
        }

        public void OnBeforeSerialize() {
            serializedModifiers.Clear();

            foreach (var kvp in modifiersDict) {
                serializedModifiers.Add(new() {
                    typeName = kvp.Key.AssemblyQualifiedName,
                    container = kvp.Value
                });
            }
        }

        [Serializable]
        private class ModifierSerializationHelper {
            public Type type;
            public string typeName;

            [SerializeReference]
            public EventModifierContainer container;
        }

        public static class ComponentList<T> {
            public static List<T> list = new();
        }
    }
}