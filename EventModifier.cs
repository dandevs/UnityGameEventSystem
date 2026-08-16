using System;
using System.Collections.Generic;
using UnityEngine;

namespace EventPipelines
{
    /// <summary>
    /// Field-based chain node. Plain class — lives inside an EventModified&lt;T&gt; pipeline
    /// and is advanced by the pipeline owner's Update() (which updates each modifier).
    /// Owner is assigned at registration;
    /// Continue walks the owner's pipeline (next modifier, or Settle at the end).
    /// </summary>
    [Serializable]
    public abstract class EventModifier
    {
        /// <summary>Pipeline owner — assigned by EventModified&lt;T&gt; on registration.</summary>
        internal EventModified Owner { get; set; }

        public abstract void Push<T>(in T @event);

        /// <summary>Advances this modifier's live handles. Implemented by EventModifier&lt;THandle&gt;.</summary>
        public abstract void Update();

        /// <summary>Live handle count — editor/debug aid (live state visualization).</summary>
        public virtual int LiveHandleCount => 0;

        /// <summary>Something in flight — NOT active = safe window to detach (see EventModified.Remove).</summary>
        public bool Active => LiveHandleCount > 0;

        /// <summary>
        /// Aborts this modifier's live state — pending events die, nothing settles.
        /// Non-virtual: drains live handles (ResetHandles), then runs OnReset().
        /// </summary>
        /// <param name="callExit">True: OnExit runs per handle (graceful). False: hard abort — OnReset still runs.</param>
        public void Reset(bool callExit = true)
        {
            ResetHandles(callExit);
            OnReset();
        }

        /// <summary>Handle drain — implemented by EventModifier&lt;THandle&gt;; base has no handles.</summary>
        internal virtual void ResetHandles(bool callExit) { }

        /// <summary>
        /// State-hygiene hook — ALWAYS runs, including hard aborts (callExit: false);
        /// callExit only governs handle OnExit calls. Re-arm gates/counters here.
        /// </summary>
        protected virtual void OnReset() { }

        public void Continue<T>(in T @event)
        {
            if (Owner == null)
            {
                // Inspector mid-edit assignment (e.g. type picked on a null row) bypasses Add()/rebind.
                Debug.LogWarning($"({GetType().Name}) has no owner — event consumed. Register via Add() or reload the scene.");
                return;
            }

            Owner.Continue(in @event, this);
        }
    }

    //------------------------------------------------------------------------------------------------------------------

    [Serializable]
    public abstract class EventModifier<THandle> : EventModifier where THandle : EventHandle, new()
    {
        /// <summary>Live handles. Runtime state only — never serialized.</summary>
        [NonSerialized] public List<THandle> handles = new();

        public override int LiveHandleCount => handles.Count;

        public override void Push<T>(in T @event)
        {
            var handle = EventHandle.GetHandle<THandle>();

            if (handle.Initialize(@event, this))
            {
                handle.Enter();
                handles.Add(handle);
            }
            else
            {
                EventHandle.ReturnHandle(handle);
                Debug.LogWarning($"Failed to initialize handle for event {typeof(T)}.");
            }
        }

        /// <summary>Advances all live handles; retires (Exit + pool) finished ones. Called by the pipeline owner.</summary>
        public override void Update()
        {
            for (var i = 0; i < handles.Count; i++)
            {
                var handle = handles[i];

                if (handle.Update())
                {
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
        internal override void ResetHandles(bool callExit)
        {
            if (handles.Count == 0)
                return;

            var drained = handles.ToArray();
            handles.Clear();

            foreach (var handle in drained)
            {
                if (callExit)
                    handle.Exit();
                EventHandle.ReturnHandle(handle);   // runs handle.Reset() — pool hygiene
            }
        }
    }
}
