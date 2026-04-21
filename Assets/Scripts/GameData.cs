using UnityEngine;
using System.IO;
using System;

namespace Survivor
{
public class GameData
{
    public bool InGame;

    public int[] AliveEnemyIndices;
    public int AliveEnemyCount;
    public int[] DeadEnemyIndices;
    public int DeadEnemyCount;

    public float SpawnTime;

    public Vector2[] EnemyPosition;
    public int[] EnemyType;

    public Vector2 PlayerDirection;

    public float GameTime;
}
}