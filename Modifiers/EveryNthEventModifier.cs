using System;
using UnityEngine;

namespace EventPipelines {
    /// <summary>
    /// Recurring event-count gate. Absorbs events until N have arrived (counting the
    /// first), then Continues the Nth event's payload exactly once and retires — the
    /// next event opens a fresh window (recurring via episodes, MinHold-style; Reset()
    /// ends any live window). Fires at most once per Update; a same-frame burst beyond
    /// a full window still fires just once with the LATEST payload — the surplus is
    /// not carried over (Pattern C parks one payload, it is not a queue).
    /// </summary>
    [Serializable]
    public class EveryNthEventModifier
        : EventModifierPersistent<EveryNthEventModifier, EveryNthEventModifier.Handle>
    {
        [Min(1)] public int N = 3;

        public class Handle : PersistentHandle<EveryNthEventModifier>
        {
            private int _seen;

            protected override void OnEnter() => _seen = 1;   // the first event counts

            protected override void OnPulse<T>(in T @event) => _seen++;

            protected override bool OnUpdate<T>(ref T @event) {
                if (_seen < modifier.N)
                    return false;

                Continue(in @event);   // the Nth event's payload (latest parked)
                return true;           // window fired — episode over; next event re-arms
            }
        }
    }
}
