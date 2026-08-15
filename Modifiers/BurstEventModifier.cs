using System;
using UnityEngine;

namespace EventSystem2 {
    /// <summary>
    /// Continues any event Count times, Interval seconds apart (one per update while the
    /// interval has elapsed), then finishes. Event-agnostic — one handle per incoming event;
    /// overlapping events produce independent bursts (stack policy).
    /// </summary>
    [Serializable]
    public class BurstEventModifier : EventModifier<BurstEventModifier.Handle>
    {
        [Min(1)] public int Count = 3;
        [Min(0f)] public float Interval = 0.1f;

        public class Handle : EventHandle<BurstEventModifier>
        {
            private float _timer;
            private int _remaining;

            protected override void OnEnter()
            {
                _remaining = modifier.Count;
                _timer = 0f; // first shot lands on the first update
            }

            protected override bool OnUpdate<T>(ref T @event)
            {
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
