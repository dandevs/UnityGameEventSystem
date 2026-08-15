# EventSystem2 — AGENTS.md

Field-based event pipeline system for Unity. An `EventModified<T>` field owns an ordered
pipeline of plain-class modifiers; writing the field (`.Value` / `.Post()`) enters the
pipeline, each modifier rents pooled `EventHandle`s that tick per frame, and the settled
result is cached back into the field without retriggering. MonoBehaviours appear only as
pipeline *owners* (they call `Tick()` in `Update`).

**2026-08 field-only rewrite:** the GameObject hub (`EventListenerModifierSystem`,
`DispatchEvent`, drag-and-drop `EventModifierContainer`s, per-type registration) was
REMOVED. Stale references to it are dead — do not resurrect them. The six hub-era bug
fixes carried forward into the new owner code (chain walk, fall-through, pool reset).

## Dependencies

- **UltEvents** — `OnSettle` terminal on `EventModified<T>`
- **Unity.Collections** — `UnsafeUtility.As` bitcasts (no boxing on hot path)
- **UnityEditor** (`Editor/` only) — `EventModifiedDrawer` (custom property drawer)
- Shapes (`MonoBehaviourGizmos`) — dropped in the field rewrite
- Odin Inspector — removed 2026-08 (standalone pass); the plugin now has zero editor-package dependencies

## Assembly layout (important)

`Assets/Plugins/**` compiles to **Assembly-CSharp-firstpass**; its `Editor/` subfolders
would compile to *Editor-firstpass*, which CANNOT see `Assembly-CSharp` (where
`Scripts/Modifiers` lives). That is why the self-tests sit in
`Scripts/Modifiers/Editor/`, not in this folder. Keep editor tooling that references
concrete modifiers outside the plugin.

## Architecture

```
Value = x ─→ Post(x) ─→ pipeline[0].Push ─→ (handles tick in Tick()) ─→ Continue
             │                                                        │
             │                              next modifier's Push ←─────┤
             │                                                        │
             └─ empty pipeline → Settle(x) ←── last Continue ←────────┘
                                        │
                     _latest = x; OnSettle.Invoke; Settled.Invoke
```

| File | Contents |
|---|---|
| `EventModified.cs` | `EventModified<T>` (Post/Value/Settle/Settled/OnSettle/Tick) + non-generic base (pipeline, chain walk) |
| `EventModifier.cs` | Plain `EventModifier` (Push/Tick abstract, Owner, Continue) + `EventModifier<THandle>` (rent/tick/retire handles) |
| `EventHandle.cs` | `EventHandle` (pool + `GenericEventHolder<T>` trampoline) + two generic variants |
| `EventModifierPersistent.cs` | `PersistentHandle<TModifier>` + `EventModifierPersistent<TModifier, THandle>` (one live handle per episode) |
| `EventSystem2.cs` | `IEventListener<T>` (subscribe contract) |
| `Editor/EventModifiedDrawer.cs` | Custom property drawer for `EventModified` fields — foldout header + play-mode value badge, native managed-reference pipeline list, per-modifier live handle counts, searchable Add Modifier dropdown (AdvancedDropdown, TypeCache-discovered, grouped Per-Event/Stream) |
| `Editor/EventModifierElementDrawer.cs` | Labels `[SerializeReference]` EventModifier list elements by concrete type ("Element 0" → "Delay", nulls → "Null"); `ModifierLabels` is the single source of display names (shared with the Add dropdown) |
| `Scripts/Modifiers/*` | Concrete modifiers (`Delay`, `Repeat`, `Burst`, `DamageOverTime` typed; `Debounce`, `Throttle` persistent) + `DamageEvent`, demo owners (`Gun`, `Enemy`) |
| `Scripts/Modifiers/Editor/` | `EventSystem2SelfTests` (also Tools/EventSystem2 menu) |

**Why two handle variants** — the trampoline solves C# generic erasure: a non-generic
base cannot declare a `T` field, so `EventHandle<TModifier>` parks the event in a
`GenericEventHolder<T>` cached per-(handle, T) in a `ConditionalWeakTable`, and
`Update()` bounces through it into `OnUpdate<T>(ref @event)`. Use it for event-agnostic
logic. `EventHandle<TModifier, TEvent>` is for event-specific logic — real typed field.
Do not "simplify" the CWT away.

**Why `UnsafeUtility.As`** — bitcast, not conversion. In `EventModified<T>.Continue<T2>`,
`T2 == T` by construction (Post only accepts `T`); never `Push` a foreign type into a
modifier owned by a different `EventModified<T>`.

## Usage

