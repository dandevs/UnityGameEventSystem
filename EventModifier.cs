using System.Collections.Generic;
using Drawing;
using Sirenix.OdinInspector;
using UnityEngine;

namespace EventSystem2 {
    public abstract class EventModifier : MonoBehaviourGizmos {
        protected virtual int defaultOrder => 0;

        private IEventListenerModifierSystem _system;
        public IEventListenerModifierSystem system {
            get {
                if (_system == null)
                    _system = GetComponentInParent<IEventListenerModifierSystem>();

                return _system;
            }
            
            set => _system = value;
        }
        
        [SerializeField, HideInInspector]
        private int _order = int.MinValue;

        [ShowInInspector, PropertyOrder(999)]
        public int order {
            get => _order == int.MinValue ? defaultOrder : _order;
            set => _order = value;
        }

        public abstract void Push<T>(in T @event);

        public void Continue<T>(in T @event) {
            system.Continue(in @event, this);
        }
    }

    //------------------------------------------------------------------------------------------------------------------

    public abstract class EventModifier<THandle> : EventModifier where THandle : EventHandle, new() {
        [PropertyOrder(1000)]
        public List<THandle> handles = new();

        public override void Push<T>(in T @event) {
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
}