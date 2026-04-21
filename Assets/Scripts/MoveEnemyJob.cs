using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Survivor
{
    [BurstCompile]
    partial struct MoveEnemyJob : IJobEntity
    {
        public float Dt;

        void Execute(ref LocalTransform transform, in EnemyMoveSpeed speed)
        {
            float2 pos2   = new float2(transform.Position.x, transform.Position.y);
            float2 dir    = -math.normalizesafe(pos2);
            float2 newPos = pos2 + dir * speed.Value * Dt;
            transform.Position = new float3(newPos.x, newPos.y, 0f);
        }
    }
}
