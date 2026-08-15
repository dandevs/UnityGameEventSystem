using System;
using System.Collections.Generic;
using UnityEngine;

namespace EventSystem2 {
    /// <summary>
    /// Field-based chain node. Plain class — lives inside an EventModified&lt;T&gt; pipeline
    /// and is ticked by the pipeline's Tick(). Owner is assigned at registration;
    /// Continue walks the owner's pipeline (next modifier, or Settle at the end).
    /// </summary>
    [Serializable]
    public abstract class EventModifier {
        /// <summary>Pipeline owner — assigned by EventModified&lt;T&gt; on registration.</summary>
        internal EventModified Owner { get; set; }

        public abstract void Push<T>(in T @event);

        /// <summary>Advances this modifier's live handles. Implemented by EventModifier&lt;THandle&gt;.</summary>
        public abstract void Tick();

        /// <summary>Live handle count — editor/debug aid (live state visualization).</summary>
        public virtual int LiveHandleCount => 0;

        public void Continue<T>(in T @event) => Owner.Continue(in @event, this);
    }

    //------------------------------------------------------------------------------------------------------------------

    [Serializable]
    public abstract class EventModifier<THandle> : EventModifier where THandle : EventHandle, new() {
        /// <summary>Live handles. Runtime state only — never serialized.</summary>
        [NonSerialized] public List<THandle> handles = new();

        public override int LiveHandleCount => handles.Count;

        public override void Push<T>(in T @event) {
            var handle = EventHandle.GetHandle<THandle>();

            if (handle.Initialize(@event, this)) {
                handle.Enter();
                handles.Add(handle);
            }
            else {
                EventHandle.ReturnHandle(handle);
                Debug.LogWarning($"Failed to initialize handle for event {typeof(T)}.");
            }
        }

        /// <summary>Advances all live handles; retires (Exit + pool) finished ones. Called by the pipeline owner.</summary>
        public override void Tick() {
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
