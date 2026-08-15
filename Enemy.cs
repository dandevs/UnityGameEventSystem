using EventPipelines;
using SaintsField.Playa;
using UnityEngine;

/// <summary>
/// Field-based damage demo. [SaintsSerialized] attempt — direct serialization of the
/// closed-generic field. If serialization fights back, switch to the Gun.cs pattern
/// (serialized pipeline + runtime wrapper construction).
/// </summary>
public partial class Enemy : MonoBehaviour
{
    [SaintsSerialized]
    public EventModified<DamageEvent> Damage = new();

    private void Awake() =>
        Damage.Settled += e => Debug.Log($"[Enemy] took {e.Amount} (source: {(e.Source != null ? e.Source.name : "null")})");

    private void Update() => Damage.Tick();
}
