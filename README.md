# Chapter 12 — "Only Move" experiments

A companion/sandbox project to **Chapter-12** of *Data-Oriented Design for Games* by Nitzan Wilnai (Manning).

Where the main **Chapter-12** project jobifies the *entire* simulation, this project isolates a **single step — moving the enemies — and uses separate branches to compare different ways of parallelizing or accelerating just that step.** It's the R&D behind the final Chapter-12 implementation, and a clean way to benchmark each technique against the same baseline.

All branches are the same game; they differ only in how enemy movement (and, in some cases, how the resulting positions are written back to the enemy transforms) is implemented. Use the on-screen FPS / perf overlay (`Tools/FPSCounter.cs`) to compare them under load.

## Branches

| Branch | Technique | What it does |
|---|---|---|
| `main` | **Baseline** | Plain C# scalar `for` loop on the main thread — no Burst, no jobs. Enemy positions and the GameObject transforms are both updated on the main thread. The reference to measure everything else against. |
| `feature/burst-only-move-enemies` | **Burst, single-threaded** | Moves enemies in a `[BurstCompile]`d static method (`moveEnemies`). Still one thread, but compiled to native/SIMD code — isolates the speedup from **Burst alone**, without the Job System. |
| `feature/move-enemies-job` | **Job System + Burst** | Moves enemies with a Burst-compiled `MoveEnemiesJob : IJobParallelFor` scheduled across worker threads (`Schedule(count, 64)`). Transforms are still written on the main thread. Adds **multithreading** on top of Burst. |
| `feature/sync-transforms-job` | **Jobs + parallel transform writes** | Builds on the job-based move and *also* pushes the new positions onto the enemy GameObject transforms in parallel via `SyncEnemyTransformsJob : IJobParallelForTransform` over a `TransformAccessArray`, removing the main-thread transform loop. The most complete jobs-based version, and the current default branch. |
| `feature/move-enemies-ecs` | **DOTS / ECS** | Reimplements enemies as ECS entities (`Unity.Entities`, `EnemyComponents.cs` with `EnemyMoveSpeed : IComponentData`) updated by an ECS system — a different *architecture* rather than bolting jobs onto the existing GameObject pool. |

## Progression

Roughly increasing sophistication, each step adding one technique:

```
main (scalar, main thread)
  └─ burst-only-move-enemies     + Burst compilation
       └─ move-enemies-job       + C# Job System (multithreaded)
            └─ sync-transforms-job  + parallel transform writes (TransformAccessArray)

move-enemies-ecs                 alternative paradigm: full DOTS / ECS
```

## Running

- Unity **2022.3.62f2**.
- Open `Assets/Scenes/MainGameScene.unity` and press **Play**.
- Switch techniques with `git checkout <branch>`, then re-open / Play and watch the perf overlay to compare.
- After editing any `*SO` asset under `Assets/Data/`, run the editor menu **DOD ▸ Balance ▸ Parse Local** to regenerate `Assets/Resources/balance.bytes`.
- The `feature/move-enemies-ecs` branch additionally relies on the Entities package (already in its manifest).

## Note

The full, finished chapter — with the **entire** simulation (move, out-of-bounds, collision, player move) jobified and Burst-compiled plus parallel transform sync — lives in the sibling **Chapter-12** project. This "only-move" project deliberately limits the scope to the movement step so each technique can be compared in isolation.
