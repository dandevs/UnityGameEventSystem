using System;
using UnityEngine;

namespace EventPipelines
{
    /// <summary>
    /// Recurring event-count gate — a counter modifier, no handles (cross-event state
    /// without payload lives on modifier fields). Every Nth Post Continues immediately,
    /// at Post time, in the poster's call stack, with its OWN payload — same-frame
    /// loops work: 100 posts with N=5 fire 20 times (payloads 5, 10, ... 100).
    /// N=1 is a synchronous pass-through. Reset() re-arms by clearing the count.
    /// Update() is a no-op — the gate decides at Post time, never at Update.
    /// </summary>
    [Serializable]
    public class EveryNthEventModifier : EventModifier
    {
        [Min(1)] public int N = 3;

        [NonSerialized] private int _seen;

        public override void Push<T>(in T @event)
        {
            _seen++;

            if (_seen < N)
                return;             // absorbed — the gate is still counting

            _seen = 0;
            Continue(in @event);    // the Nth event's own payload, at Post time
        }

        /// <summary>Nothing to advance — the gate decides at Post time.</summary>
        public override void Update() { }

        /// <summary>Re-arms the gate (clears the count toward N).</summary>
        public override void Reset(bool callExit = true) => _seen = 0;
    }
}
