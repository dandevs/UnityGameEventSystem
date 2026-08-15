using System;
using UnityEngine;

namespace EventPipelines
{
    /// <summary>Unit of the required silence window.</summary>
    public enum TapUnit { Frames, Time }

    /// <summary>
    /// "Tap to shoot" gate — the inverse of Cooldown: an event continues only if the
    /// gate has NOT been called for at least the window — Frames (Time.frameCount) or
    /// Time (Time.timeAsDouble). EVERY call stamps (not just fires), so a held trigger
    /// (owner re-posting every frame) is absorbed: only the first press after a silent
    /// window continues. A counter modifier, no handles (cross-event state without
    /// payload lives on modifier fields) — the gate decides at Post time, in the
    /// poster's call stack, with the event's OWN payload. Nothing is queued, no
    /// trailing fire; Reset() re-arms (clears the stamp); Update() is a no-op —
    /// frame-guard-immune, works in same-frame loops. Compose with Cooldown for
    /// "tap to shoot, capped fire rate" ([Tap, Cooldown] in that order).
    /// </summary>
    [Serializable]
    public class TapEventModifier : EventModifier
    {
        [Min(0f)] public TapUnit Unit = TapUnit.Frames;

        /// <summary>Used when Unit == Frames — frames of silence required.</summary>
        [Min(1)] public int Frames = 10;

        /// <summary>Used when Unit == Time (Time.timeAsDouble).</summary>
        [Min(0f)] public float Seconds = 0.2f;

        // Same shape as Cooldown, inverted check: stamp on EVERY call (that is what
        // mutes a held trigger), flag instead of sentinel stamps (int.MinValue
        // arithmetic overflows int for real frame counts).
        [NonSerialized] private bool _hasBeenCalled;
        [NonSerialized] private int _lastCallFrame;
        [NonSerialized] private double _lastCallAt;

        public override void Push<T>(in T @event)
        {
            if (_hasBeenCalled)
            {
                var tooRecent = Unit == TapUnit.Frames
                    ? Time.frameCount - _lastCallFrame < Frames
                    : Time.timeAsDouble - _lastCallAt < Seconds;

                if (tooRecent)
                {
                    _lastCallFrame = Time.frameCount;   // this call counts as noise too
                    _lastCallAt = Time.timeAsDouble;
                    return;         // absorbed — called too recently (holding / re-tapping)
                }
            }

            _hasBeenCalled = true;
            _lastCallFrame = Time.frameCount;    // stamp both: Unit can change at runtime
            _lastCallAt = Time.timeAsDouble;
            Continue(in @event);    // fresh press fires at Post time, the event's own payload
        }

        /// <summary>Nothing to advance — the gate decides at Post time.</summary>
        public override void Update() { }

        /// <summary>Re-arms the gate (next post fires regardless of the last stamp).</summary>
        public override void Reset(bool callExit = true) => _hasBeenCalled = false;
    }
}
