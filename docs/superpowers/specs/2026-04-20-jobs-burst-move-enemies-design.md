# Jobs/Burst for Moving Enemies — Design Spec

**Date:** 2026-04-20
**Chapter branch:** Chapter-12-only-move
**Scope:** Add Unity Jobs + Burst to parallelize enemy movement, and use a `TransformAccessArray` to write the resulting positions onto pooled enemy GameObjects. Pedagogical example — one movement function converted, the rest untouched.

---

## Goals

1. Replace the per-frame `Logic.moveEnemies` loop with a Burst-compiled `IJobParallelFor`.
2. Replace Board's per-frame transform-sync loop with a Burst-compiled `IJobParallelForTransform` driven by a `TransformAccessArray`.
3. Keep the surface area small: **only** `moveEnemies` becomes a job. Collision, out-of-bounds, and player-move stay on the main thread.
4. Preserve visual parity with the pre-conversion build (same positions, same behavior).

## Non-goals

- Parallelizing `doEnemyToEnemyCollision`, `checkEnemyOutOfBounds`, or `movePlayer`.
- Cross-frame job scheduling / latency hiding. All jobs `Complete()` inside `Logic.Tick`.
- Changing the DOD architectural rules beyond what jobs require.

---

## Architecture

### Packages to add (`Packages/manifest.json`)

- `com.unity.burst`
- `com.unity.collections`
- `com.unity.mathematics`
- `com.unity.jobs`

### Data-layout change — `GameData`

`Vector2[] EnemyPosition` and several companion arrays become `NativeArray<T>`, because Burst jobs can't accept managed arrays.

| Before | After |
|---|---|
| `Vector2[] EnemyPosition` | `NativeArray<float2> EnemyPosition` |
| `int[] EnemyType` | `NativeArray<int> EnemyType` |
| `int[] AliveEnemyIndices` | `NativeArray<int> AliveEnemyIndices` |
| `int[] DeadEnemyIndices` | `NativeArray<int> DeadEnemyIndices` |
| *(new)* | `NativeArray<float> EnemyVelocityNative` — mirror of `Balance.EnemyVelocity`, built once in `AllocateGameData` |

All arrays allocated `Allocator.Persistent` once in `Logic.AllocateGameData`. A new `Logic.FreeGameData(GameData)` disposes them; called from `Game.OnDestroy`.

`AliveEnemyCount`, `DeadEnemyCount`, and every other scalar field on `GameData` stay unchanged.

### New state on `Board`

- `TransformAccessArray m_enemyTransforms` — pool-sized, holds enemy pool transforms in pool-index order. Grows via `.Add(transform)` the first time a given pool slot is instantiated.
- `NativeArray<int> m_poolToEnemyIndex` — size `MaxEnemyPoolSize`, value at slot `poolIndex` is the `enemyIndex` currently occupying that slot, or `-1` if the slot is free. This is the reverse of the existing `m_enemyToPoolIndex` map.

### Tick flow (inside `Logic.Tick`)

```
1. JobHandle moveHandle = MoveEnemiesJob.Schedule(aliveCount, 64, dependsOn: default)
2. moveHandle.Complete()
3. checkEnemyOutOfBounds                       (main thread, unchanged)
4. doEnemyToEnemyCollision                     (main thread, unchanged)
5. movePlayer                                  (main thread, unchanged)
6. JobHandle syncHandle = SyncEnemyTransformsJob.Schedule(m_enemyTransforms)
7. syncHandle.Complete()
```

Board's old "write `EnemyPosition[enemyIndex]` to `m_enemyPool[poolIndex].transform.localPosition`" loop is **removed** — step 6/7 does that work in parallel via the transform job.

`Logic.Tick`'s signature grows by two parameters: `TransformAccessArray enemyTransforms` and `NativeArray<int> poolToEnemyIndex`. Board passes them in; Logic never owns them. This preserves the DOD rule that Logic doesn't hold Unity-side references.

---

## The two job structs

### `MoveEnemiesJob` — Burst, `IJobParallelFor`

```csharp
[BurstCompile]
struct MoveEnemiesJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<int>   AliveEnemyIndices;
    [ReadOnly] public NativeArray<int>   EnemyType;
    [ReadOnly] public NativeArray<float> EnemyVelocity;

    [NativeDisableParallelForRestriction]
    public NativeArray<float2> EnemyPosition;

    public float Dt;

    public void Execute(int i)
    {
        int    enemyIndex = AliveEnemyIndices[i];
        float2 pos        = EnemyPosition[enemyIndex];
        float2 dir        = -math.normalizesafe(pos);
        float  speed      = EnemyVelocity[EnemyType[enemyIndex]];
        EnemyPosition[enemyIndex] = pos + dir * speed * Dt;
    }
}
```

