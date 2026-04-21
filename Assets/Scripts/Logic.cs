using UnityEngine;
using System;

namespace Survivor
{
    public static class Logic
    {
        public static void AllocateGameData(GameData gameData, Balance balance)
        {
            gameData.EnemyPosition = new Vector2[balance.MaxEnemies];
            gameData.EnemyType = new int[balance.MaxEnemies];

            gameData.AliveEnemyIndices = new int[balance.MaxEnemies];
            gameData.DeadEnemyIndices = new int[balance.MaxEnemies];
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
            // Debug.Log("Spawning enemy");
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
            gameData.EnemyPosition[enemyIndex] = direction.normalized * balance.SpawnRadius;
            gameData.EnemyType[enemyIndex] = getRandomEnemyTypeByWeight(balance); ;
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

        static void removeEnemyArrayCopy(GameData gameData, int enemyIndex)
        {
            int index = -1;
            // parse through the array, only re-store values that are not value
            for (int i = 0; i < gameData.AliveEnemyCount; i++)
                if (gameData.AliveEnemyIndices[i] == enemyIndex)
                {
                    index = i;
                    break;
                }

            if (index > -1)
                Array.Copy(gameData.AliveEnemyIndices, index + 1, gameData.AliveEnemyIndices, index, gameData.AliveEnemyCount - index - 1);

            gameData.AliveEnemyCount--;
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

            moveEnemies(gameData, balance, dt);

            checkEnemyOutOfBounds(gameData, balance, removedEnemyIndices, ref removedEnemyCount);

            doEemyToEnemyCollision(gameData, balance);

            movePlayer(gameData, balance, dt);

            gameOver = false;//checkGameOver(metaData, gameData, balance);
        }

        static void moveEnemies(GameData gameData, Balance balance, float dt)
        {
            for (int i = 0; i < gameData.AliveEnemyCount; i++)
            {
                int enemyIndex = gameData.AliveEnemyIndices[i];
                Vector2 dir = -gameData.EnemyPosition[enemyIndex].normalized;
                int enemyType = gameData.EnemyType[enemyIndex];
                gameData.EnemyPosition[enemyIndex] = gameData.EnemyPosition[enemyIndex] + dir * balance.EnemyVelocity[enemyType] * dt;
            }
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
                    Vector2 diff = gameData.EnemyPosition[enemyIndex1] - gameData.EnemyPosition[enemyIndex2];
                    float distance = radius1 + balance.EnemyRadius[gameData.EnemyType[enemyIndex2]];
                    float distanceSqr = distance * distance;
                    if (diff.sqrMagnitude <= distanceSqr)
                    {
                        Vector2 diffNormalized = diff.normalized;
                        Vector2 midPoint = (gameData.EnemyPosition[enemyIndex1] + gameData.EnemyPosition[enemyIndex2]) / 2.0f;
                        float halfTotalRadius = (balance.EnemyRadius[gameData.EnemyType[enemyIndex1]] + balance.EnemyRadius[gameData.EnemyType[enemyIndex2]]) / 2.0f;
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
                if (gameData.EnemyPosition[enemyIndex].sqrMagnitude > distanceSqr)
                    removeEnemy(gameData, enemyIndex, removedEnemyIndices, ref removedEnemyCount);
            }
        }

        static void movePlayer(GameData gameData, Balance balance, float dt)
        {
            Vector2 playerPosition = gameData.PlayerDirection * balance.PlayerVelocity * dt;
            for (int i = 0; i < gameData.AliveEnemyCount; i++)
            {
                int enemyIndex = gameData.AliveEnemyIndices[i];
                gameData.EnemyPosition[enemyIndex] -= playerPosition;
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
                if (gameData.EnemyPosition[enemyIndex].magnitude < balance.PlayerRadius)
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