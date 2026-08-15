using System;
using UnityEngine;

namespace EventSystem2 {
    /// <summary>
    /// Splits a DamageEvent into TickCount ticks, each carrying Amount / TickCount,
    /// spread evenly over Duration. Typed handle — reads inside the event.
    /// </summary>
    [Serializable]
    public class DamageOverTimeModifier : EventModifier<DamageOverTimeModifier.Handle>
    {
        [Min(1)] public int TickCount = 5;
        [Min(0f)] public float Duration = 5f;

        public class Handle : EventHandle<DamageOverTimeModifier, DamageEvent>
        {
            private float _timer;
            private int _remaining;

            protected override void OnEnter()
            {
                _remaining = modifier.TickCount;
                _timer = 0f; // first tick lands on the first update
            }

            protected override bool OnUpdate(ref DamageEvent @event)
            {
                _timer -= Time.deltaTime;

                if (_timer > 0f)
                    return false;

                var amountPerTick = @event.Amount / modifier.TickCount;
                Continue(new DamageEvent(amountPerTick, @event.Source));

                _remaining--;
                _timer = modifier.Duration / modifier.TickCount;

                return _remaining <= 0;
            }
        }
    }
}
