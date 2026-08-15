using System;
using UnityEngine;

namespace EventPipelines
{
    /// <summary>Unit of the cooldown duration.</summary>
    public enum CooldownUnit { Frames, Time }

    /// <summary>
    /// Rate gate — an event continues only if enough time has passed since the last
    /// continue: Frames (Time.frameCount) or Time (Time.timeAsDouble). A counter
    /// modifier, no handles (cross-event state without payload lives on modifier
    /// fields) — the gate decides at Post time, in the poster's call stack, with the
    /// event's OWN payload. Leading edge: the first post always fires (stamp starts
    /// "long ago"); while spammed, exactly one continue per cooldown window; releasing
    /// stops dead — nothing is queued, no trailing fire. Reset() re-arms (clears the
    /// stamp). Update() is a no-op — frame-guard-immune, works in same-frame loops.
    /// </summary>
    [Serializable]
    public class CooldownEventModifier : EventModifier
    {
        [Min(0f)] public CooldownUnit Unit = CooldownUnit.Frames;

        /// <summary>Used when Unit == Frames — frames since the last fire.</summary>
        [Min(1)] public int Frames = 6;

        /// <summary>Used when Unit == Time (Time.timeAsDouble).</summary>
        [Min(0f)] public float Seconds = 0.5f;

        // No sentinel arithmetic ("long ago" stamps overflow int for frameCount) —
        // a has-fired flag keeps the first-fire path out of the elapsed math entirely.
        [NonSerialized] private bool _hasFired;
        [NonSerialized] private int _lastFiredFrame;
        [NonSerialized] private double _lastFiredAt;

        public override void Push<T>(in T @event)
        {
            if (_hasFired)
            {
                var onCooldown = Unit == CooldownUnit.Frames
                    ? Time.frameCount - _lastFiredFrame < Frames
                    : Time.timeAsDouble - _lastFiredAt < Seconds;

                if (onCooldown)
                    return;         // absorbed — still on cooldown
            }

            _hasFired = true;
            _lastFiredFrame = Time.frameCount;    // stamp both: Unit can change at runtime
            _lastFiredAt = Time.timeAsDouble;
            Continue(in @event);    // fires at Post time, the event's own payload
        }

        /// <summary>Nothing to advance — the gate decides at Post time.</summary>
        public override void Update() { }

        /// <summary>Re-arms the gate (next post fires regardless of the last stamp).</summary>
        public override void Reset(bool callExit = true) => _hasFired = false;
    }
}
