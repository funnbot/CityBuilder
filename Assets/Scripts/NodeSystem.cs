using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

[BurstCompile]
public partial struct NodeSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        
    }

    public void OnDestroy(ref SystemState state)
    {
        
    }
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (RefRW<NodeComponent> node in SystemAPI.Query<RefRW<NodeComponent>>())
        {
            Entity newEntity = state.EntityManager.Instantiate(node.ValueRO.entity);
        }
    }
}