Scheduled with `innerloopBatchCount = 64` (tunable).

**`[NativeDisableParallelForRestriction]` note:** writes are indexed by `enemyIndex` (from `AliveEnemyIndices[i]`), not by the loop index `i`. The parallel-for safety system would otherwise reject this. It is safe in practice because `AliveEnemyIndices[0..AliveEnemyCount-1]` contains unique values — no two iterations write to the same `EnemyPosition` slot.

### `SyncEnemyTransformsJob` — Burst, `IJobParallelForTransform`

```csharp
[BurstCompile]
struct SyncEnemyTransformsJob : IJobParallelForTransform
{
    [ReadOnly] public NativeArray<float2> EnemyPosition;
    [ReadOnly] public NativeArray<int>    PoolToEnemyIndex;

    public void Execute(int poolIndex, TransformAccess transform)
    {
        int enemyIndex = PoolToEnemyIndex[poolIndex];
        if (enemyIndex < 0) return;
        float2 p = EnemyPosition[enemyIndex];
        transform.localPosition = new Vector3(p.x, p.y, 0f);
    }
}
```

Scheduled via `SyncEnemyTransformsJob.Schedule(m_enemyTransforms)` — one execution per transform in the array. The `-1` gate skips unused pool slots.

---

## Lifecycle

### `Logic.AllocateGameData` (once, at startup)

```csharp
gameData.EnemyPosition       = new NativeArray<float2>(balance.MaxEnemies, Allocator.Persistent);
gameData.EnemyType           = new NativeArray<int>(balance.MaxEnemies, Allocator.Persistent);
gameData.AliveEnemyIndices   = new NativeArray<int>(balance.MaxEnemies, Allocator.Persistent);
gameData.DeadEnemyIndices    = new NativeArray<int>(balance.MaxEnemies, Allocator.Persistent);
gameData.EnemyVelocityNative = new NativeArray<float>(balance.EnemyVelocity, Allocator.Persistent);
```

### `Logic.FreeGameData` (new — called from `Game.OnDestroy`)

```csharp
if (gameData.EnemyPosition.IsCreated)       gameData.EnemyPosition.Dispose();
if (gameData.EnemyType.IsCreated)           gameData.EnemyType.Dispose();
if (gameData.AliveEnemyIndices.IsCreated)   gameData.AliveEnemyIndices.Dispose();
if (gameData.DeadEnemyIndices.IsCreated)    gameData.DeadEnemyIndices.Dispose();
if (gameData.EnemyVelocityNative.IsCreated) gameData.EnemyVelocityNative.Dispose();
```

### `Board.Init` (once)

```csharp
m_enemyTransforms  = new TransformAccessArray(MaxEnemyPoolSize);
m_poolToEnemyIndex = new NativeArray<int>(MaxEnemyPoolSize, Allocator.Persistent);
for (int i = 0; i < MaxEnemyPoolSize; i++) m_poolToEnemyIndex[i] = -1;
```

### `Board.OnDestroy`

```csharp
if (m_enemyTransforms.isCreated)  m_enemyTransforms.Dispose();
if (m_poolToEnemyIndex.IsCreated) m_poolToEnemyIndex.Dispose();
```

### Spawn path — `Board.Tick` loop over `addedEnemyIndices`

```csharp
int poolIndex = getFreeEnemyPoolIndex(enemyType);
m_enemyPool[poolIndex].SetActive(true);
m_enemyToPoolIndex[enemyIndex] = poolIndex;
m_poolToEnemyIndex[poolIndex]  = enemyIndex;   // new
```

Inside `getFreeEnemyPoolIndex`, when a brand-new pool GameObject is instantiated (the `m_enemyPoolCount < MaxEnemyPoolSize` branch):

```csharp
m_enemyPool[m_enemyPoolCount] = AssetManager.Instance.GetEnemyGameObject(...);
m_enemyTransforms.Add(m_enemyPool[m_enemyPoolCount].transform);   // new
```

### Despawn path — `Board.Tick` loop over `removedEnemyIndices`

```csharp
int poolIndex = m_enemyToPoolIndex[enemyIndex];
m_enemyPool[poolIndex].SetActive(false);
m_enemyPoolUnusedIndices[m_enemyPoolUnusedIndicesCount++] = poolIndex;
m_poolToEnemyIndex[poolIndex] = -1;   // new — gate the transform-sync job
```

### `Board.Show` (restoring from save)

Current logic walks `AliveEnemyIndices`, assigns pool slots, and sets `m_enemyToPoolIndex[enemyIdx] = poolIndex`. Add the inverse update:

