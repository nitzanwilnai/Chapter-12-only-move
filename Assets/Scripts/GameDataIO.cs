using System;
using System.IO;
using UnityEngine;
using UnityEngine.Assertions.Must;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Survivor
{
public static class GameDataIO
{
    public static void Save(GameData gameData, Balance balance)
    {
        Debug.LogFormat("SaveGame()");

        if (!Directory.Exists(Application.persistentDataPath + "/DODSurvivor"))
            Directory.CreateDirectory(Application.persistentDataPath + "/DODSurvivor");

        string fileName = Application.persistentDataPath + "/DODSurvivor/gamedata.dat";
        using (FileStream fs = File.Create(fileName))
        using (BinaryWriter bw = new BinaryWriter(fs))
        {
            int version = 1;
            bw.Write(version);

            bw.Write(gameData.InGame);

            bw.Write(gameData.AliveEnemyCount);
            for (int i = 0; i < gameData.AliveEnemyCount; i++)
                bw.Write(gameData.AliveEnemyIndices[i]);

            bw.Write(gameData.DeadEnemyCount);
            for (int i = 0; i < gameData.DeadEnemyCount; i++)
                bw.Write(gameData.DeadEnemyIndices[i]);

            bw.Write(balance.MaxEnemies);

            EntityManager em = gameData.EcsWorld.EntityManager;
            for (int i = 0; i < balance.MaxEnemies; i++)
            {
                Entity e = gameData.EnemyEntity[i];
                if (e != Entity.Null && em.Exists(e))
                {
                    LocalTransform t = em.GetComponentData<LocalTransform>(e);
                    bw.Write(t.Position.x);
                    bw.Write(t.Position.y);
                }
                else
                {
                    bw.Write(0f);
                    bw.Write(0f);
                }
            }

            for (int i = 0; i < balance.MaxEnemies; i++)
                bw.Write(gameData.EnemyType[i]);

            bw.Write(gameData.PlayerDirection.x);
            bw.Write(gameData.PlayerDirection.y);

            bw.Write(gameData.GameTime);

            bw.Write(balance.NumEnemyTypes);
            for (int enemyType = 0; enemyType < balance.NumEnemyTypes; enemyType++)
                bw.Write(balance.EnemyIDToName[enemyType]);
        }
    }

    public static void Load(GameData gameData, Balance balance)
    {
        string fileName = Application.persistentDataPath + "/DODSurvivor/gamedata.dat";
        if (File.Exists(fileName))
        {
            using (FileStream stream = File.Open(fileName, FileMode.Open))
            using (BinaryReader br = new BinaryReader(stream))
            {
                int version = br.ReadInt32();

                gameData.InGame = br.ReadBoolean();

                gameData.AliveEnemyCount = br.ReadInt32();
                for (int i = 0; i < gameData.AliveEnemyCount; i++)
                    gameData.AliveEnemyIndices[i] = br.ReadInt32();

                gameData.DeadEnemyCount = br.ReadInt32();
                for (int i = 0; i < gameData.DeadEnemyCount; i++)
                    gameData.DeadEnemyIndices[i] = br.ReadInt32();

                int numEnemies = br.ReadInt32();
                float2[] loadedPositions = new float2[numEnemies];
                for (int i = 0; i < numEnemies; i++)
                {
                    float x = br.ReadSingle();
                    float y = br.ReadSingle();
                    loadedPositions[i] = new float2(x, y);
                }

                for (int i = 0; i < numEnemies; i++)
                    gameData.EnemyType[i] = br.ReadInt32();

                gameData.PlayerDirection.x = br.ReadSingle();
                gameData.PlayerDirection.y = br.ReadSingle();

                gameData.GameTime = br.ReadSingle();

                int numEnemyTypes = br.ReadInt32();
                for (int enemyType = 0; enemyType < numEnemyTypes; enemyType++)
                {
                    string enemyIdentifier = br.ReadString();
                    int newType = balance.EnemyNameToID[enemyIdentifier];
                    if (newType != enemyType)
                    {
                        for (int i = 0; i < numEnemies; i++)
                        {
                            if (gameData.EnemyType[i] == enemyType)
                            {
                                gameData.EnemyType[i] = newType;
                            }
                        }
                    }
                }

                EntityManager em = gameData.EcsWorld.EntityManager;
                for (int i = 0; i < balance.MaxEnemies; i++)
                {
                    Entity existing = gameData.EnemyEntity[i];
                    if (existing != Entity.Null && em.Exists(existing))
                        em.DestroyEntity(existing);
                    gameData.EnemyEntity[i] = Entity.Null;
                }
                for (int i = 0; i < gameData.AliveEnemyCount; i++)
                {
                    int enemyIndex = gameData.AliveEnemyIndices[i];
                    int type = gameData.EnemyType[enemyIndex];
                    Entity e = em.CreateEntity();
                    em.AddComponentData(e, LocalTransform.FromPosition(new float3(loadedPositions[enemyIndex].x, loadedPositions[enemyIndex].y, 0f)));
                    em.AddComponentData(e, new EnemyMoveSpeed { Value = balance.EnemyVelocity[type] });
                    gameData.EnemyEntity[enemyIndex] = e;
                }
            }
        }
    }

        public static bool SaveGameExists()
        {
            bool inGame = false;
            string fileName = Application.persistentDataPath + "/DODSurvivor/gamedata.dat";
            if (File.Exists(fileName))
            {
                using (FileStream stream = File.Open(fileName, FileMode.Open))
                using (BinaryReader br = new BinaryReader(stream))
                {
                    int version = br.ReadInt32();

                    inGame = br.ReadBoolean();
                }
            }
            return inGame;
        }
    }
}