```csharp
// 1. Declare a field + pipeline (code-composed)...
public class Enemy : MonoBehaviour {
    public EventModified<DamageEvent> Damage = new(new DamageOverTimeModifier { TickCount = 5, Duration = 2f });
    void Awake() => Damage.Settled += e => Debug.Log(e.Amount);
    void Update() => Damage.Tick();          // owner ticks — once per frame, per field
}

// 2. ...or serialize the pipeline and build the wrapper at runtime (Gun.cs pattern —
//    works with plain [SerializeReference]; use this if closed-generic field
//    serialization fights back)
[SerializeReference] List<EventModifier> _pipeline = new();

// 3. Send: assignment posts. 4. Read: last SETTLED value. 5. React: Settled / OnSettle / IEventListener<T>.
enemy.Damage.Value = new DamageEvent(25f, attacker);
```

Serialization (verified 2026-08, Unity 6000.5 — SO disk round-trip via forced re-import): a
plain `[SerializeField] EventModified<T>` field serializes natively — closed generics
included — and `[SerializeReference]` alone suffices on the protected `_pipeline` (no
`[SerializeField]` needed on non-public fields). The inspector draws it through
`EventModifiedDrawer`; the pipeline list uses Unity's native managed-reference picker.
Deserialization rebinds `modifier.Owner` via `ISerializationCallbackReceiver`. SaintsField's
`[SaintsSerialized]` also works but is not required for the direct route. Unity 6 requires
every type in a serialized modifier's inheritance chain to carry `[Serializable]` — all
pipeline base classes do (`EventModifier`, `EventModifier<>`, `EventModifierPersistent<,>`);
keep it that way on any new base, or Unity warns per serialized instance.

## Writing modifiers

Modifiers are plain `[Serializable]` classes in `Scripts/Modifiers/` (outside the plugin —
see Assembly layout). Three patterns; pick by where the state lives (see Semantics →
State homes). Reference implementations exist for all three — copy the closest one.

**Pattern A — per-event, event-agnostic** (one handle per incoming event; overlapping
events run as independent handles = stack policy). Reference: `DelayEventModifier`.

```csharp
[Serializable]
public class MyEventModifier : EventModifier<MyEventModifier.Handle> {
    [Min(0f)] public float Seconds = 0.5f;      // config → modifier fields (shared, serialized)

    public class Handle : EventHandle<MyEventModifier> {      // trampoline: any event type
        private float _timeLeft;                              // per-event state → handle fields

        protected override void OnEnter() => _timeLeft = modifier.Seconds;
        // ^ REQUIRED init point — handles are pooled; never rely on field initializers/defaults

        protected override bool OnUpdate<T>(ref T @event) {   // T = whatever was posted
            _timeLeft -= Time.deltaTime;
            if (_timeLeft > 0f) return false;   // false = keep handle alive (wait)
            Continue(in @event);                // pass to next stage (or Settle at pipeline end)
            return true;                        // true = retire (Exit + return to pool)
        }
    }
}
```

**Pattern B — per-event, typed** (logic reads/writes event fields). Reference:
`DamageOverTimeModifier`. Same structure, but:

```csharp
public class Handle : EventHandle<MyEventModifier, DamageEvent> {   // declares the event type
    protected override bool OnUpdate(ref DamageEvent @event) {       // typed, no <T>
        Continue(new DamageEvent(@event.Amount * 2f, @event.Source)); // transform payload
        return true;
    }
}
```

Note: the declared `TEvent` must match the owner field's `T` exactly (same assembly types —
the bitcast in `Initialize` has no runtime conversion).

**Pattern C — persistent / stream** (cross-event state + latest payload: debounce,
throttle, coalescing; one live handle per episode). Reference: `DebounceEventModifier`.

```csharp
[Serializable]
public class MyStreamModifier
    : EventModifierPersistent<MyStreamModifier, MyStreamModifier.Handle> {
    public class Handle : PersistentHandle<MyStreamModifier> {
        protected override void OnEnter() { }                 // episode start (first event)
        protected override void OnPulse<T>(in T @event) { }   // each absorbed (subsequent) event
        protected override bool OnUpdate<T>(ref T @event) { } // retire (true) = episode over
    }
}
```

`Push` folding comes free from the base — don't override it (see the `ShouldAbsorb` note
in Semantics before changing absorb mechanics).

**Rules, all patterns:**

- `OnUpdate` returns `true` to retire the handle, `false` to keep it ticking.
- `Continue(in @event)` forwards; **returning `true` without `Continue` consumes** the
  event (it never settles).
- Init per use belongs in `OnEnter` — pooled handles arrive with `Reset()`-cleaned fields;
  never field initializers.
- `modifier` is the shared config instance — read it, never write it from handles.
- Cross-event state WITHOUT payload (counters, last-accepted timestamps) → plain modifier
  fields on a Pattern A/B modifier (e.g. a `MinInterval` rate gate); no handle gymnastics.
- Multi-value logic (fire N times): emit via multiple `Continue` calls across updates
  (see `Burst`/`Repeat`/`DamageOverTime`), not by reaching into other handles.

