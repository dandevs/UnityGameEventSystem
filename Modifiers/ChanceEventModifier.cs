using System;
using UnityEngine;

namespace EventPipelines {
    /// <summary>
    /// Per-event probability gate: each incoming event passes with Probability chance,
    /// otherwise it is consumed (it never settles). Independent roll per event —
    /// overlapping events don't correlate. Endpoints are exact by construction:
    /// Probability 1 always passes, 0 always consumes.
    /// </summary>
    [Serializable]
    public class ChanceEventModifier : EventModifier<ChanceEventModifier.Handle>
    {
        [Range(0f, 1f)] public float Probability = 0.5f;

        public class Handle : EventHandle<ChanceEventModifier>
        {
            protected override bool OnUpdate<T>(ref T @event)
            {
                var pass = modifier.Probability >= 1f
                           || (modifier.Probability > 0f
                               && UnityEngine.Random.value < modifier.Probability);

                if (!pass)
                    return true;        // consume — no Continue

                Continue(in @event);
                return true;
            }
        }
    }
}
