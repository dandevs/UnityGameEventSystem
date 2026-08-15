using System;
using UnityEngine;

namespace EventPipelines
{
    [Serializable]
    public readonly struct DamageEvent
    {
        public readonly float Amount;
        public readonly GameObject Source;

        public DamageEvent(float amount, GameObject source = null)
        {
            Amount = amount;
            Source = source;
        }
    }
}