**Registration:** constructor (`new EventModified<T>(new MyEventModifier { ... })`),
`.Add(modifier)`, the `[SerializeReference]` list + runtime-wrap pattern (`Gun.cs`), or
the inspector's **Add Modifier** menu. The menu offers exactly the modifiers that match
the authoring contract: concrete, non-generic, `[Serializable]`, parameterless ctor
(discovered via `TypeCache`, filter in `EventModifiedDrawer.GetAddableModifierTypes`).
The native list's own "+" inserts a *null* managed reference — prefer the Add menu.

## Semantics agents must not break

- **Write = Post (enter pipeline); read = last settled value.** `Value = x; Value` returns
  the PREVIOUS settled result. Documented behavior, not a bug.
- **Settle is the single writer of `_latest` and never Posts** — no-retrigger is
  structural. Re-entrant `Post` from a `Settled`/`OnSettle` handler **throws**
  (depth guard). Cross-frame feedback (handler posting later, on its own) is allowed —
  infinite loops there are caller error.
- **Tick contract:** owners tick every field once per frame, from `Update()` (not
  FixedUpdate — handles read `Time.deltaTime`/`Time.time`). Double-ticking a field is
  safe (per-handle frame guard); not ticking stalls handles silently.
- **Handle lifecycle:** `Push` rents + `Enter()` (init belongs in `OnEnter` — handles are
  pooled; `Reset()` runs on pool return); each `Update()` at most once per frame; `true`
  → `Exit()` + pooled.
- **State homes:** per-event state → handle fields; cross-event state without payload
  (counters, last-accepted time) → modifier fields; cross-event state + latest payload
  (Debounce/Throttle/coalescing) → persistent handle (one live handle per episode,
  `handles.Count ∈ {0,1}`; `Pulse` = re-Initialize payload swap, no `OnEnter` rerun).
- **Future, deliberately NOT implemented:** a `ShouldAbsorb<T>(in T)` virtual on
  `EventModifierPersistent` as the fold-or-spill dial (persistent base is fold-only;
  spilling pulses into their own handle would relax the invariant to `{0..N}` and needs
  an oldest-vs-newest decision). Do not add ad-hoc `Push` overrides for this without
  settling that design.

## Fixed bugs (do not re-flag, do not regress)

Hub era (2026-08, pre-rewrite — semantics carried into `EventModified`):
1. Inverted `IsAssignableFrom` in typed-handle `Initialize` (silent type confusion).
2. Static shared listener list (reentrancy) → pooling.
3. Dispatch-target self-only check → nearest-owner walk.
4. `Continue` swallowed events on missing modifier → warn + settle/deliver (now: warn +
   `Settle` directly).
5. Pooled handles never reset → `virtual Reset()` from `ReturnHandle`.
6. Dead `order` field on modifiers — deleted.

Field era (2026-08 rewrite):
7. **`base.OnEnter()`/`base.OnExit()` in `EventHandle<TModifier>` never dispatched to
   derived overrides** (base calls are nonvirtual) — trampoline handles' `OnEnter` was
   always a no-op; latent from the original repo, invisible while defaults aligned with
   zero-init. Now `Enter()/Exit()` call `OnEnter()/OnExit()` virtually in both variants.

Standalone pass (2026-08):
8. **`Owner` never rebound after deserialization** — deserialization bypasses `Add()`, so
   every modifier in a serialized pipeline had `Owner == null` → NRE on the first
   `Continue`. Fixed: `EventModified` implements `ISerializationCallbackReceiver` and
   rebinds in `OnAfterDeserialize`. (Found via serialization spike. The suspected
   "pipeline silently not serialized" hypothesis was DISPROVEN — `[SerializeReference]`
   alone serializes non-public fields.)

## Known leftover issues (working list — revisit over time)

- 🟡 **`Settled`/`OnSettle` subscriber exceptions are not isolated.** A throwing handler
  propagates through `Settle ← Continue ← Update ← Tick` into the owner's `Update`,
  interrupting the remaining pipeline mid-walk. UltEvents' `InvokeSafe` does per-listener
  try/catch for exactly this; the C# `Settled` event doesn't. Fix: wrap `Settled?.Invoke`
  per-subscriber (or document `InvokeSafe` usage for `OnSettle`).
- 🟡 **IL2CPP/AOT unverified** (bitcasts, generic instantiations, SerializeReference —
  serialization itself verified editor-side only). Device smoke test before shipping.
- 🟡 **Temporal semantics untested.** Self-tests pin single-frame behavior only
  (EditMode time doesn't advance; handles read `Time.time` directly, so time-based
  logic needs a PlayMode run or a time-injection refactor to cover). Burst spacing,
  debounce windows, DoT cadence are the layer where regressions would hide.
- 🔵 **Handle pool is unbounded, main-thread-only.**
- 🔵 **Not yet driven by real usage.** The motivating scenario (weapon system as
  `Trigger`/`Ammo`/`Heat` `EventModified` fields with pipelines) compiles but has not
  run. Design gets honest through usage — wire `Gun` for real before extending the
  framework.
