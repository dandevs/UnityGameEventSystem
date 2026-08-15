using System;
using UnityEngine;

namespace EventSystem2 {
    /// <summary>
    /// Rx-style throttle: emits the first event immediately (next update), then ignores
    /// events for Interval seconds; if events arrived during the window, emits the LATEST
    /// one when it expires and restarts the window. Retires once a window passes with no events.
    /// </summary>
    [Serializable]
    public class ThrottleEventModifier
        : EventModifierPersistent<ThrottleEventModifier, ThrottleEventModifier.Handle>
    {
        [Min(0f)] public float Interval = 0.5f;

        public class Handle : PersistentHandle<ThrottleEventModifier>
        {
            private float _windowEnd;
            private bool _emittedLeading;
            private bool _pending;

            protected override void OnEnter() {
                _windowEnd = Time.time + modifier.Interval;
                _emittedLeading = false;
                _pending = false;
            }

            protected override void OnPulse<T>(in T @event) => _pending = true;

            protected override bool OnUpdate<T>(ref T @event) {
                if (!_emittedLeading) {                    // leading edge: emit right away
                    _emittedLeading = true;
                    _windowEnd = Time.time + modifier.Interval;
                    Continue(in @event);
                    return false;
                }

                if (Time.time < _windowEnd)
                    return false;

                if (_pending) {                            // trailing-latest at window end
                    _pending = false;
                    _windowEnd = Time.time + modifier.Interval;
                    Continue(in @event);
                    return false;
                }

                return true;                               // a full quiet window — episode over
            }
        }
    }
}
