using System.Collections.Generic;
using EventPipelines;
using UnityEngine;

/// <summary>
/// Field-based trigger demo: Trigger clock -> pipeline -> shots.
/// Fallback serialization pattern: the pipeline ([SerializeReference] list of plain
/// modifiers) serializes natively; the EventModified&lt;T&gt; wrapper is constructed at runtime.
/// Wire the inspector list with e.g. DebounceEventModifier (0.3) + RepeatEventModifier (3, 0.1).
/// </summary>
public class Gun : MonoBehaviour
{
    [SerializeReference] private List<EventModifier> _triggerPipeline = new();

    private EventModified<int> _trigger;
    private int _clicks;
    private float _nextClick;

    private void Awake()
    {
        _trigger = new EventModified<int>();

        foreach (var modifier in _triggerPipeline)
            _trigger.Add(modifier);

        _trigger.Settled += v => Debug.Log($"[Gun] Bang! (shot #{v})");
    }

    private void Update()
    {
        _trigger.Update();

        // Simulates 5 fast clicks (12.5/s) to demo the pipeline, e.g. Debounce -> Burst.
        if (_clicks < 5 && Time.time >= _nextClick)
        {
            _clicks++;
            _nextClick = Time.time + 0.08f;
            _trigger.Value++;   // pulse clock: post a click
        }
    }
}
