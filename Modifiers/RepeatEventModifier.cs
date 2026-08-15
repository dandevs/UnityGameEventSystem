using System;
using UnityEngine;

namespace EventPipelines {
    /// <summary>
    /// Continues any event Count times, Interval seconds apart, then finishes.
    /// Interval &lt;= 0 emits ALL Count Continues immediately, in one Update (burst
    /// mode). Event-agnostic — uses the trampoline handle, works for every event type.
    /// </summary>
    [Serializable]
    public class RepeatEventModifier : EventModifier<RepeatEventModifier.Handle>
    {
        [Min(1)] public int Count = 3;
        [Min(0f)] public float Interval = 0.5f;

        public class Handle : EventHandle<RepeatEventModifier>
        {
            private float _timer;
            private int _remaining;

            protected override void OnEnter()
            {
                _remaining = modifier.Count;
                _timer = 0f; // first repeat lands on the first update
            }

            protected override bool OnUpdate<T>(ref T @event)
            {
                if (modifier.Interval <= 0f)
                {
                    // Burst mode — no spacing to wait for: emit everything now.
                    for (var i = 0; i < _remaining; i++)
                        Continue(in @event);

                    return true;
                }

                _timer -= Time.deltaTime;

                if (_timer > 0f)
                    return false;

                Continue(in @event);

                _remaining--;
                _timer = modifier.Interval;

                return _remaining <= 0;
            }
        }
    }
}
