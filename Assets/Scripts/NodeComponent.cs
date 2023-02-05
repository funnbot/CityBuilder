using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public struct NodeComponent : IComponentData
{
    public Entity entity;
    public float3 position;
}
