using System.Collections;
using System.Collections.Generic;
using UnityEngine.AddressableAssets;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;

class NodeAuthoring : MonoBehaviour
{
    public GameObject prefab;

    private void Start()
    {

    }
}

class NodeBaker : Baker<NodeAuthoring>
{
    public override void Bake(NodeAuthoring authoring)
    {
        AddComponent(new NodeComponent
        {
            entity = GetEntity(authoring.prefab),
            position = float3.zero
        });
    }
}
