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
