using UnityEngine;
using System;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Jobs;

namespace Survivor
{
    public static class Logic
    {
        public static void AllocateGameData(GameData gameData, Balance balance)
        {
            gameData.EnemyPosition       = new NativeArray<float2>(balance.MaxEnemies, Allocator.Persistent);
            gameData.EnemyType           = new NativeArray<int>(balance.MaxEnemies, Allocator.Persistent);
            gameData.AliveEnemyIndices   = new NativeArray<int>(balance.MaxEnemies, Allocator.Persistent);
            gameData.DeadEnemyIndices    = new NativeArray<int>(balance.MaxEnemies, Allocator.Persistent);
            gameData.EnemyVelocityNative = new NativeArray<float>(balance.EnemyVelocity, Allocator.Persistent);
        }

        public static void FreeGameData(GameData gameData)
        {
            if (gameData.EnemyPosition.IsCreated)       gameData.EnemyPosition.Dispose();
            if (gameData.EnemyType.IsCreated)           gameData.EnemyType.Dispose();
            if (gameData.AliveEnemyIndices.IsCreated)   gameData.AliveEnemyIndices.Dispose();
            if (gameData.DeadEnemyIndices.IsCreated)    gameData.DeadEnemyIndices.Dispose();
            if (gameData.EnemyVelocityNative.IsCreated) gameData.EnemyVelocityNative.Dispose();
        }

        public static void Init(MetaData metaData)
        {
            metaData.MenuState = MENU_STATE.NONE;
        }

        public static void StartGame(GameData gameData, Balance balance)
        {
            gameData.InGame = true;

            gameData.GameTime = 0.0f;
            gameData.SpawnTime = 0.0f;

            gameData.PlayerDirection = Vector2.zero;

            for (int i = 0; i < balance.MaxEnemies; i++)
                gameData.DeadEnemyIndices[i] = balance.MaxEnemies - 1 - i;
            gameData.DeadEnemyCount = balance.MaxEnemies;
            gameData.AliveEnemyCount = 0;
        }

        static bool canSpawnEnemy(GameData gameData, Balance balance)
        {
            return gameData.DeadEnemyCount > 0 && gameData.AliveEnemyCount < balance.MaxEnemies;
        }

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

        private static int getRandomEnemyTypeByWeight(Balance balance)
        {
            int enemyType = 0;
            int totalWeight = 0;
            for (int spawnIdx = 0; spawnIdx < balance.SpawnDataID.Length; spawnIdx++)
            {
                totalWeight += balance.SpawnDataWeight[spawnIdx];
            }

            int randomWeight = UnityEngine.Random.Range(0, totalWeight);

            totalWeight = 0;
            for (int spawnIdx = 0; spawnIdx < balance.SpawnDataID.Length; spawnIdx++)
            {
                totalWeight += balance.SpawnDataWeight[spawnIdx];
                if (randomWeight < totalWeight)
                {
                    enemyType = balance.SpawnDataID[spawnIdx];
                    break;
                }
            }

            return enemyType;
        }

        static void removeEnemy(GameData gameData, int enemyIndex, Span<int> removedEnemyIndices, ref int removedEnemyCount)
        {
            Debug.LogFormat("Removing enemy {0}", enemyIndex);
            int count = 0;
            for (int i = 0; i < gameData.AliveEnemyCount; i++)
                if (gameData.AliveEnemyIndices[i] != enemyIndex)
                    gameData.AliveEnemyIndices[count++] = gameData.AliveEnemyIndices[i];
            gameData.AliveEnemyCount = count;

            gameData.DeadEnemyIndices[gameData.DeadEnemyCount++] = enemyIndex;
            removedEnemyIndices[removedEnemyCount++] = enemyIndex;
        }

        private const double DegToRad = Math.PI / 180.0d;
        private const double RadToDeg = 180.0d / Math.PI;

        public static Vector2 RotateVector(Vector2 a, double degrees)
        {
            double radians = degrees * DegToRad;
            double ca = Math.Cos(radians);
            double sa = Math.Sin(radians);
            a.x = (float)(ca * a.x - sa * a.y);
            a.y = (float)(sa * a.x + ca * a.y);
            return a;
        }

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
        {
            gameData.GameTime += dt;

            gameData.SpawnTime += dt;
            if (gameData.SpawnTime >= balance.SpawnTime)
            {
                gameData.SpawnTime -= balance.SpawnTime;
                if (canSpawnEnemy(gameData, balance))
                    spawnEnemy(gameData, balance, addedEnemyIndices, ref addedEnemyCount);
            }

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

            checkEnemyOutOfBounds(gameData, balance, removedEnemyIndices, ref removedEnemyCount);

            doEemyToEnemyCollision(gameData, balance);

            movePlayer(gameData, balance, dt);

            gameOver = false;//checkGameOver(metaData, gameData, balance);
        }

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

        static void movePlayer(GameData gameData, Balance balance, float dt)
        {
            Vector2 delta = gameData.PlayerDirection * balance.PlayerVelocity * dt;
            float2 playerDelta = new float2(delta.x, delta.y);
            for (int i = 0; i < gameData.AliveEnemyCount; i++)
            {
                int enemyIndex = gameData.AliveEnemyIndices[i];
                gameData.EnemyPosition[enemyIndex] -= playerDelta;
            }
        }

        public static void MouseMove(GameData gameData, Vector2 mouseDownPos, Vector2 mouseCurrentPos)
        {
            gameData.PlayerDirection = (mouseCurrentPos - mouseDownPos).normalized;
        }

        public static void MouseUp(GameData gameData)
        {
            gameData.PlayerDirection = Vector2.zero;
        }

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

        public static void SetMenuState(MetaData metaData, MENU_STATE newMenuState)
        {
            metaData.MenuState = newMenuState;
        }
    }

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
}