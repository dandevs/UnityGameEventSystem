using System;
using UnityEngine;

namespace EventPipelines
{
    /// <summary>
    /// Holds any event for N seconds, then passes it on unchanged.
    /// Event-agnostic — uses the trampoline handle, works for every event type.
    /// </summary>
    [Serializable]
    public class DelayEventModifier : EventModifier<DelayEventModifier.Handle>
    {
        [Min(0f)] public float Seconds = 0.5f;

        public class Handle : EventHandle<DelayEventModifier>
        {
            private float _timeLeft;

            protected override void OnEnter() => _timeLeft = modifier.Seconds;

            protected override bool OnUpdate<T>(ref T @event)
            {
                _timeLeft -= Time.deltaTime;

                if (_timeLeft > 0f)
                    return false;

                Continue(in @event);
                return true;
            }
        }
    }
}