```csharp
m_poolToEnemyIndex[poolIndex] = enemyIdx;
```

Unused slots remain at `-1` from `Init` / `Hide`.

### `Board.Hide`

After destroying pool GameObjects (unchanged), also reset the parallel-access structures:

```csharp
m_enemyTransforms.Dispose();
m_enemyTransforms = new TransformAccessArray(MaxEnemyPoolSize);
for (int i = 0; i < MaxEnemyPoolSize; i++) m_poolToEnemyIndex[i] = -1;
```

### Safety — why this ordering is safe

- `TransformAccessArray` must not be resized or disposed while a job that uses it is in flight.
- Both jobs `Complete()` inside `Logic.Tick` before it returns.
- `Hide` and pool-growth (`.Add`) happen on the main thread, outside of `Tick`, so no in-flight job can be touching the array.
- No `JobHandle` leaks outside of `Logic.Tick`.

---

## Save/Load (`GameDataIO`)

The only change is swapping `Vector2.x, Vector2.y` reads/writes to `float2.x, float2.y`. Byte layout (two floats per position, 4-byte ints elsewhere) is unchanged → **existing save files continue to load correctly; no version bump.**

Example (Save):
```csharp
for (int i = 0; i < balance.MaxEnemies; i++)
{
    bw.Write(gameData.EnemyPosition[i].x);
    bw.Write(gameData.EnemyPosition[i].y);
}
```
(Identical to today; only the element type changes.)

---

## Balance-side data

`Balance.EnemyVelocity` stays `float[]` (keeps `[Serializable]` editing, `BalanceSO` workflow, and tool-time parsing managed-array-friendly). A `NativeArray<float> EnemyVelocityNative` is built once from it in `Logic.AllocateGameData` and lives on `GameData`. Balance is immutable at runtime, so the mirror stays in sync by construction.

Rejected alternative: put the `NativeArray<float>` directly on `Balance` and allocate it in `LoadBalance()`. Cleaner single source of truth, but makes `Balance` handle native disposal and complicates `[Serializable]` editing in the Inspector.

---

## Files touched

| File | Change |
|---|---|
| `Packages/manifest.json` | Add burst, collections, mathematics, jobs packages |
| `Assets/Scripts/GameData.cs` | Swap 4 `[]` fields to `NativeArray<>`; add `EnemyVelocityNative` |
| `Assets/Scripts/Logic.cs` | Add 2 job structs; `Tick` dispatches them; add `FreeGameData`; `Vector2 → float2` type swaps in `spawnEnemy` / collision / bounds / player |
| `Assets/Scripts/Board.cs` | Add `m_enemyTransforms` + `m_poolToEnemyIndex`; remove old transform-sync loop; maintain maps in spawn/despawn/Show/Hide; add `OnDestroy` |
| `Assets/Scripts/Game.cs` | Call `Logic.FreeGameData` in `OnDestroy` |
| `Assets/Scripts/GameDataIO.cs` | Swap `Vector2 ↔ float2` reads/writes (byte-compatible) |

---

## Testing

1. **Correctness parity:** with a fixed RNG seed and fixed input, snapshot enemy positions over 60 ticks before the conversion; rerun after the conversion; compare position arrays frame-by-frame. Accept small epsilon for `math.normalizesafe` vs `Vector2.normalized`.
2. **`normalizesafe` vs `Vector2.normalized`:** both return zero when magnitude is below a tiny epsilon. Flag if divergence appears near the origin.
3. **Leak detection:** enter and exit play mode ~10 times. Any `Leak Detected` warning from the `NativeArray` safety system indicates a missing `Dispose`.
4. **Visual smoke test:** play the game; enemies should move identically to the pre-conversion build.
5. **Burst verification:** open *Jobs → Burst → Open Inspector* and confirm `MoveEnemiesJob` and `SyncEnemyTransformsJob` appear as compiled entries. If absent, `[BurstCompile]` isn't taking effect.

---

## Open questions / risks

- **`[NativeDisableParallelForRestriction]` correctness** depends on `AliveEnemyIndices` containing no duplicates. The existing spawn/despawn code maintains this invariant (stack-style add, shift-left remove). Worth a runtime assertion in debug builds if we ever see weird behavior.
- **`TransformAccessArray.Add` during `Tick`** (spawn path) — `Add` is legal because no job using the array is running at that point: spawn handling happens *after* `Logic.Tick` returns, and we haven't scheduled the next sync job yet. If this ordering ever changes, revisit.
- **`EnemyVelocityNative` staleness** — if balance is ever hot-reloaded at runtime, the mirror would go stale. Current project reloads balance only at app startup, so this is not a concern today; flag it if balance hot-reload is added later.
