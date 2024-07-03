using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Drawing;
using Sirenix.OdinInspector;
using UltEvents;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace EventSystem2 {
    public class EventListenerModifierSystem : MonoBehaviour, ISerializationCallbackReceiver {
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
                container.modifiers[0].Push(@event);
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
            if (!modifiersDict.TryGetValue(typeof(T), out var container))
                return;
                
            var index = container.modifiers.IndexOf(modifier);

            if (index == -1)
                return;

            if (index + 1 < container.modifiers.Count)
                container.modifiers[index + 1].Push(@event);
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

    public abstract class EventModifier : MonoBehaviourGizmos {
        protected virtual int defaultOrder => 0;

        private EventListenerModifierSystem _system;
        public EventListenerModifierSystem system {
            get {
                if (_system == null)
                    _system = GetComponentInParent<EventListenerModifierSystem>();

                return _system;
            }
            
            set => _system = value;
        }
        
        [SerializeField, HideInInspector]
        private int _order = int.MinValue;

        [ShowInInspector]
        public int order {
            get => _order == int.MinValue ? defaultOrder : _order;
            set => _order = value;
        }

        public abstract void Push<T>(T @event);

        public void Continue<T>(in T @event) {
            system.Continue(in @event, this);
        }
    }

    public abstract class EventModifier<THandle> : EventModifier where THandle : EventHandle, new() {
        [PropertyOrder(1000)]
        public List<THandle> handles = new();

        public override void Push<T>(T @event) {
            var handle = EventHandle.GetHandle<THandle>();

            if (handle.Initialize(@event, this)) {
                handle.Enter();
                handles.Add(handle);
            }
            else {
                EventHandle.ReturnHandle(handle);
                Debug.LogWarning($"Failed to initialize handle for event {typeof(T)}.");
                // handlePool.Push(handle);
            }
        }

        protected virtual void Update() {
            for (var i = 0; i < handles.Count; i++) {
                var handle = handles[i];

                if (handle.Update()) {
                    handle.Exit();
                    handles.RemoveAt(i);
                    EventHandle.ReturnHandle(handle);
                    i--;
                }
            }
        }
    }

    //------------------------------------------------------------------------------------------------------------------

    public abstract class EventHandle {
        private static readonly Dictionary<Type, Stack<EventHandle>> handlePools = new();
        protected int frameLastUpdated = -1;
        protected virtual Type eventType => typeof(object);

        public abstract void Enter();
        public abstract void Exit();
        public abstract bool Update();

        protected virtual void OnEnter() {}
        protected virtual void OnExit() {}
        protected abstract bool OnUpdate<T>(ref T @event);

        public abstract bool Initialize<T>(T @event, EventModifier modifier);

        public bool IsType<TSource, TTarget>(TSource @in, out TTarget @out, out Func<TTarget, TSource> recast) {
            if (typeof(TSource) == typeof(TTarget)) {
                @out = UnsafeUtility.As<TSource, TTarget>(ref @in);
                recast = static (TTarget value) => UnsafeUtility.As<TTarget, TSource>(ref value);
                return true;
            }

            @out = default;
            recast = null;
            return false;
        }

        public static T GetHandle<T>() where T : EventHandle, new() {
            var type = typeof(T);

            if (!handlePools.TryGetValue(type, out var pool)) 
                handlePools[type] = pool = new Stack<EventHandle>();

            return pool.TryPop(out var handle) ? (T)handle : new T();
        }

        public static void ReturnHandle(EventHandle handle) {
            var type = handle.GetType();

            if (!handlePools.TryGetValue(type, out var pool)) {
                pool = new Stack<EventHandle>();
                handlePools[type] = pool;
            }

            pool.Push(handle);
        }
        
        [Serializable]
        public abstract class GenericEventHolder {
            public abstract bool Update();
        }
        
        [Serializable]
        public class GenericEventHolder<T> : GenericEventHolder {
            private static readonly ConditionalWeakTable<EventHandle, GenericEventHolder<T>> cache = new();
            public T @event;
            public EventHandle handle;

            public static GenericEventHolder<T> Get(EventHandle handle) {
                if (cache.TryGetValue(handle, out var helper)) 
                    return helper;

                helper = new() { handle = handle };
                cache.Add(handle, helper);
                return helper;
            }

            public override bool Update() {
                return handle.OnUpdate(ref @event);
            }
        }
    }

    public abstract class EventHandle<TModifier> : EventHandle where TModifier : EventModifier {
        [HideInInspector]
        public TModifier modifier;
        private GenericEventHolder genericHelper;

        public override bool Initialize<T>(T @event, EventModifier modifier) {
            if (modifier is not TModifier tmodifier) {
                Debug.LogWarning($"Modifier {typeof(TModifier)} expected, but {modifier.GetType()} received.");
                return false;
            }

            this.modifier = tmodifier;

            var helper = GenericEventHolder<T>.Get(this);
            helper.@event = @event;
            genericHelper = helper;

            return true;
        }

        public override void Enter() => OnEnter();
        public override void Exit() => OnExit();

        public override bool Update() {
            var frame = Time.frameCount;

            if (frameLastUpdated != frame) {
                frameLastUpdated = frame;
                return genericHelper.Update();
            }
            else
                return false;
        }

        public void Continue<T>(in T @event) {
            modifier.Continue(in @event);
        }
    }

    public abstract class EventHandle<TModifier, TEvent> : EventHandle<TModifier> where TModifier : EventModifier {
        protected override Type eventType => typeof(TEvent);
        public TEvent @event;

        public override bool Initialize<T>(T @event, EventModifier modifier) {
            if (modifier is not TModifier tmodifier) {
                Debug.LogWarning($"Modifier {typeof(TModifier)} expected, but {modifier.GetType()} received.");
                return false;
            }

            if (typeof(T) != typeof(TEvent)) {
                Debug.LogWarning($"Event {typeof(TEvent)} expected, but {typeof(T)} received.");
                return false;
            }

            this.modifier = tmodifier;
            this.@event = UnsafeUtility.As<T, TEvent>(ref @event);

            return true;
        }

        public override bool Update() {
            var frame = Time.frameCount;

            if (frameLastUpdated != frame) {
                frameLastUpdated = frame;
                return OnUpdate(ref @event);
            }
            else
                return false;
        }

        protected sealed override bool OnUpdate<T>(ref T @event) => true;
        protected abstract bool OnUpdate(ref TEvent @event);
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
}