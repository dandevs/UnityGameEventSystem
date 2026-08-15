using System;

namespace EventPipelines
{
    /// <summary>
    /// Handle base for stream (persistent) modifiers: one live handle per episode.
    /// First event rents it via the normal Push path (OnEnter = episode start);
    /// subsequent events are absorbed by <see cref="Pulse{T}"/> which swaps the
    /// stored payload (re-Initialize) and raises <see cref="OnPulse{T}"/>.
    /// Retire from OnUpdate as usual (return true) to end the episode.
    /// </summary>
    public abstract class PersistentHandle<TModifier> : EventHandle<TModifier>
        where TModifier : EventModifier
    {
        public void Pulse<T>(in T @event) {
            // Re-Initialize = overwrite the trampoline holder's payload; does not
            // re-run OnEnter (episode start) nor touch frameLastUpdated.
            // Cannot fail from EventModifierPersistent.Push (modifier type matches by construction).
            if (Initialize(@event, modifier))
                OnPulse(in @event);
        }

        /// <summary>Called for every absorbed event after the first. Reset timers / set pending flags here.</summary>
        protected virtual void OnPulse<T>(in T @event) { }
    }

    /// <summary>
    /// Base for stream modifiers (Debounce, Throttle, coalescing...): keeps ONE live
    /// handle per episode instead of one handle per event. Invariant: handles.Count is 0 or 1.
    /// Cross-event state without a payload (counters, last-accepted time) still belongs
    /// on plain modifier fields; this base is for when you need history + the latest payload.
    /// </summary>
    [Serializable]
    public abstract class EventModifierPersistent<TModifier, THandle> : EventModifier<THandle>
        where TModifier : EventModifierPersistent<TModifier, THandle>
        where THandle : PersistentHandle<TModifier>, new()
    {
        public override void Push<T>(in T @event) {
            if (handles.Count > 0) {
                handles[0].Pulse(in @event);   // fold into the live handle
                return;
            }

            base.Push(in @event);              // first event rents THE handle
        }
    }
}
