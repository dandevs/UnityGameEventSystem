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

        /// <summary>Advances every modifier's handles. Owners call this once per frame, per field.</summary>
        public void Tick() {
            for (var i = 0; i < _pipeline.Count; i++)
                _pipeline[i].Tick();
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
            modifier.Owner = this;
            _pipeline.Add(modifier);
            return this;
        }

        /// <summary>Enters the pipeline. Empty pipeline settles immediately (same call stack).</summary>
        public void Post(T value) {
            if (_dispatchDepth > 0)
                throw new InvalidOperationException(
                    "EventModified<T>.Post called re-entrantly during settle — writing Value from Settled/OnSettle handlers is not allowed.");

            _dispatchDepth++;
            try {
                if (_pipeline.Count > 0)
                    _pipeline[0].Push(value);
                else
                    Settle(value);
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

            if (index + 1 < _pipeline.Count) {
                _pipeline[index + 1].Push(in @event);
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
