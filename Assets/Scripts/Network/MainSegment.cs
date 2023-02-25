using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;
using UnityEngine.Assertions;

using static Unity.Collections.NativeArrayOptions;
using static Unity.Collections.Allocator;

// main node, points to end of segment
public partial struct MainSegment : IComponentData
{
    public float3 position;
    public float2 controlPoint;
    public Entity endNode;
}

// list of start nodes using this node as the end of their segment
[InternalBufferCapacity(3)]
public partial struct SharedSegment : IBufferElementData
{
    public Entity startNode;
}

[BurstCompile]
public partial struct SpatialChunk : ISharedComponentData
{
    private int2 index;
}

[BurstCompile]
public partial struct SpatialChunkNeedsMeshRebuild : IComponentData
{
    public bool value;
}

[BurstCompile]
public struct RebuildSegmentQuery
{
    private EntityQuery query;
    private SegmentAspect.TypeHandle handle;

    public RebuildSegmentQuery(ref SystemState state)
    {
        query = new EntityQueryBuilder(Allocator.Temp).WithAspectRO<SegmentAspect>().Build(state.EntityManager);
        query.AddChangedVersionFilter(ComponentType.ReadOnly<MainSegment>());
        handle = new(ref state, isReadOnly: true); 
    }

    [BurstCompile]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AnyToRebuild() => query.CalculateChunkCount() > 0;

    [BurstCompile]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Update(ref SystemState state) => handle.Update(ref state);

    [BurstCompile]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SegmentAspect.Enumerator Query() => SegmentAspect.Query(query, handle);
}

[BurstCompile]
public readonly partial struct SegmentAspect : IAspect
{
    private readonly Entity self;
    private readonly RefRW<MainSegment> mainSegment;
    private readonly DynamicBuffer<SharedSegment> sharedSegments;

    public float3 Position { get => mainSegment.ValueRO.position; set => mainSegment.ValueRW.position = value; }
    public Entity MainEndNode => mainSegment.ValueRO.endNode;
    public float2 ControlPoint { get => mainSegment.ValueRO.controlPoint; set => mainSegment.ValueRW.controlPoint = value; }
    public int SharedSegmentCount => sharedSegments.Length;
    [BurstCompile]
    public Entity GetSharedSegment(int index) => sharedSegments[index].startNode;

    [BurstCompile]
    public void ConnectMainSegment(SegmentAspect end)
    {
        Assert.IsTrue(MainEndNode.IsNull());
        mainSegment.ValueRW.endNode = end.self;
        _ = end.sharedSegments.Add(new SharedSegment { startNode = self });
    }

    [BurstCompile]
    public void RemoveMainSegment()
    {
        Assert.IsTrue(!MainEndNode.IsNull());
        for (int i = 0; i < sharedSegments.Length; ++i)
        {
            if (sharedSegments[i].startNode == mainSegment.ValueRO.endNode)
            {
                sharedSegments.RemoveAtSwapBack(i);
                break;
            }
        }
        mainSegment.ValueRW.endNode = Entity.Null;
    }

    public static Entity CreateSegment(WorldUnmanaged world)
    {
        Entity entity = world.EntityManager.CreateEntity(typeof(MainSegment));
        _ = world.EntityManager.AddBuffer<SharedSegment>(entity);
        _ = world.EntityManager.AddChunkComponentData<SpatialChunkNeedsMeshRebuild>(entity);
        return entity;
    }
}

public partial struct NetworkMesh : IComponentData
{
    public NetworkMeshBuilder stream;
}

public partial struct Modified : IComponentData, IEnableableComponent { }

[BurstCompile]
[UpdateBefore(typeof(ApplyMeshDataArray))]
[CreateBefore(typeof(ApplyMeshDataArray))]
public partial struct AllocateMeshDataArray : ISystem
{
    private RebuildSegmentQuery query;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _ = state.EntityManager.CreateSingleton(new NetworkMesh { stream = new(new AABB { Center = float3.zero, Extents = new float3(1, 1, 1) }) }, "NetworkMesh Singleton");
        query = new(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        var mesh = SystemAPI.GetSingletonRW<NetworkMesh>();
        // mesh.ValueRW.stream.Dispose();
        state.EntityManager.DestroyEntity(SystemAPI.GetSingletonEntity<NetworkMesh>());
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var singlet = SystemAPI.GetSingletonRW<NetworkMesh>();
        ref var builder = ref singlet.ValueRW.stream;

        bool needAllocate = true;

        query.Update(ref state);
        foreach (SegmentAspect segment in query.Query())
        {
            if (!segment.MainEndNode.IsNull())
            {
                if (needAllocate)
                {
                    builder.Allocate(16);
                    needAllocate = false;
                }

                SegmentAspect end = SystemAPI.GetAspectRO<SegmentAspect>(segment.MainEndNode);
                builder.AddSegment(segment, end);
            }
        }
    }
}

[CreateAfter(typeof(AllocateMeshDataArray))]
[UpdateAfter(typeof(AllocateMeshDataArray))]
public partial class ApplyMeshDataArray : SystemBase
{
    private NetworkMeshBuilder.DataArray meshData;
    private Mesh mesh;
    private Material mat;

    public Entity CreateMeshRenderer()
    {
        Entity entity = EntityManager.CreateEntity(typeof(Static));
        _ = EntityManager.AddComponentData(entity, new LocalTransform { Position = float3.zero, Rotation = quaternion.identity, Scale = 1 });
        RenderMeshArray renderMeshArray = new(new[] { mat }, new[] { mesh });
        RenderMeshUtility.AddComponents(entity,
            EntityManager,
            new RenderMeshDescription(UnityEngine.Rendering.ShadowCastingMode.On),
            renderMeshArray,
            MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
        return entity;
    }

    private Entity AddSegment(Entity start, float3 pos)
    {
        Entity e = SegmentAspect.CreateSegment(World.Unmanaged);
        SegmentAspect segment = SystemAPI.GetAspectRW<SegmentAspect>(e);
        segment.Position = pos;
        SegmentAspect startSegment = SystemAPI.GetAspectRW<SegmentAspect>(start);
        startSegment.ConnectMainSegment(segment);
        return e;
    }

    protected override void OnCreate()
    {
        mesh = new Mesh();
        mesh.MarkDynamic();

        meshData = new(Allocator.Persistent);

        mat = Addressables.LoadAssetAsync<Material>("Assets/DefaultLit.mat").WaitForCompletion();
        Entity renderer = CreateMeshRenderer();
        _ = EntityManager.AddComponentData<LocalTransform>(renderer, new LocalTransform { Position = float3.zero, Rotation = quaternion.identity, Scale = 1 });

        Entity a = SegmentAspect.CreateSegment(World.Unmanaged);
        SegmentAspect segment = SystemAPI.GetAspectRW<SegmentAspect>(a);
        segment.Position = new float3(0, 0.1f, 1);
        Entity b = AddSegment(a, new float3(0, 0.1f, 2));
        Entity c = AddSegment(b, new float3(1, 0.1f, 3));
        _ = AddSegment(c, new float3(1, 0.1f, 5));
    }

    protected override void OnDestroy()
    {
        // meshData.Dispose();
    }

    protected override void OnUpdate()
    {
        var singlet = SystemAPI.GetSingletonRW<NetworkMesh>();
        if (singlet.ValueRO.stream.IsEmpty)
        {
            meshData.ApplyAndResetBuilder(ref singlet.ValueRW.stream, mesh);
        }
    }
}
