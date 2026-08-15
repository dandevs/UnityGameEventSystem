using System;
using System.Collections.Generic;
using UnityEngine;

namespace EventPipelines {
    /// <summary>
    /// Field-based chain node. Plain class — lives inside an EventModified&lt;T&gt; pipeline
    /// and is advanced by the pipeline owner's Update() (which ticks each modifier).
    /// Owner is assigned at registration;
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

        /// <summary>
        /// Aborts this modifier's live handles — pending events die, nothing settles.
        /// Base is a no-op (no handles); EventModifier&lt;THandle&gt; drains them.
        /// </summary>
        /// <param name="callExit">True: OnExit runs per handle (graceful). False: hard abort.</param>
        public virtual void Reset(bool callExit = true) { }

        public void Continue<T>(in T @event) {
            if (Owner == null) {
                // Inspector mid-edit assignment (e.g. type picked on a null row) bypasses Add()/rebind.
                Debug.LogWarning($"({GetType().Name}) has no owner — event consumed. Register via Add() or reload the scene.");
                return;
            }

            Owner.Continue(in @event, this);
        }
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

        /// <summary>
        /// Aborts all live handles (pending events die, nothing settles). The list is
        /// cleared BEFORE any OnExit runs — an OnExit that Posts lands on a fresh list,
        /// so reset covers exactly the handles alive at call time.
        /// </summary>
        public override void Reset(bool callExit = true) {
            if (handles.Count == 0)
                return;

            var drained = handles.ToArray();
            handles.Clear();

            foreach (var handle in drained) {
                if (callExit)
                    handle.Exit();
                EventHandle.ReturnHandle(handle);   // runs handle.Reset() — pool hygiene
            }
        }
    }
}
