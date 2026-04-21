using UnityEngine;
using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Survivor
{
    public static class Logic
    {
        public static void AllocateGameData(GameData gameData, Balance balance)
        {
            gameData.EnemyEntity       = new NativeArray<Entity>(balance.MaxEnemies, Allocator.Persistent);
            gameData.EnemyType         = new NativeArray<int>(balance.MaxEnemies, Allocator.Persistent);
            gameData.AliveEnemyIndices = new NativeArray<int>(balance.MaxEnemies, Allocator.Persistent);
            gameData.DeadEnemyIndices  = new NativeArray<int>(balance.MaxEnemies, Allocator.Persistent);

            gameData.EcsWorld = new World("EnemyWorld");
        }

        public static void FreeGameData(GameData gameData)
        {
            if (gameData.EnemyEntity.IsCreated)       gameData.EnemyEntity.Dispose();
            if (gameData.EnemyType.IsCreated)         gameData.EnemyType.Dispose();
            if (gameData.AliveEnemyIndices.IsCreated) gameData.AliveEnemyIndices.Dispose();
            if (gameData.DeadEnemyIndices.IsCreated)  gameData.DeadEnemyIndices.Dispose();

            if (gameData.EcsWorld != null && gameData.EcsWorld.IsCreated)
            {
                gameData.EcsWorld.Dispose();
                gameData.EcsWorld = null;
            }
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

            destroyAllEnemyEntities(gameData, balance);
        }

        static void destroyAllEnemyEntities(GameData gameData, Balance balance)
        {
            EntityManager em = gameData.EcsWorld.EntityManager;
            for (int i = 0; i < balance.MaxEnemies; i++)
            {
                Entity e = gameData.EnemyEntity[i];
                if (e != Entity.Null && em.Exists(e))
                    em.DestroyEntity(e);
                gameData.EnemyEntity[i] = Entity.Null;
            }
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
            int enemyType = getRandomEnemyTypeByWeight(balance);
            gameData.EnemyType[enemyIndex] = enemyType;

            EntityManager em = gameData.EcsWorld.EntityManager;
            Entity e = em.CreateEntity();
            em.AddComponentData(e, LocalTransform.FromPosition(new float3(spawnPos.x, spawnPos.y, 0f)));
            em.AddComponentData(e, new EnemyMoveSpeed { Value = balance.EnemyVelocity[enemyType] });
            gameData.EnemyEntity[enemyIndex] = e;
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

            EntityManager em = gameData.EcsWorld.EntityManager;
            Entity e = gameData.EnemyEntity[enemyIndex];
            if (e != Entity.Null && em.Exists(e))
                em.DestroyEntity(e);
            gameData.EnemyEntity[enemyIndex] = Entity.Null;
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

            moveEnemies(gameData, dt);

            checkEnemyOutOfBounds(gameData, balance, removedEnemyIndices, ref removedEnemyCount);

            doEemyToEnemyCollision(gameData, balance);

            movePlayer(gameData, balance, dt);

            gameOver = false;//checkGameOver(metaData, gameData, balance);
        }

        static void moveEnemies(GameData gameData, float dt)
        {
            EntityManager em = gameData.EcsWorld.EntityManager;
            for (int i = 0; i < gameData.AliveEnemyCount; i++)
            {
                int enemyIndex = gameData.AliveEnemyIndices[i];
                Entity e = gameData.EnemyEntity[enemyIndex];
                LocalTransform t = em.GetComponentData<LocalTransform>(e);
                float speed = em.GetComponentData<EnemyMoveSpeed>(e).Value;
                float2 pos2 = new float2(t.Position.x, t.Position.y);
                float2 dir = -math.normalizesafe(pos2);
                float2 newPos = pos2 + dir * speed * dt;
                t.Position = new float3(newPos.x, newPos.y, 0f);
                em.SetComponentData(e, t);
            }
        }

        static void doEemyToEnemyCollision(GameData gameData, Balance balance)
        {
            EntityManager em = gameData.EcsWorld.EntityManager;
            for (int i = 0; i < gameData.AliveEnemyCount; i++)
            {
                int enemyIndex1 = gameData.AliveEnemyIndices[i];
                Entity e1 = gameData.EnemyEntity[enemyIndex1];
                LocalTransform t1 = em.GetComponentData<LocalTransform>(e1);
                float radius1 = balance.EnemyRadius[gameData.EnemyType[enemyIndex1]];

                for (int j = i + 1; j < gameData.AliveEnemyCount; j++)
                {
                    int enemyIndex2 = gameData.AliveEnemyIndices[j];
                    Entity e2 = gameData.EnemyEntity[enemyIndex2];
                    LocalTransform t2 = em.GetComponentData<LocalTransform>(e2);

                    float2 pos1 = new float2(t1.Position.x, t1.Position.y);
                    float2 pos2 = new float2(t2.Position.x, t2.Position.y);
                    float2 diff = pos1 - pos2;
                    float distance = radius1 + balance.EnemyRadius[gameData.EnemyType[enemyIndex2]];
                    float distanceSqr = distance * distance;
                    if (math.lengthsq(diff) <= distanceSqr)
                    {
                        float2 diffNormalized = math.normalizesafe(diff);
                        float2 midPoint = (pos1 + pos2) * 0.5f;
                        float halfTotalRadius = (balance.EnemyRadius[gameData.EnemyType[enemyIndex1]] + balance.EnemyRadius[gameData.EnemyType[enemyIndex2]]) * 0.5f;
                        float2 new1 = midPoint + diffNormalized * halfTotalRadius;
                        float2 new2 = midPoint - diffNormalized * halfTotalRadius;
                        t1.Position = new float3(new1.x, new1.y, 0f);
                        t2.Position = new float3(new2.x, new2.y, 0f);
                        em.SetComponentData(e1, t1);
                        em.SetComponentData(e2, t2);
                    }
                }
            }
        }

        static void checkEnemyOutOfBounds(GameData gameData, Balance balance, Span<int> removedEnemyIndices, ref int removedEnemyCount)
        {
            EntityManager em = gameData.EcsWorld.EntityManager;
            float distanceSqr = balance.SpawnRadius * balance.SpawnRadius * 1.1f;
            for (int i = 0; i < gameData.AliveEnemyCount; i++)
            {
                int enemyIndex = gameData.AliveEnemyIndices[i];
                Entity e = gameData.EnemyEntity[enemyIndex];
                LocalTransform t = em.GetComponentData<LocalTransform>(e);
                float2 pos2 = new float2(t.Position.x, t.Position.y);
                if (math.lengthsq(pos2) > distanceSqr)
                    removeEnemy(gameData, enemyIndex, removedEnemyIndices, ref removedEnemyCount);
            }
        }

        static void movePlayer(GameData gameData, Balance balance, float dt)
        {
            Vector2 delta = gameData.PlayerDirection * balance.PlayerVelocity * dt;
            float2 playerDelta = new float2(delta.x, delta.y);
            EntityManager em = gameData.EcsWorld.EntityManager;
            for (int i = 0; i < gameData.AliveEnemyCount; i++)
            {
                int enemyIndex = gameData.AliveEnemyIndices[i];
                Entity e = gameData.EnemyEntity[enemyIndex];
                LocalTransform t = em.GetComponentData<LocalTransform>(e);
                t.Position = new float3(t.Position.x - playerDelta.x, t.Position.y - playerDelta.y, 0f);
                em.SetComponentData(e, t);
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
            EntityManager em = gameData.EcsWorld.EntityManager;
            for (int i = 0; i < gameData.AliveEnemyCount; i++)
            {
                int enemyIndex = gameData.AliveEnemyIndices[i];
                Entity e = gameData.EnemyEntity[enemyIndex];
                LocalTransform t = em.GetComponentData<LocalTransform>(e);
                float2 pos2 = new float2(t.Position.x, t.Position.y);
                if (math.length(pos2) < balance.PlayerRadius)
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
}
