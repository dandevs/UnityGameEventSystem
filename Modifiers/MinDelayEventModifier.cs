using System;
using UnityEngine;

namespace EventPipelines
{
    /// <summary>Unit of the minimum gate duration.</summary>
    public enum MinDelayUnit { Frames, Time }

    /// <summary>
    /// Persistent hold-to-qualify gate with a selectable unit — MinHold generalized.
    /// The owner is expected to re-post the field every frame while "held" (e.g.
    /// Input.GetKey in Update); each pulse stamps its frame, and the episode survives
    /// only while pulses keep arriving on new frames. Once the hold has lasted the
    /// minimum duration — Frames (Time.frameCount) or Time (Time.timeAsDouble) — the
    /// LATEST payload continues exactly once. Releasing early consumes the event — it
    /// never settles. Unlike Delay (Pattern A, every event delayed independently),
    /// this is a persistent gate: one episode, pulses fold in, latest payload wins.
    /// </summary>
    [Serializable]
    public class MinDelayEventModifier
        : EventModifierPersistent<MinDelayEventModifier, MinDelayEventModifier.Handle>
    {
        [Min(0f)] public MinDelayUnit Unit = MinDelayUnit.Time;

        /// <summary>Used when Unit == Frames.</summary>
        [Min(1)] public int Frames = 3;

        /// <summary>Used when Unit == Time (Time.timeAsDouble, like MinHold).</summary>
        [Min(0f)] public float Seconds = 0.5f;

        public class Handle : PersistentHandle<MinDelayEventModifier>
        {
            private int _lastFrame;
            private int _startFrame;
            private double _heldSince;

            protected override void OnEnter()
            {
                _lastFrame = Time.frameCount;
                _startFrame = Time.frameCount;
                _heldSince = Time.timeAsDouble;
            }

            protected override void OnPulse<T>(in T @event) => _lastFrame = Time.frameCount;

            protected override bool OnUpdate<T>(ref T @event)
            {
                if (Time.frameCount != _lastFrame)
                    return true;        // a frame passed with no pulse — released, consume

                if (modifier.Unit == MinDelayUnit.Frames)
                {
                    if (Time.frameCount - _startFrame < modifier.Frames)
                        return false;   // still held, frame minimum not reached yet
                }
                else
                {
                    if (Time.timeAsDouble - _heldSince < modifier.Seconds)
                        return false;   // still held, time minimum not reached yet
                }

                Continue(in @event);    // fired once — episode over
                return true;
            }
        }
    }
}
