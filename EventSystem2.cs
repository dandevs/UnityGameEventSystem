using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UltEvents;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace EventSystem2 {
    [Serializable, HideReferenceObjectPicker]
    public class EventModifierContainer {
        [InlineEditor]
        public List<EventModifier> modifiers = new();
    }

    [Serializable, HideReferenceObjectPicker]
    public class EventModifierContainer<TEvent> : EventModifierContainer {
        [PropertyOrder(-1), HideReferenceObjectPicker, SerializeReference]
        public UltEvent<TEvent> onEvent = new();

        public static EventModifierContainer<TEvent> CreateInstance() {
            return new EventModifierContainer<TEvent>();
        }
    }

    //------------------------------------------------------------------------------------------------------------------

    public interface IEventListener<T> {
        public void OnEvent(T @event);
    }

    public static class EventSystem2Utils {
        public static void DispatchEvent<T>(this GameObject gameObject, T @event) {
            if (!gameObject.TryGetComponent<EventListenerModifierSystem>(out var system)) {
                var list = EventListenerModifierSystem.ComponentList<IEventListener<T>>.list;
                gameObject.GetComponentsInChildren(false, list);

                foreach (var listener in list)
                    listener.OnEvent(@event);
            }
            else
                system.Dispatch(@event);
        }
    }

    public interface IEventListenerModifierSystem {
        public void Dispatch<T>(T @event);
        public void Continue<T>(in T @event, EventModifier modifier);
    }
}