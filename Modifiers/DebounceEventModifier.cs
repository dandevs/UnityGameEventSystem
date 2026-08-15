using System;
using UnityEngine;

namespace EventPipelines
{
    /// <summary>
    /// Emits the LAST event after QuietPeriod seconds of silence; every new event
    /// during the wait resets the timer and replaces the stored payload.
    /// </summary>
    [Serializable]
    public class DebounceEventModifier
        : EventModifierPersistent<DebounceEventModifier, DebounceEventModifier.Handle>
    {
        [Min(0f)] public float QuietPeriod = 0.3f;

        public class Handle : PersistentHandle<DebounceEventModifier>
        {
            private float _lastPulse;

            protected override void OnEnter() => _lastPulse = Time.time;
            protected override void OnPulse<T>(in T @event) => _lastPulse = Time.time;

            protected override bool OnUpdate<T>(ref T @event)
            {
                if (Time.time - _lastPulse < modifier.QuietPeriod)
                    return false;

                Continue(in @event);
                return true;
            }
        }
    }
}
