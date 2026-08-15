using System;
using System.Collections.Generic;
using UltEvents;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace EventPipelines {
    /// <summary>
    /// Non-generic pipeline owner: registration, chain walking, ticking.
    /// Continue resolves the next modifier in the pipeline; the last Continue settles.
    /// </summary>
    [Serializable]
    public abstract class EventModified : ISerializationCallbackReceiver {
        [SerializeReference] protected List<EventModifier> _pipeline = new();

        /// <summary>Read-only view of the pipeline (editor/debug aid).</summary>
        public IReadOnlyList<EventModifier> Pipeline => _pipeline;

        /// <summary>Last settled value, boxed. Editor/debug aid — boxes per call.</summary>
        public abstract object BoxedLatest { get; }

        internal abstract void Continue<T>(in T @event, EventModifier from);

        /// <summary>
        /// Advances every modifier's handles. Owners call this once per frame, per field.
        /// Null pipeline elements (inspector "+" inserts) are skipped.
        /// </summary>
        public void Tick() {
            for (var i = 0; i < _pipeline.Count; i++)
                _pipeline[i]?.Tick();
        }

        /// <summary>
        /// Aborts every modifier's live handles — pending events die, nothing settles.
        /// Call when the field's context dies (holster, disable, despawn). Null pipeline
        /// elements are skipped; callExit=false skips every handle's OnExit (hard abort).
        /// </summary>
        public void Reset(bool callExit = true) {
            for (var i = 0; i < _pipeline.Count; i++)
                _pipeline[i]?.Reset(callExit);
        }

        /// <summary>Deserialization bypasses Add() — rebind Owner here or the first Continue NREs.</summary>
        public void OnAfterDeserialize() {
            foreach (var modifier in _pipeline)
                if (modifier != null)
                    modifier.Owner = this;
        }

        public void OnBeforeSerialize() { }
    }

    /// <summary>
    /// A reactive field with a modifier pipeline. Write (.Value / Post) enters the pipeline;
    /// the settled result is cached and broadcast — Settle is the single writer of Value
    /// and never re-Posts (no retrigger, by structure).
    /// </summary>
    [Serializable]
    public class EventModified<T> : EventModified {
        /// <summary>Designer-wireable terminal. Invoked on every settle.</summary>
        public UltEvent<T> OnSettle = new();

        /// <summary>Code terminal. Invoked on every settle.</summary>
        public event Action<T> Settled;

        private T _latest;
        private int _dispatchDepth;

        /// <summary>Write = enter the pipeline. Read = last SETTLED value (not the posted one).</summary>
        public T Value { get => _latest; set => Post(value); }

        /// <summary>Explicit read alias — same as reading .Value.</summary>
        public T Latest => _latest;

        public override object BoxedLatest => _latest;

        public EventModified() { }

        public EventModified(params EventModifier[] modifiers) {
            foreach (var modifier in modifiers)
                Add(modifier);
        }

        public EventModified<T> Add(EventModifier modifier) {
            if (modifier == null)
                throw new ArgumentNullException(nameof(modifier),
                    "Explicit nulls are a code bug — inspector-inserted nulls are tolerated, Add(null) is not.");
            modifier.Owner = this;
            _pipeline.Add(modifier);
            return this;
        }

        /// <summary>First non-null index at or after <paramref name="from"/>; -1 if none.</summary>
        private int NextLiveIndex(int from) {
            for (var i = from; i < _pipeline.Count; i++)
                if (_pipeline[i] != null)
                    return i;
            return -1;
        }

        /// <summary>Enters the pipeline. Empty pipeline settles immediately (same call stack).</summary>
        public void Post(T value) {
            if (_dispatchDepth > 0)
                throw new InvalidOperationException(
                    "EventModified<T>.Post called re-entrantly during settle — writing Value from Settled/OnSettle handlers is not allowed.");

            _dispatchDepth++;
            try {
                var start = NextLiveIndex(0);

                if (start != -1)
                    _pipeline[start].Push(value);
                else
                    Settle(value);   // empty (or all-null) pipeline settles immediately
            }
            finally { _dispatchDepth--; }
        }

        internal override void Continue<T2>(in T2 @event, EventModifier from) {
            var index = _pipeline.IndexOf(from);

            if (index == -1) {
                Debug.LogWarning($"({from.GetType().Name}) Modifier removed from pipeline; settling directly.");
                var orphan = @event;
                Settle(UnsafeUtility.As<T2, T>(ref orphan));
                return;
            }

            var next = NextLiveIndex(index + 1);

            if (next != -1) {
                _pipeline[next].Push(in @event);   // null elements are skipped, not walked into
            }
            else {
                var e = @event;
                Settle(UnsafeUtility.As<T2, T>(ref e));  // T2 == T by construction (Post only accepts T)
            }
        }

        private void Settle(T result) {
            _latest = result;
            OnSettle?.Invoke(result);
            Settled?.Invoke(result);
        }
    }
}
