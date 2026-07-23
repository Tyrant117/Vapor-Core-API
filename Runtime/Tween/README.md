# Vapor Tweening

A small, standalone tweening library for Unity 6. It tweens **arbitrary values** (floats, vectors,
colors, quaternions, and your own types) and ships **high-level helpers** for `Transform`,
`RectTransform`, `CanvasGroup`, and UI Toolkit `VisualElement` style properties.

- Assembly: `vapor.core.tween` (no dependencies beyond the engine)
- Namespace: `Vapor.Tweening` — add `using Vapor.Tweening;`
- No scene setup: a hidden runner is created automatically the first time a tween starts.

To use it from another assembly, add `vapor.core.tween` to that assembly definition's references.

## Quick start

```csharp
using Vapor.Tweening;

// Transform
transform.TweenPosition(new Vector3(0, 5, 0), 1f).SetEase(Ease.OutQuad);
transform.TweenScale(2f, 0.5f).SetLoops(-1, LoopType.Yoyo);
transform.TweenRotation(Quaternion.Euler(0, 180, 0), 1f);

// UI Toolkit VisualElement — style properties
element.TweenOpacity(0f, 0.3f).OnComplete(() => element.RemoveFromHierarchy());
element.TweenBackgroundColor(Color.red, 0.25f);
element.TweenSize(new Vector2(200, 80), 0.4f).SetEase(Ease.OutBack);

// UI Toolkit VisualElement — translate / scale / rotate styles (do not reflow layout)
element.TweenPosition(new Vector2(0, -40), 0.35f).SetEase(Ease.OutCubic);
element.TweenScale(1.1f, 0.15f).SetLoops(2, LoopType.Yoyo);
element.TweenRotation(180f, 0.5f);

// uGUI
canvasGroup.TweenFadeIn(0.3f);
rectTransform.TweenAnchoredPosition(Vector2.zero, 0.5f);

// Any value at all
Tween.To(() => _health, v => _health = v, 100f, 1f).SetEase(Ease.Linear);
Tween.FromTo(v => _material.SetFloat("_Dissolve", v), 0f, 1f, 2f);
```

## Sequences

```csharp
Sequence.Create()
    .Append(panel.TweenOpacity(1f, 0.3f))     // then
    .Join(panel.TweenScale(1.1f, 0.3f))       // at the same time as the previous
    .AppendInterval(0.5f)                       // wait
    .AppendCallback(() => Debug.Log("shown"))  // fire a callback
    .SetLoops(2, LoopType.Yoyo);
```

Build a sequence synchronously in one chain — it starts playing the moment it's created.

**Looping:** with `Restart` (or `Incremental`), each loop is a clean replay: every child's `OnStart` / `OnStepComplete` / `OnComplete` fire again each loop. With `Yoyo`, children reverse via position instead, so their per-tween lifecycle callbacks fire once — put per-cycle logic on the sequence itself (`AppendCallback`, or `seq.OnStepComplete`).

### UI Toolkit: start after layout

A child captures its start value from `resolvedStyle` when the timeline first reaches it. If you start a sequence the same frame an element is created, attached, or shown (`display` flips from `none`), `resolvedStyle` can still be stale. Defer the start until layout resolves:

```csharp
Sequence.Create()
    .Append(panel.TweenOpacity(1f, 0.3f))
    .Join(panel.TweenScale(1f, 0.3f))
    .PlayAfterLayout(panel);   // pauses, then plays once panel is laid out
```

`PlayAfterLayout` works on any tween or sequence. For arbitrary deferral there's also `element.WhenReady(() => { ... })`, which runs immediately if the element is already laid out, otherwise once after its first `GeometryChangedEvent`.

## Configuration (fluent, chainable)

| Method | Purpose |
| --- | --- |
| `SetEase(Ease)` / `SetEase(AnimationCurve)` / `SetEase(Func<float,float>)` | Easing |
| `SetDelay(float)` | Start delay |
| `SetLoops(int, LoopType)` | Loop count (`-1` = infinite) and `Restart` / `Yoyo` / `Incremental` |
| `SetUpdate(bool)` / `SetUnscaled()` | Run on unscaled time (keeps going while `Time.timeScale == 0`) |
| `SetTimeScale(float)` | Per-tween speed multiplier |
| `SetAutoKill(bool)` | Keep the tween alive after completing so it can be restarted |
| `SetRelative(bool)` | Treat the end value as an offset from the start |
| `From(value)` / `From()` | Play from an explicit value, or from the end value to the current value |
| `SetTarget(object)` / `SetId(object)` | Tag for `Tween.Kill(target)` |

Callbacks: `OnStart`, `OnPlay`, `OnPause`, `OnUpdate`, `OnStepComplete`, `OnComplete`, `OnKill`, `OnRewind`.

Control: `Play()`, `Pause()`, `TogglePause()`, `Restart()`, `Rewind()`, `Goto(time)`,
`Complete()`, `Kill(complete)`. Bulk: `Tween.Kill(target)`, `Tween.KillAll()`, or
`element.KillTweens()` / `transform.KillTweens()`.

## Async / await

```csharp
await element.TweenOpacity(1f, 0.3f).AsAwaitable();
```

## Performance

- The runner advances tweens in one allocation-free `Update`; registration, de-registration and the kill sweep are all O(1) amortized.
- Built-in extensions (`transform.TweenPosition`, `element.TweenOpacity`, …) are **capture-free**: they use cached `static` reader/writer delegates, so no per-call closures are allocated.
- Completed tweens are **pooled and recycled**, so steady-state tween creation is allocation-free once warmed up. (`Tween.To(() => x, v => x = v, …)` still allocates the closures you pass — inherent to arbitrary get/set — but the tween object itself is recycled.)
- Because tweens are recycled, **don't hold a raw `Tween` reference and use it after it finishes** — that instance may already be driving a different animation. If you need to keep a reference around, snapshot a **`TweenHandle`** (below); it detects recycling and fails safe. Sequences are not pooled.

## Keeping a reference safely: `TweenHandle`

A `TweenHandle` is a value-type wrapper that survives recycling. It snapshots the tween's `Version` and re-checks it on every access, so a handle to a finished (and recycled) tween resolves to "dead" instead of touching whatever animation now owns that instance. It's a generation-checked handle, not a GC weak reference — allocation-free.

```csharp
TweenHandle fade = panel.TweenOpacity(0f, 0.3f); // implicit conversion (or .AsHandle())

// Any time later — safe even after it completed and was recycled:
bool stillRunning = fade.IsActive;   // false once completed/killed/recycled
if (fade.IsActive) fade.Kill();      // no-op if it already finished
fade.OnComplete(() => { /* ... */ }); // no-op if stale
```

Queries — `IsValid`, `IsActive`, `IsPlaying`, `IsComplete` — go false once recycled; control methods — `Kill`, `Pause`, `Play`, `Complete`, `Restart`, `Goto` — become no-ops. Use `TryGet(out Tween)` for advanced access.

## Custom types

Provide a `TweenOps<T>` (interpolate, and optionally add/scale for relative & incremental loops):

```csharp
var ops = new TweenOps<MyType>((a, b, t) => MyType.Lerp(a, b, t));
Tween.To(() => value, v => value = v, target, 1f, ops);
```
