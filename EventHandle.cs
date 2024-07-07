using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Sirenix.OdinInspector;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace EventSystem2 {
    public abstract class EventHandle {
        private static readonly Dictionary<Type, Stack<EventHandle>> handlePools = new();
        protected int frameLastUpdated = -1;
        protected virtual Type eventType => typeof(object);

        public virtual void Enter() => OnEnter();
        public virtual void Exit() => OnExit();
        public abstract bool Update();

        protected virtual void OnEnter() {}
        protected virtual void OnExit() {}
        protected abstract bool OnUpdate<T>(ref T @event);

        public abstract bool Initialize<T>(T @event, EventModifier modifier);

        public bool IsType<TSource, TTarget>(TSource @in, out TTarget @out, out Func<TTarget, TSource> recast) {
            if (typeof(TTarget).IsAssignableFrom(typeof(TSource))) {
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
            public abstract bool Update(EventHandle handle);
        }
        
        [Serializable]
        public class GenericEventHolder<T> : GenericEventHolder {
            private static readonly ConditionalWeakTable<EventHandle, GenericEventHolder<T>> cache = new();
            public T @event;

            public static GenericEventHolder<T> Get(EventHandle handle) {
                if (cache.TryGetValue(handle, out var helper)) 
                    return helper;

                helper = new();
                cache.Add(handle, helper);
                return helper;
            }

            public override bool Update(EventHandle handle) {
                return handle.OnUpdate(ref @event);
            }
        }
    }
    
    //------------------------------------------------------------------------------------------------------------------

    public abstract class EventHandle<TModifier> : EventHandle where TModifier : EventModifier {
        [HideInInspector]
        public TModifier modifier;

        [SerializeReference, HideReferenceObjectPicker, HideLabel, PropertyOrder(100)]
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

        public sealed override void Enter() => base.OnEnter();
        public sealed override void Exit() => base.OnExit();

        public override bool Update() {
            var frame = Time.frameCount;

            if (frameLastUpdated != frame) {
                frameLastUpdated = frame;
                return genericHelper.Update(this);
            }
            else
                return false;
        }

        protected void Continue<T>(in T @event) {
            modifier.Continue(in @event);
        }
    }

    //------------------------------------------------------------------------------------------------------------------

    public abstract class EventHandle<TModifier, TEvent> : EventHandle where TModifier : EventModifier {
        [HideInInspector]
        public TModifier modifier;

        [PropertyOrder(100)]
        public TEvent @event;

        public override bool Initialize<T>(T @event, EventModifier modifier) {
            if (modifier is not TModifier tmodifier) {
                Debug.LogWarning($"Modifier {typeof(TModifier)} expected, but {modifier.GetType()} received.");
                return false;
            }

            if (!typeof(T).IsAssignableFrom(typeof(TEvent))) {
                Debug.LogWarning($"Event {typeof(TEvent)} expected, but {typeof(T)} received.");
                return false;
            }

            this.modifier = tmodifier;
            this.@event = UnsafeUtility.As<T, TEvent>(ref @event);

            return true;
        }

        public sealed override void Enter() => base.Enter();
        public sealed override void Exit() => base.Exit();

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

        protected void Continue(in TEvent @event) {
            modifier.Continue(in @event);
        }
    }
}