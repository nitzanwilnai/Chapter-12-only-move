# Jobs/Burst for Moving Enemies Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert `Logic.moveEnemies` to a Burst-compiled `IJobParallelFor` and use a `TransformAccessArray`-driven `IJobParallelForTransform` to write enemy positions onto pooled GameObjects.

**Architecture:** `GameData.EnemyPosition` (and companion arrays) become `NativeArray<T>`. `Logic.Tick` schedules two Burst jobs per frame: `MoveEnemiesJob` (parallel position math) and `SyncEnemyTransformsJob` (parallel transform writes via `TransformAccessArray`). Both jobs `Complete()` inside `Logic.Tick`; nothing leaks across frames. Collision, out-of-bounds, and player-move stay on the main thread.

**Tech Stack:** Unity 2022+, C#, Unity Jobs + Burst + Collections + Mathematics packages, TransformAccessArray.

**Spec:** `docs/superpowers/specs/2026-04-20-jobs-burst-move-enemies-design.md`

---

## Project conventions

- No automated test framework is wired up in this chapter branch. Verification after each task is:
  1. **Compile check:** `dotnet build Chapter-12-only-move.sln` from the project root (catches C# syntax/type errors).
  2. **Unity Editor recompile:** keep the Editor open throughout; after saving files, watch the Console for any red errors after Unity's domain reload.
  3. **Playmode smoke test** (only for tasks that change runtime behavior): enter Play → start a new game → confirm enemies spawn, move inward, collide, exit bounds, and the player input circle works. Exit Play cleanly — watch for `Leak Detected` warnings in the Console.
- Commit after each task. Use conventional-ish messages matching the existing `Initial commit` tone — short, imperative.

## File Structure

Files created: none.

Files modified:
- `Packages/manifest.json` — add 4 Unity packages.
- `Assets/Scripts/GameData.cs` — swap 4 managed arrays to `NativeArray`; add `EnemyVelocityNative`.
- `Assets/Scripts/Logic.cs` — add `FreeGameData`; add 2 job structs; change `Tick` to schedule jobs; `Vector2 → float2` updates in helpers that index `EnemyPosition`.
- `Assets/Scripts/Board.cs` — add `TransformAccessArray` + `NativeArray<int>` fields; maintain them in `Init`/`Show`/`Hide`/spawn/despawn; remove the main-thread transform-sync loop at the end of `Tick`; pass the two structures into `Logic.Tick`.
- `Assets/Scripts/Game.cs` — call `Logic.FreeGameData` in `OnDestroy`.
- `Assets/Scripts/GameDataIO.cs` — update `Load` to construct `float2` via `new float2(x, y)` (NativeArray indexers return by value, so component assignment no longer compiles).

---

### Task 1: Install Unity Burst + Mathematics + Collections packages

**Files:**
- Modify: `Packages/manifest.json`

Unity version (from `ProjectSettings/ProjectVersion.txt`) is **2022.3.62f2**. For this version, `IJobParallelFor`, `JobHandle`, `IJobParallelForTransform`, and `TransformAccessArray` are all in Unity core (shipped with the `Unity.Jobs` and `UnityEngine.Jobs` namespaces built into the engine). We only need three packages: Burst (for the `[BurstCompile]` attribute), Mathematics (for `float2` and `math.*`), and Collections (for `[NativeDisableParallelForRestriction]` in Unity 2022).

- [ ] **Step 1: Edit `Packages/manifest.json` to add 3 dependencies**

Final `manifest.json` `dependencies` block should include these three new entries (alphabetized with the existing entries):

```json
{
  "dependencies": {
    "com.unity.burst": "1.8.12",
    "com.unity.collab-proxy": "2.7.1",
    "com.unity.collections": "2.1.4",
    "com.unity.feature.2d": "2.0.1",
    "com.unity.ide.rider": "3.0.36",
    "com.unity.ide.visualstudio": "2.0.22",
    "com.unity.mathematics": "1.3.1",
    "com.unity.test-framework": "1.1.33",
    "com.unity.textmeshpro": "3.0.7",
    ...rest unchanged...
  }
}
```

(If any of these versions are newer than what Unity's Package Manager offers, Unity will substitute a compatible one automatically — that's fine. Do **not** add `com.unity.jobs` — that's a separate experimental package we don't need.)

- [ ] **Step 2: Switch to the Unity Editor and let it import the new packages**

Expected: Console shows "Package Manager" resolving dependencies, Library/ regenerates, compilation succeeds with no errors. This may take 30–60 seconds on first import.

- [ ] **Step 3: Verify Burst is visible**

In Unity menu bar: `Jobs → Burst → Open Inspector`. A new window opens. Contents will be empty for now — we haven't written any `[BurstCompile]` structs yet. Simply confirming the menu entry exists is enough.

- [ ] **Step 4: Commit**

```bash
git add Packages/manifest.json Packages/packages-lock.json
git commit -m "Add Unity Jobs, Burst, Collections, Mathematics packages"
```

(`packages-lock.json` will also update automatically; stage it.)

---

### Task 2: Convert GameData arrays to NativeArray and update all consumers

This is a large atomic change because `GameData`'s field types are used in `Logic.cs`, `Board.cs`, `GameDataIO.cs`, and (transitively) `Game.cs`. Split across multiple commits would leave the project in a non-compiling state. Do it all in one commit.

**Files:**
- Modify: `Assets/Scripts/GameData.cs`
- Modify: `Assets/Scripts/Logic.cs`
- Modify: `Assets/Scripts/Board.cs`
- Modify: `Assets/Scripts/Game.cs`
- Modify: `Assets/Scripts/GameDataIO.cs`

- [ ] **Step 1: Replace `GameData.cs` entirely**

```csharp
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;

namespace Survivor
{
    public class GameData
    {
        public bool InGame;

        public NativeArray<int> AliveEnemyIndices;
        public int AliveEnemyCount;
        public NativeArray<int> DeadEnemyIndices;
        public int DeadEnemyCount;

        public float SpawnTime;

        public NativeArray<float2> EnemyPosition;
        public NativeArray<int>    EnemyType;

        public NativeArray<float> EnemyVelocityNative;

        public Vector2 PlayerDirection;

        public float GameTime;
    }
}
```

(`PlayerDirection` stays `Vector2` — it's a scalar, not an array, and integrates with Unity's input handling which returns `Vector2`.)

- [ ] **Step 2: Update `Logic.AllocateGameData` to use NativeArray constructors**

In `Assets/Scripts/Logic.cs`, first add two `using` lines at the top (below the existing `using UnityEngine;` / `using System;`):

```csharp
using Unity.Collections;
using Unity.Mathematics;
```

Then replace the entire `AllocateGameData` body:

```csharp
public static void AllocateGameData(GameData gameData, Balance balance)
{
    gameData.EnemyPosition       = new NativeArray<float2>(balance.MaxEnemies, Allocator.Persistent);
    gameData.EnemyType           = new NativeArray<int>(balance.MaxEnemies, Allocator.Persistent);
    gameData.AliveEnemyIndices   = new NativeArray<int>(balance.MaxEnemies, Allocator.Persistent);
    gameData.DeadEnemyIndices    = new NativeArray<int>(balance.MaxEnemies, Allocator.Persistent);
    gameData.EnemyVelocityNative = new NativeArray<float>(balance.EnemyVelocity, Allocator.Persistent);
}
```

- [ ] **Step 3: Add `Logic.FreeGameData`**

Insert this method in `Logic.cs` immediately after `AllocateGameData`:

```csharp
public static void FreeGameData(GameData gameData)
{
    if (gameData.EnemyPosition.IsCreated)       gameData.EnemyPosition.Dispose();
    if (gameData.EnemyType.IsCreated)           gameData.EnemyType.Dispose();
    if (gameData.AliveEnemyIndices.IsCreated)   gameData.AliveEnemyIndices.Dispose();
    if (gameData.DeadEnemyIndices.IsCreated)    gameData.DeadEnemyIndices.Dispose();
    if (gameData.EnemyVelocityNative.IsCreated) gameData.EnemyVelocityNative.Dispose();
}
```

- [ ] **Step 4: Update `Logic.spawnEnemy` to write `float2` into `EnemyPosition`**

Replace the body of `spawnEnemy` in `Logic.cs` with:

```csharp
static void spawnEnemy(GameData gameData, Balance balance, Span<int> addedEnemyIndices, ref int addedEnemyCount)
{
    int enemyIndex = gameData.DeadEnemyIndices[--gameData.DeadEnemyCount];
    gameData.AliveEnemyIndices[gameData.AliveEnemyCount++] = enemyIndex;
    addedEnemyIndices[addedEnemyCount++] = enemyIndex;

    Vector2 direction = gameData.PlayerDirection;
    float angle = UnityEngine.Random.value * 180.0f - 90.0f;
    if (direction.magnitude == 0.0f)
    {
        direction = new Vector2(0.0f, 1.0f);
        angle = UnityEngine.Random.value * 360.0f;
    }
    direction = RotateVector(direction, angle);
    Vector2 spawnPos = direction.normalized * balance.SpawnRadius;
    gameData.EnemyPosition[enemyIndex] = new float2(spawnPos.x, spawnPos.y);
    gameData.EnemyType[enemyIndex] = getRandomEnemyTypeByWeight(balance);
}
```

(Explicit `new float2(x, y)` — NativeArray indexer returns by value so direct assignment of the whole element is required.)

- [ ] **Step 5: Update `Logic.moveEnemies` to use `float2` math** *(still main-thread in this task — we turn it into a job in Task 5)*

Replace the body of `moveEnemies` in `Logic.cs`:

```csharp
static void moveEnemies(GameData gameData, Balance balance, float dt)
{
    for (int i = 0; i < gameData.AliveEnemyCount; i++)
    {
        int enemyIndex = gameData.AliveEnemyIndices[i];
        float2 pos     = gameData.EnemyPosition[enemyIndex];
        float2 dir     = -math.normalizesafe(pos);
        int    enemyType = gameData.EnemyType[enemyIndex];
        gameData.EnemyPosition[enemyIndex] = pos + dir * balance.EnemyVelocity[enemyType] * dt;
    }
}
```

- [ ] **Step 6: Update `Logic.doEemyToEnemyCollision` for `float2`**

Replace the body in `Logic.cs`:

```csharp
static void doEemyToEnemyCollision(GameData gameData, Balance balance)
{
    for (int i = 0; i < gameData.AliveEnemyCount; i++)
    {
        int enemyIndex1 = gameData.AliveEnemyIndices[i];
        float radius1 = balance.EnemyRadius[gameData.EnemyType[enemyIndex1]];
        for (int j = i + 1; j < gameData.AliveEnemyCount; j++)
        {
            int enemyIndex2 = gameData.AliveEnemyIndices[j];
            float2 pos1 = gameData.EnemyPosition[enemyIndex1];
            float2 pos2 = gameData.EnemyPosition[enemyIndex2];
            float2 diff = pos1 - pos2;
            float distance = radius1 + balance.EnemyRadius[gameData.EnemyType[enemyIndex2]];
            float distanceSqr = distance * distance;
            if (math.lengthsq(diff) <= distanceSqr)
            {
                float2 diffNormalized = math.normalizesafe(diff);
                float2 midPoint = (pos1 + pos2) * 0.5f;
                float halfTotalRadius = (balance.EnemyRadius[gameData.EnemyType[enemyIndex1]] + balance.EnemyRadius[gameData.EnemyType[enemyIndex2]]) * 0.5f;
                gameData.EnemyPosition[enemyIndex1] = midPoint + diffNormalized * halfTotalRadius;
                gameData.EnemyPosition[enemyIndex2] = midPoint - diffNormalized * halfTotalRadius;
            }
        }
    }
}
```

- [ ] **Step 7: Update `Logic.checkEnemyOutOfBounds` for `float2`**

Replace the body:

```csharp
static void checkEnemyOutOfBounds(GameData gameData, Balance balance, Span<int> removedEnemyIndices, ref int removedEnemyCount)
{
    float distanceSqr = balance.SpawnRadius * balance.SpawnRadius * 1.1f;
    for (int i = 0; i < gameData.AliveEnemyCount; i++)
    {
        int enemyIndex = gameData.AliveEnemyIndices[i];
        if (math.lengthsq(gameData.EnemyPosition[enemyIndex]) > distanceSqr)
            removeEnemy(gameData, enemyIndex, removedEnemyIndices, ref removedEnemyCount);
    }
}
```

- [ ] **Step 8: Update `Logic.movePlayer` for `float2`**

Replace the body:

```csharp
static void movePlayer(GameData gameData, Balance balance, float dt)
{
    Vector2 playerPos2 = gameData.PlayerDirection * balance.PlayerVelocity * dt;
    float2 playerDelta = new float2(playerPos2.x, playerPos2.y);
    for (int i = 0; i < gameData.AliveEnemyCount; i++)
    {
        int enemyIndex = gameData.AliveEnemyIndices[i];
        gameData.EnemyPosition[enemyIndex] -= playerDelta;
    }
}
```

(`-=` on a NativeArray element works: the indexer gets, subtracts, sets.)

- [ ] **Step 9: Update `Logic.checkGameOver` for `float2`**

Replace the body:

```csharp
static bool checkGameOver(MetaData metaData, GameData gameData, Balance balance)
{
    for (int i = 0; i < gameData.AliveEnemyCount; i++)
    {
        int enemyIndex = gameData.AliveEnemyIndices[i];
        if (math.length(gameData.EnemyPosition[enemyIndex]) < balance.PlayerRadius)
        {
            if (gameData.GameTime > metaData.BestTime)
                metaData.BestTime = gameData.GameTime;

            gameData.InGame = false;
            return true;
        }
    }
    return false;
}
```

- [ ] **Step 10: Update `Board.Tick`'s final transform-sync loop to read `float2`**

Find this block at the end of `Board.Tick` in `Assets/Scripts/Board.cs`:

```csharp
for (int i = 0; i < gameData.AliveEnemyCount; i++)
{
    int enemyIndex = gameData.AliveEnemyIndices[i];
    int poolIndex = m_enemyToPoolIndex[enemyIndex];
    m_enemyPool[poolIndex].transform.localPosition = gameData.EnemyPosition[enemyIndex];
}
```

Replace it with:

```csharp
for (int i = 0; i < gameData.AliveEnemyCount; i++)
{
    int enemyIndex = gameData.AliveEnemyIndices[i];
    int poolIndex = m_enemyToPoolIndex[enemyIndex];
    float2 p = gameData.EnemyPosition[enemyIndex];
    m_enemyPool[poolIndex].transform.localPosition = new Vector3(p.x, p.y, 0f);
}
```

Also add this `using` at the top of `Board.cs`:

```csharp
using Unity.Mathematics;
```

- [ ] **Step 11: Update `GameDataIO.Load` to use `new float2(...)` for positions**

In `Assets/Scripts/GameDataIO.cs`, add this `using` at the top:

```csharp
using Unity.Mathematics;
```

Find this block in `Load`:

```csharp
int numEnemies = br.ReadInt32();
for (int i = 0; i < numEnemies; i++)
{
    gameData.EnemyPosition[i].x = br.ReadSingle();
    gameData.EnemyPosition[i].y = br.ReadSingle();
}
```

Replace with:

```csharp
int numEnemies = br.ReadInt32();
for (int i = 0; i < numEnemies; i++)
{
    float x = br.ReadSingle();
    float y = br.ReadSingle();
    gameData.EnemyPosition[i] = new float2(x, y);
}
```

Also find the `PlayerDirection` component-assignment in `Load`:

```csharp
gameData.PlayerDirection.x = br.ReadSingle();
gameData.PlayerDirection.y = br.ReadSingle();
```

Leave this unchanged — `PlayerDirection` is still a `Vector2`, which is a struct *field* (not a NativeArray element), so component assignment compiles fine. (Vector2 fields of a reference-typed class support this; NativeArray elements do not.)

(`Save` already uses `gameData.EnemyPosition[i].x`/`.y` **reads** which compile fine against `float2` because `float2` has public `x`/`y` fields. No change needed.)

- [ ] **Step 12: Add `Game.OnDestroy` to free GameData**

Open `Assets/Scripts/Game.cs` and add this method in the `Game` class (place it next to `Start`):

```csharp
void OnDestroy()
{
    Logic.FreeGameData(m_gameData);
}
```

No new `using` needed — `Logic` is already in scope.

- [ ] **Step 13: Compile check (command line)**

Run from project root:

```bash
dotnet build Chapter-12-only-move.sln
```

Expected: `Build succeeded. 0 Error(s).` (warnings are fine).

- [ ] **Step 14: Compile check (Unity Editor)**

Switch to Unity Editor, allow recompile, check Console. Expected: no red errors.

- [ ] **Step 15: Playmode smoke test**

Enter Play. Start a new game. Verify:
- Enemies spawn on ring, move inward, collide, exit out of bounds.
- Drag mouse → player input circle works, world scrolls.
- Pause button works.
- Exit Play.

In Console, confirm **no `Leak Detected` warnings**. (If you see one, a NativeArray wasn't disposed — most likely because `OnDestroy` didn't run, or `FreeGameData` is missing a field.)

- [ ] **Step 16: Commit**

```bash
git add Assets/Scripts/GameData.cs Assets/Scripts/Logic.cs Assets/Scripts/Board.cs Assets/Scripts/Game.cs Assets/Scripts/GameDataIO.cs
git commit -m "Convert GameData enemy arrays to NativeArray<T>"
```

---

### Task 3: Add TransformAccessArray + PoolToEnemyIndex bookkeeping on Board

At the end of this task, the new structures exist and are maintained but **not yet used** — Board still does its old main-thread transform sync. This task changes no runtime behavior; it only adds bookkeeping that Task 7 will consume.

**Files:**
- Modify: `Assets/Scripts/Board.cs`

- [ ] **Step 1: Add `using` lines and new fields on `Board`**

At the top of `Assets/Scripts/Board.cs`, add:

```csharp
using Unity.Collections;
using UnityEngine.Jobs;
```

(`Unity.Mathematics` was already added in Task 2.)

In the `Board` class, add two fields next to the other pool fields (after `int m_enemyPoolCount;`):

```csharp
TransformAccessArray m_enemyTransforms;
NativeArray<int>     m_poolToEnemyIndex;
```

- [ ] **Step 2: Initialize them in `Board.Init`**

Find the block in `Init` that allocates the pool arrays:

```csharp
m_enemyPool = new GameObject[MaxEnemyPoolSize];
m_enemyPoolType = new int[MaxEnemyPoolSize];
m_enemyToPoolIndex = new int[MaxEnemyPoolSize];
m_enemyPoolUnusedIndices = new int[MaxEnemyPoolSize];
m_enemyPoolUnusedIndicesCount = 0;
```

Add immediately after it:

```csharp
m_enemyTransforms  = new TransformAccessArray(MaxEnemyPoolSize);
m_poolToEnemyIndex = new NativeArray<int>(MaxEnemyPoolSize, Allocator.Persistent);
for (int i = 0; i < MaxEnemyPoolSize; i++) m_poolToEnemyIndex[i] = -1;
```

- [ ] **Step 3: Add `OnDestroy` on `Board`**

Add this method to `Board`:

```csharp
void OnDestroy()
{
    if (m_enemyTransforms.isCreated)  m_enemyTransforms.Dispose();
    if (m_poolToEnemyIndex.IsCreated) m_poolToEnemyIndex.Dispose();
}
```

(Note: `TransformAccessArray.isCreated` is lowercase `i`; `NativeArray<T>.IsCreated` is uppercase `I`. Not a typo.)

- [ ] **Step 4: `Board.getFreeEnemyPoolIndex` — call `m_enemyTransforms.Add` when a new pool GO is instantiated**

Find this block inside `getFreeEnemyPoolIndex`:

```csharp
if (m_enemyPoolCount < MaxEnemyPoolSize)
{
    m_enemyPool[m_enemyPoolCount] = AssetManager.Instance.GetEnemyGameObject(SpriteParent, balance.EnemyPrefabName[enemyType]);

    Debug.Log("m_enemyPool[" + m_enemyPoolCount + "] " + m_enemyPool[m_enemyPoolCount].name);

    m_enemyPoolType[m_enemyPoolCount] = enemyType;
    m_enemyPoolCount++;
    return m_enemyPoolCount - 1;
}
```

Replace with:

```csharp
if (m_enemyPoolCount < MaxEnemyPoolSize)
{
    m_enemyPool[m_enemyPoolCount] = AssetManager.Instance.GetEnemyGameObject(SpriteParent, balance.EnemyPrefabName[enemyType]);

    Debug.Log("m_enemyPool[" + m_enemyPoolCount + "] " + m_enemyPool[m_enemyPoolCount].name);

    m_enemyPoolType[m_enemyPoolCount] = enemyType;
    m_enemyTransforms.Add(m_enemyPool[m_enemyPoolCount].transform);
    m_enemyPoolCount++;
    return m_enemyPoolCount - 1;
}
```

- [ ] **Step 5: Maintain `m_poolToEnemyIndex` in `Board.Tick`'s spawn loop**

Find this block in `Board.Tick`:

```csharp
for (int i = 0; i < addedEnemyCount; i++)
{
    int enemyIndex = addedEnemyIndices[i]; // This is the index in the gameData arrays, not the enemy pool index
    int enemyType = gameData.EnemyType[enemyIndex];

    int poolIndex = getFreeEnemyPoolIndex(enemyType);
    m_enemyPool[poolIndex].SetActive(true);
    m_enemyToPoolIndex[enemyIndex] = poolIndex;
}
```

Replace with:

```csharp
for (int i = 0; i < addedEnemyCount; i++)
{
    int enemyIndex = addedEnemyIndices[i]; // This is the index in the gameData arrays, not the enemy pool index
    int enemyType = gameData.EnemyType[enemyIndex];

    int poolIndex = getFreeEnemyPoolIndex(enemyType);
    m_enemyPool[poolIndex].SetActive(true);
    m_enemyToPoolIndex[enemyIndex] = poolIndex;
    m_poolToEnemyIndex[poolIndex] = enemyIndex;
}
```

- [ ] **Step 6: Maintain `m_poolToEnemyIndex` in `Board.Tick`'s despawn loop**

Find this block in `Board.Tick`:

```csharp
for (int i = 0; i < removedEnemyCount; i++)
{
    int enemyIndex = removedEnemyIndices[i];
    int poolIndex = m_enemyToPoolIndex[enemyIndex];
    m_enemyPool[poolIndex].SetActive(false);

    m_enemyPoolUnusedIndices[m_enemyPoolUnusedIndicesCount++] = poolIndex;
}
```

Replace with:

```csharp
for (int i = 0; i < removedEnemyCount; i++)
{
    int enemyIndex = removedEnemyIndices[i];
    int poolIndex = m_enemyToPoolIndex[enemyIndex];
    m_enemyPool[poolIndex].SetActive(false);

    m_enemyPoolUnusedIndices[m_enemyPoolUnusedIndicesCount++] = poolIndex;
    m_poolToEnemyIndex[poolIndex] = -1;
}
```

- [ ] **Step 7: Maintain `m_poolToEnemyIndex` in `Board.Show`**

Find this block in `Show`:

```csharp
for (int enemyIdx = 0; enemyIdx < gameData.AliveEnemyCount; enemyIdx++)
{
    int enemyType = gameData.EnemyType[enemyIdx];

    int poolIndex = getFreeEnemyPoolIndex(enemyType);
    m_enemyPool[poolIndex].SetActive(true);
    m_enemyToPoolIndex[enemyIdx] = poolIndex;
}
```

Replace with:

```csharp
for (int enemyIdx = 0; enemyIdx < gameData.AliveEnemyCount; enemyIdx++)
{
    int enemyType = gameData.EnemyType[enemyIdx];

    int poolIndex = getFreeEnemyPoolIndex(enemyType);
    m_enemyPool[poolIndex].SetActive(true);
    m_enemyToPoolIndex[enemyIdx] = poolIndex;
    m_poolToEnemyIndex[poolIndex] = enemyIdx;
}
```

- [ ] **Step 8: Reset `m_enemyTransforms` and `m_poolToEnemyIndex` in `Board.Hide`**

Find `Board.Hide`:

```csharp
public void Hide()
{
    for (int enemyIdx = 0; enemyIdx < m_enemyPoolCount; enemyIdx++)
    {
        Debug.Log("HIDE() m_enemyPool[" + enemyIdx + "] " + m_enemyPool[enemyIdx].name);
        m_enemyPool[enemyIdx].SetActive(false);
        GameObject.Destroy(m_enemyPool[enemyIdx]);
        m_enemyPool[enemyIdx] = null;
        m_enemyPoolType[enemyIdx] = -1;
    }
    m_enemyPoolCount = 0;
    m_enemyPoolUnusedIndicesCount = 0;

    m_player.SetActive(false);

    hideUI();
}
```

Replace with:

```csharp
public void Hide()
{
    for (int enemyIdx = 0; enemyIdx < m_enemyPoolCount; enemyIdx++)
    {
        Debug.Log("HIDE() m_enemyPool[" + enemyIdx + "] " + m_enemyPool[enemyIdx].name);
        m_enemyPool[enemyIdx].SetActive(false);
        GameObject.Destroy(m_enemyPool[enemyIdx]);
        m_enemyPool[enemyIdx] = null;
        m_enemyPoolType[enemyIdx] = -1;
    }
    m_enemyPoolCount = 0;
    m_enemyPoolUnusedIndicesCount = 0;

    if (m_enemyTransforms.isCreated) m_enemyTransforms.Dispose();
    m_enemyTransforms = new TransformAccessArray(MaxEnemyPoolSize);
    for (int i = 0; i < MaxEnemyPoolSize; i++) m_poolToEnemyIndex[i] = -1;

    m_player.SetActive(false);

    hideUI();
}
```

(We dispose and recreate `m_enemyTransforms` rather than trying to remove entries one-by-one. It's a fresh start on each `Hide`/`Show` cycle.)

- [ ] **Step 9: Compile check**

```bash
dotnet build Chapter-12-only-move.sln
```

Expected: `Build succeeded.`

- [ ] **Step 10: Unity Editor compile + playmode smoke test**

Switch to Editor, confirm no Console errors. Play → new game → verify nothing is visually broken (behavior unchanged — bookkeeping is dormant). Exit → confirm no `Leak Detected` warnings.

- [ ] **Step 11: Commit**

```bash
git add Assets/Scripts/Board.cs
git commit -m "Add TransformAccessArray and PoolToEnemyIndex bookkeeping on Board"
```

---

### Task 4: Add `MoveEnemiesJob` struct to `Logic.cs` (unused)

**Files:**
- Modify: `Assets/Scripts/Logic.cs`

- [ ] **Step 1: Add job-related `using` statements**

In `Assets/Scripts/Logic.cs`, after the existing `using` lines, add:

```csharp
using Unity.Burst;
using Unity.Jobs;
```

- [ ] **Step 2: Add the `MoveEnemiesJob` struct**

Add this struct **inside** the `Survivor` namespace, outside the `Logic` class (i.e. sibling to `Logic`), at the bottom of the file before the closing namespace brace:

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

**Why `[NativeDisableParallelForRestriction]`:** the job writes `EnemyPosition[enemyIndex]`, where `enemyIndex` comes from `AliveEnemyIndices[i]` — not directly from the loop index `i`. The safety system can't prove there are no overlapping writes, so it would block the schedule. We know it's safe because `AliveEnemyIndices[0..AliveEnemyCount-1]` contains unique values by construction (stack-style add, filter-remove). No two iterations write to the same slot.

- [ ] **Step 3: Compile check**

```bash
dotnet build Chapter-12-only-move.sln
```

Expected: `Build succeeded.`

- [ ] **Step 4: Unity Editor compile**

Switch to Editor, confirm no Console errors. Open `Jobs → Burst → Open Inspector`. Expected: `Survivor.MoveEnemiesJob` appears in the list (this confirms Burst picked it up).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Logic.cs
git commit -m "Add MoveEnemiesJob Burst struct"
```

---

### Task 5: Replace `moveEnemies` call in `Logic.Tick` with a scheduled `MoveEnemiesJob`

**Files:**
- Modify: `Assets/Scripts/Logic.cs`

- [ ] **Step 1: Update the call site in `Logic.Tick`**

Find this line in `Logic.Tick`:

```csharp
moveEnemies(gameData, balance, dt);
```

Replace with:

```csharp
MoveEnemiesJob moveJob = new MoveEnemiesJob
{
    AliveEnemyIndices = gameData.AliveEnemyIndices,
    EnemyType         = gameData.EnemyType,
    EnemyVelocity     = gameData.EnemyVelocityNative,
    EnemyPosition     = gameData.EnemyPosition,
    Dt                = dt,
};
JobHandle moveHandle = moveJob.Schedule(gameData.AliveEnemyCount, 64);
moveHandle.Complete();
```

- [ ] **Step 2: Delete the now-unused `moveEnemies` static helper**

Remove the entire `moveEnemies` method from `Logic.cs` (the one that starts with `static void moveEnemies(GameData gameData, Balance balance, float dt)` — including its for-loop body, closing braces). Its behavior is now inlined above as the job schedule.

- [ ] **Step 3: Compile check**

```bash
dotnet build Chapter-12-only-move.sln
```

Expected: `Build succeeded.` If you get "`moveEnemies` is not defined" — you removed the helper but a call to it remained. Grep the file for `moveEnemies(` — it should only appear in the class name `MoveEnemiesJob`.

- [ ] **Step 4: Unity Editor compile + playmode test**

Switch to Editor. Confirm no Console errors. Enter Play → start a new game.

Expected: enemies move identically to before — same direction (inward toward origin), same speeds per type. If they stand still, `EnemyVelocityNative` wasn't populated (see Task 2, Step 2). If they move in wrong directions, `math.normalizesafe` sign might be inverted — check the job body matches the step-2 code exactly.

Exit Play → confirm no `Leak Detected` warnings.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Logic.cs
git commit -m "Schedule MoveEnemiesJob in Logic.Tick"
```

---

### Task 6: Add `SyncEnemyTransformsJob` struct to `Logic.cs` (unused)

**Files:**
- Modify: `Assets/Scripts/Logic.cs`

- [ ] **Step 1: Add `using UnityEngine.Jobs;`**

At the top of `Logic.cs`, add:

```csharp
using UnityEngine.Jobs;
```

(This namespace contains `IJobParallelForTransform` and `TransformAccess`. It's distinct from `Unity.Jobs`.)

- [ ] **Step 2: Add the `SyncEnemyTransformsJob` struct**

Inside the `Survivor` namespace, at the bottom of the file (next to `MoveEnemiesJob`):

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

- [ ] **Step 3: Compile check**

```bash
dotnet build Chapter-12-only-move.sln
```

Expected: `Build succeeded.`

- [ ] **Step 4: Unity Editor compile**

Switch to Editor. Open `Jobs → Burst → Open Inspector`. Expected: `Survivor.SyncEnemyTransformsJob` appears alongside `MoveEnemiesJob`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Logic.cs
git commit -m "Add SyncEnemyTransformsJob Burst struct"
```

---

### Task 7: Schedule `SyncEnemyTransformsJob` in `Logic.Tick`; remove Board's transform-sync loop

This task wires the sync job and removes the main-thread loop it replaces. Afterwards, transform writes happen in parallel via Burst + `TransformAccessArray`, completing the feature.

**Files:**
- Modify: `Assets/Scripts/Logic.cs`
- Modify: `Assets/Scripts/Board.cs`

- [ ] **Step 1: Add `using UnityEngine.Jobs;` at the top of `Logic.cs`** *(already done in Task 6 — verify it's there; skip if present).*

- [ ] **Step 2: Extend `Logic.Tick`'s signature**

Find the current signature of `Logic.Tick`:

```csharp
public static void Tick(
    MetaData metaData,
    GameData gameData,
    Balance balance,
    float dt,
    out bool gameOver,
    Span<int> addedEnemyIndices,
    ref int addedEnemyCount,
    Span<int> removedEnemyIndices,
    ref int removedEnemyCount
    )
```

Add two parameters at the end:

```csharp
public static void Tick(
    MetaData metaData,
    GameData gameData,
    Balance balance,
    float dt,
    out bool gameOver,
    Span<int> addedEnemyIndices,
    ref int addedEnemyCount,
    Span<int> removedEnemyIndices,
    ref int removedEnemyCount,
    TransformAccessArray enemyTransforms,
    NativeArray<int>     poolToEnemyIndex
    )
```

- [ ] **Step 3: Schedule the sync job at the end of `Logic.Tick`, before `gameOver` is computed**

Find the current end of `Logic.Tick`:

```csharp
movePlayer(gameData, balance, dt);

gameOver = false;//checkGameOver(metaData, gameData, balance);
```

Replace with:

```csharp
movePlayer(gameData, balance, dt);

SyncEnemyTransformsJob syncJob = new SyncEnemyTransformsJob
{
    EnemyPosition    = gameData.EnemyPosition,
    PoolToEnemyIndex = poolToEnemyIndex,
};
JobHandle syncHandle = syncJob.Schedule(enemyTransforms);
syncHandle.Complete();

gameOver = false;//checkGameOver(metaData, gameData, balance);
```

- [ ] **Step 4: Pass the new arguments from `Board.Tick` into `Logic.Tick`**

In `Assets/Scripts/Board.cs`, find the current `Logic.Tick` call:

```csharp
Logic.Tick(
    metaData,
    gameData,
    balance,
    dt,
    out isGameOver,
    addedEnemyIndices,
    ref addedEnemyCount,
    removedEnemyIndices,
    ref removedEnemyCount
    );
```

Replace with:

```csharp
Logic.Tick(
    metaData,
    gameData,
    balance,
    dt,
    out isGameOver,
    addedEnemyIndices,
    ref addedEnemyCount,
    removedEnemyIndices,
    ref removedEnemyCount,
    m_enemyTransforms,
    m_poolToEnemyIndex
    );
```

- [ ] **Step 5: Remove the main-thread transform-sync loop in `Board.Tick`**

In `Board.Tick`, find this block (which Task 2 had updated to use `float2`):

```csharp
for (int i = 0; i < gameData.AliveEnemyCount; i++)
{
    int enemyIndex = gameData.AliveEnemyIndices[i];
    int poolIndex = m_enemyToPoolIndex[enemyIndex];
    float2 p = gameData.EnemyPosition[enemyIndex];
    m_enemyPool[poolIndex].transform.localPosition = new Vector3(p.x, p.y, 0f);
}
```

**Delete this block entirely.** The parallel job now handles it.

- [ ] **Step 6: Compile check**

```bash
dotnet build Chapter-12-only-move.sln
```

Expected: `Build succeeded.`

- [ ] **Step 7: Unity Editor compile + playmode test**

Switch to Editor. Confirm no Console errors.

Play → start a new game. Verify:
- **Enemy positions match the pre-conversion build.** Movement, spawn, out-of-bounds, and collision all look identical to before.
- **Transforms update every frame** — enemies don't appear frozen.
- **Player input circle works** — drag moves the world (via `movePlayer` main-thread loop still writing positions, which the sync job then reads).
- **Pause/resume** via the pause button works; no position corruption.
- **Exit Play cleanly** → no `Leak Detected` warnings in the Console.

If enemies appear frozen on screen, `m_enemyTransforms` is probably empty because spawn-path `.Add(transform)` isn't firing. Set a breakpoint or temporary `Debug.Log` in `getFreeEnemyPoolIndex` to confirm the `.Add` line runs on first spawn of each pool slot.

- [ ] **Step 8: Burst verification**

Open `Jobs → Burst → Open Inspector`. Both `Survivor.MoveEnemiesJob` and `Survivor.SyncEnemyTransformsJob` should show as compiled. Selecting each reveals the generated assembly — not required to read, just confirming Burst is active.

- [ ] **Step 9: Commit**

```bash
git add Assets/Scripts/Logic.cs Assets/Scripts/Board.cs
git commit -m "Schedule SyncEnemyTransformsJob; remove main-thread transform loop"
```

---

## Self-Review

- **Spec coverage:**
  - Packages added → Task 1 ✓
  - NativeArray conversion of `EnemyPosition`/`EnemyType`/`AliveEnemyIndices`/`DeadEnemyIndices` → Task 2 ✓
  - `EnemyVelocityNative` mirror → Task 2 step 2 ✓
  - `FreeGameData` + `Game.OnDestroy` → Task 2 steps 3, 12 ✓
  - `GameDataIO` NativeArray assignment pattern fix → Task 2 step 11 ✓
  - `m_enemyTransforms` + `m_poolToEnemyIndex` on Board, init/destroy/show/hide maintenance → Task 3 ✓
  - Spawn/despawn maintenance → Task 3 steps 5, 6 ✓
  - `MoveEnemiesJob` + Burst schedule → Tasks 4, 5 ✓
  - `SyncEnemyTransformsJob` + `TransformAccessArray` schedule → Tasks 6, 7 ✓
  - Removal of Board's main-thread transform-sync loop → Task 7 step 5 ✓
  - Testing approach → "Project conventions" header + per-task smoke tests ✓

- **Placeholder scan:** No TBDs, no "handle edge cases" hand-waves. Every code step shows final code.

- **Type consistency:** Job struct field names (`AliveEnemyIndices`, `EnemyType`, `EnemyVelocity`, `EnemyPosition`, `Dt`, `PoolToEnemyIndex`) are identical between Task 4 / Task 6 (definition) and Task 5 / Task 7 (instantiation). `FreeGameData` disposes exactly the 5 fields that `AllocateGameData` creates. `Logic.Tick`'s new parameters (`TransformAccessArray`, `NativeArray<int>`) match the types of the Board fields passed in.
