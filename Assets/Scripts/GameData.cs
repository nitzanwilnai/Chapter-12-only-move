using UnityEngine;
using Unity.Collections;
using Unity.Entities;

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

        public NativeArray<Entity> EnemyEntity;
        public NativeArray<int>    EnemyType;

        public World EcsWorld;

        public Vector2 PlayerDirection;

        public float GameTime;
    }
}
