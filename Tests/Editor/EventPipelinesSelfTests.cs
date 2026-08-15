#if UNITY_EDITOR
using System;
using EventPipelines;
using UltEvents;
using UnityEditor;
using UnityEngine;

/// <summary>
/// EditMode self-tests for the field-based pipeline core. Runs automatically on domain
/// reload and via Tools/EventPipelines/Run Self Tests. Proper Unity Test Framework needs
/// asmdefs (this project compiles to Assembly-CSharp), so these are plain assertions.
/// Lives in the plugin's Tests/Editor folder (Assembly-CSharp-Editor-firstpass) — fine
/// since the builtin modifiers are plugin types too. Do NOT reference game-side types
/// (Gun, Enemy) here: Editor-firstpass cannot see Assembly-CSharp.
/// Timing-heavy behavior (multi-frame bursts, real delays, hold-release) is NOT covered
/// here — these tests only pin the semantics reachable within a single frame/update.
/// </summary>
public static class EventPipelinesSelfTests
{
    [InitializeOnLoadMethod]
    private static void RunOnLoad() => RunAll(false);

    [MenuItem("Tools/EventPipelines/Run Self Tests")]
    private static void MenuRun() => RunAll(true);

    public static string RunAll(bool verbose)
    {
        var pass = 0;
        var fail = 0;

        void Check(string name, bool ok)
        {
            if (ok) { pass++; return; }
            fail++;
            Debug.LogError($"[EventPipelines SelfTest FAIL] {name}");
        }

        // 1. Empty pipeline: post settles immediately, same call stack.
        var empty = new EventModified<int>();
        var got = -1;
        empty.Settled += v => got = v;
        empty.Post(42);
        Check("empty-pipeline-settles", got == 42 && empty.Value == 42);

        // 2. Value write posts; read returns last SETTLED value.
        var echo = new EventModified<int>();
        echo.Post(7);
        Check("write-then-read-settled", echo.Value == 7);

        // 3. Zero-delay chain: one Update walks the full pipeline in order.
        var chain = new EventModified<int>(
            new DelayEventModifier { Seconds = 0f },
            new DelayEventModifier { Seconds = 0f });
        var chainGot = -1;
        chain.Settled += v => chainGot = v;
        chain.Post(9);
        Check("pre-update-not-settled", chainGot == -1);
        chain.Update();
        Check("chain-walks-and-settles", chainGot == 9 && chain.Value == 9);

        // 4. Depth guard: re-entrant Post from a Settled handler throws.
        var guarded = new EventModified<int>();
        guarded.Settled += v => guarded.Post(v + 1);
        var threw = false;
        try { guarded.Post(1); }
        catch (InvalidOperationException) { threw = true; }
        Check("reentrant-post-throws", threw);

        // 5. Debounce folding: rapid posts collapse into ONE live handle, no settle in window.
        var debounce = new DebounceEventModifier { QuietPeriod = 5f };
        var debField = new EventModified<int>(debounce);
        var settles = 0;
        debField.Settled += _ => settles++;
        debField.Post(1);
        debField.Post(2);
        debField.Post(3);
        Check("debounce-single-handle", debounce.handles.Count == 1);
        debField.Update();
        Check("debounce-no-settle-in-window", settles == 0);
        Check("debounce-value-unchanged", debField.Value.Equals(default(int)));

        // 6. Repeat: one Update emits exactly the first shot; handle stays alive.
        var repeat = new RepeatEventModifier { Count = 3, Interval = 5f };
        var repeatField = new EventModified<int>(repeat);
        var shots = 0;
        repeatField.Settled += _ => shots++;
        repeatField.Post(1);
        repeatField.Update();
        Check("repeat-first-shot-only", shots == 1 && repeat.handles.Count == 1);

        // 7. UltEvent terminal + code terminal both fire on settle.
        var ult = new EventModified<int>();
        var ultCalls = 0;
        UltEvent<int>.AddDynamicCall(ref ult.OnSettle, _ => ultCalls++);
        ult.Settled += _ => ultCalls++;
        ult.Post(5);
        Check("both-terminals-fire", ultCalls == 2);

        // 8. Chance endpoints are exact by construction (no RNG at 0 or 1).
        var sure = new EventModified<int>(new ChanceEventModifier { Probability = 1f });
        var sureGot = -1;
        sure.Settled += v => sureGot = v;
        sure.Post(5);
        sure.Update();
        Check("chance-p1-always-passes", sureGot == 5 && sure.Value == 5);

        var never = new EventModified<int>(new ChanceEventModifier { Probability = 0f });
        var neverGot = -1;
        never.Settled += v => neverGot = v;
        never.Post(5);
        never.Update();
        Check("chance-p0-always-consumes", neverGot == -1 && never.Value.Equals(default(int)));

        // 9. MinHold while held below minimum: episode alive, nothing settles
        //    (same-frame pulse + update; release/threshold paths need frame advance).
        var minHold = new MinHoldEventModifier { MinimumSeconds = 5f };
        var holdField = new EventModified<int>(minHold);
        var holdSettles = 0;
        holdField.Settled += _ => holdSettles++;
        holdField.Post(1);
        holdField.Update();
        Check("minhold-holding-no-settle", holdSettles == 0 && minHold.handles.Count == 1);

        // 10. Null pipeline elements (inspector "+" inserts) are skipped at every walk
        //     point — [null, delay, null] posts into the delay and updates without NRE.
        var withNulls = new NullInjectField(new DelayEventModifier { Seconds = 5f });
        withNulls.InjectNull(0);                       // before the delay
        withNulls.InjectNull(int.MaxValue);            // after the delay
        var nullWalkSettled = false;
        withNulls.Settled += _ => nullWalkSettled = true;
        withNulls.Post(3);
        withNulls.Update();
        Check("nulls-skipped-by-walk", !nullWalkSettled && withNulls.DelayHandles == 1);

        // 11. All-null pipeline behaves like an empty one — settles immediately.
        var allNull = new NullInjectField();
        allNull.InjectNull(int.MaxValue);
        var allNullGot = -1;
        allNull.Settled += v => allNullGot = v;
        allNull.Post(5);
        Check("all-null-pipeline-settles", allNullGot == 5);

        // 12. Add(null) is explicit API misuse — throws instead of silently tolerating.
        var addNullThrew = false;
        try { allNull.Add(null); }
        catch (ArgumentNullException) { addNullThrew = true; }
        Check("add-null-throws", addNullThrew);

        // 13. Reset aborts pending handles — nothing settles afterwards.
        var resetDelay = new DelayEventModifier { Seconds = 5f };
        var resetField = new EventModified<int>(resetDelay);
        var resetSettled = false;
        resetField.Settled += _ => resetSettled = true;
        resetField.Post(1);
        resetField.Update();
        resetField.Reset();
        resetField.Update();
        Check("reset-kills-pending-handles", !resetSettled && resetDelay.handles.Count == 0);

        // 14. Reset(callExit): graceful runs OnExit per handle; hard abort skips it.
        SpyEventModifier.Enters = SpyEventModifier.Exits = 0;
        var spyField = new EventModified<int>(new SpyEventModifier());
        spyField.Post(1);
        spyField.Reset(callExit: false);
        var hardExits = SpyEventModifier.Exits;
        spyField.Post(2);
        spyField.Reset();
        Check("reset-callexit-gates-onexit",
            hardExits == 0 && SpyEventModifier.Exits == 1 && SpyEventModifier.Enters == 2);

        // 15. Field Reset walks past nulls (inspector-inserted), no NRE.
        var nullReset = new NullInjectField(new DelayEventModifier { Seconds = 5f });
        nullReset.InjectNull(0);
        nullReset.Post(1);
        nullReset.Update();
        nullReset.Reset();
        Check("reset-skips-nulls", nullReset.DelayHandles == 0);

        // 16. Pooled handles stay healthy across resets — a post-reset Post behaves normally.
        var reuseDelay = new DelayEventModifier { Seconds = 0f };
        var reuseField = new EventModified<int>(reuseDelay);
        reuseField.Post(1);
        reuseField.Reset();
        var reuseGot = -1;
        reuseField.Settled += v => reuseGot = v;
        reuseField.Post(9);
        reuseField.Update();
        Check("post-after-reset-works", reuseGot == 9 && reuseField.Value == 9);

        // 17. EveryNth: absorbs below N — posts count toward the gate, nothing settles.
        var everyNth = new EveryNthEventModifier { N = 3 };
        var nthField = new EventModified<int>(everyNth);
        var nthSettles = new System.Collections.Generic.List<int>();
        nthField.Settled += v => nthSettles.Add(v);
        nthField.Post(1);
        nthField.Post(2);
        Check("everynth-absorbs-below-n", nthSettles.Count == 0);

        // 18. EveryNth fires the Nth event's payload at POST time — no Update needed.
        nthField.Post(3);
        Check("everynth-fires-on-nth", nthSettles.Count == 1 && nthSettles[0] == 3);

        // 19. EveryNth same-frame burst loop: every Nth fires, each with its own payload.
        var loopNth = new EveryNthEventModifier { N = 5 };
        var loopField = new EventModified<int>(loopNth);
        var loopSettles = new System.Collections.Generic.List<int>();
        loopField.Settled += v => loopSettles.Add(v);
        for (var i = 1; i <= 10; i++)
            loopField.Post(i);
        Check("everynth-same-frame-loop",
            loopSettles.Count == 2 && loopSettles[0] == 5 && loopSettles[1] == 10);

        // 20. EveryNth N=1 is a synchronous pass-through.
        var everyOne = new EveryNthEventModifier { N = 1 };
        var oneField = new EventModified<int>(everyOne);
        var oneGot = -1;
        oneField.Settled += v => oneGot = v;
        oneField.Post(7);
        Check("everynth-one-passes-through", oneGot == 7);

        // 21. EveryNth Reset re-arms: three of N=5, Reset, then the 5th new post fires.
        var rearmNth = new EveryNthEventModifier { N = 5 };
        var rearmField = new EventModified<int>(rearmNth);
        var rearmGot = -1;
        rearmField.Settled += v => rearmGot = v;
        rearmField.Post(1);
        rearmField.Post(2);
        rearmField.Post(3);
        rearmField.Reset();
        for (var i = 4; i <= 8; i++)
            rearmField.Post(i);
        Check("everynth-reset-rearms", rearmGot == 8);

        // 22. MinDelay time unit: held below minimum — episode alive, nothing settles
        //    (same-frame pulse + update; release/threshold paths need frame advance).
        var minDelayT = new MinDelayEventModifier { Unit = MinDelayUnit.Time, Seconds = 5f };
        var delayFieldT = new EventModified<int>(minDelayT);
        var delaySettlesT = 0;
        delayFieldT.Settled += _ => delaySettlesT++;
        delayFieldT.Post(1);
        delayFieldT.Update();
        Check("mindelay-time-holding-no-settle", delaySettlesT == 0 && minDelayT.handles.Count == 1);

        // 23. MinDelay frames unit: held below minimum — episode alive, nothing settles.
        var minDelayF = new MinDelayEventModifier { Unit = MinDelayUnit.Frames, Frames = 5 };
        var delayFieldF = new EventModified<int>(minDelayF);
        var delaySettlesF = 0;
        delayFieldF.Settled += _ => delaySettlesF++;
        delayFieldF.Post(1);
        delayFieldF.Update();
        Check("mindelay-frames-holding-no-settle", delaySettlesF == 0 && minDelayF.handles.Count == 1);

        // 24. Repeat burst mode: Interval <= 0 emits ALL Count in one Update, then retires.
        var burstRepeat = new RepeatEventModifier { Count = 4, Interval = 0f };
        var burstField = new EventModified<int>(burstRepeat);
        var burstShots = 0;
        burstField.Settled += _ => burstShots++;
        burstField.Post(1);
        burstField.Update();
        Check("repeat-zero-interval-burst", burstShots == 4 && burstRepeat.handles.Count == 0);

        var summary = $"EventPipelines self-tests: {pass} passed, {fail} failed";
        if (verbose || fail > 0)
            Debug.Log($"<color={(fail > 0 ? "red" : "green")}>{summary}</color>");
        return summary;
    }

    /// <summary>
    /// Test-only subclass: protected _pipeline access simulates inspector-inserted nulls
    /// (which bypass Add() exactly like deserialization does).
    /// </summary>
    private class NullInjectField : EventModified<int>
    {
        private readonly DelayEventModifier _delay;

        public NullInjectField() { }

        public NullInjectField(DelayEventModifier delay) : base(delay) => _delay = delay;

        public int DelayHandles => _delay?.handles.Count ?? 0;

        public void InjectNull(int index) =>
            _pipeline.Insert(index == int.MaxValue ? _pipeline.Count : index, null);
    }

    /// <summary>
    /// Test-only observation modifier: handle never retires on its own, Enter/Exit are
    /// counted statically. Deliberately NOT [Serializable] — keeps it out of the Add menu.
    /// </summary>
    private class SpyEventModifier : EventModifier<SpyEventModifier.Handle>
    {
        public static int Enters, Exits;

        public class Handle : EventHandle<SpyEventModifier>
        {
            protected override void OnEnter() => Enters++;
            protected override void OnExit() => Exits++;
            protected override bool OnUpdate<T>(ref T @event) => false;   // stays alive until Reset
        }
    }
}
#endif
