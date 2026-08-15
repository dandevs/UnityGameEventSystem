#if UNITY_EDITOR
using System;
using EventSystem2;
using UltEvents;
using UnityEditor;
using UnityEngine;

/// <summary>
/// EditMode self-tests for the field-based pipeline core. Runs automatically on domain
/// reload and via Tools/EventSystem2/Run Self Tests. Proper Unity Test Framework needs
/// asmdefs (this project compiles to Assembly-CSharp), so these are plain assertions.
/// Lives in the plugin's Tests/Editor folder (Assembly-CSharp-Editor-firstpass) — fine
/// since the builtin modifiers are plugin types too. Do NOT reference game-side types
/// (Gun, Enemy) here: Editor-firstpass cannot see Assembly-CSharp.
/// Timing-heavy behavior (multi-frame bursts, real delays, hold-release) is NOT covered
/// here — these tests only pin the semantics reachable within a single frame/tick.
/// </summary>
public static class EventSystem2SelfTests
{
    [InitializeOnLoadMethod]
    private static void RunOnLoad() => RunAll(false);

    [MenuItem("Tools/EventSystem2/Run Self Tests")]
    private static void MenuRun() => RunAll(true);

    public static string RunAll(bool verbose)
    {
        var pass = 0;
        var fail = 0;

        void Check(string name, bool ok)
        {
            if (ok) { pass++; return; }
            fail++;
            Debug.LogError($"[EventSystem2 SelfTest FAIL] {name}");
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

        // 3. Zero-delay chain: one Tick walks the full pipeline in order.
        var chain = new EventModified<int>(
            new DelayEventModifier { Seconds = 0f },
            new DelayEventModifier { Seconds = 0f });
        var chainGot = -1;
        chain.Settled += v => chainGot = v;
        chain.Post(9);
        Check("pre-tick-not-settled", chainGot == -1);
        chain.Tick();
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
        debField.Tick();
        Check("debounce-no-settle-in-window", settles == 0);
        Check("debounce-value-unchanged", debField.Value.Equals(default(int)));

        // 6. Repeat: one Tick emits exactly the first shot; handle stays alive.
        var repeat = new RepeatEventModifier { Count = 3, Interval = 5f };
        var repeatField = new EventModified<int>(repeat);
        var shots = 0;
        repeatField.Settled += _ => shots++;
        repeatField.Post(1);
        repeatField.Tick();
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
        sure.Tick();
        Check("chance-p1-always-passes", sureGot == 5 && sure.Value == 5);

        var never = new EventModified<int>(new ChanceEventModifier { Probability = 0f });
        var neverGot = -1;
        never.Settled += v => neverGot = v;
        never.Post(5);
        never.Tick();
        Check("chance-p0-always-consumes", neverGot == -1 && never.Value.Equals(default(int)));

        // 9. MinHold while held below minimum: episode alive, nothing settles
        //    (same-frame pulse + tick; release/threshold paths need frame advance).
        var minHold = new MinHoldEventModifier { MinimumSeconds = 5f };
        var holdField = new EventModified<int>(minHold);
        var holdSettles = 0;
        holdField.Settled += _ => holdSettles++;
        holdField.Post(1);
        holdField.Tick();
        Check("minhold-holding-no-settle", holdSettles == 0 && minHold.handles.Count == 1);

        var summary = $"EventSystem2 self-tests: {pass} passed, {fail} failed";
        if (verbose || fail > 0)
            Debug.Log($"<color={(fail > 0 ? "red" : "green")}>{summary}</color>");
        return summary;
    }
}
#endif
