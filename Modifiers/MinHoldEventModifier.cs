using System;
using UnityEngine;

namespace EventPipelines {
    /// <summary>
    /// Hold-to-qualify gate. The owner is expected to re-post the field every frame
    /// while "held" (e.g. Input.GetKey in Update) — each pulse stamps its frame, and
    /// the episode survives only while pulses keep arriving on new frames. Once the
    /// hold has lasted MinimumSeconds (measured in Time.timeAsDouble), the LATEST
    /// payload continues exactly once. Releasing early consumes the event — it never
    /// settles. Post-then-update within the same frame is the expected owner order.
    /// </summary>
    [Serializable]
    public class MinHoldEventModifier
        : EventModifierPersistent<MinHoldEventModifier, MinHoldEventModifier.Handle>
    {
        [Min(0f)] public float MinimumSeconds = 0.5f;

        public class Handle : PersistentHandle<MinHoldEventModifier>
        {
            private int _lastFrame;
            private double _heldSince;

            protected override void OnEnter() {
                _lastFrame = Time.frameCount;
                _heldSince = Time.timeAsDouble;
            }

            protected override void OnPulse<T>(in T @event) => _lastFrame = Time.frameCount;

            protected override bool OnUpdate<T>(ref T @event) {
                if (Time.frameCount != _lastFrame)
                    return true;        // a frame passed with no pulse — released, consume

                if (Time.timeAsDouble - _heldSince < modifier.MinimumSeconds)
                    return false;       // still held, minimum not reached yet

                Continue(in @event);    // fired once — episode over
                return true;
            }
        }
    }
}
