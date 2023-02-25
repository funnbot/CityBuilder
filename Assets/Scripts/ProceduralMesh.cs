using JetBrains.Annotations;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Assertions;

using static UnityEngine.Rendering.VertexAttribute;
using static UnityEngine.Rendering.VertexAttributeFormat;
using UnityEngine.Splines;

[BurstCompile]
public struct Vertex
{
    public float3 position;
    public float3 normal;
    public float4 tangent;
    public float2 texCoord0; // UV0
}

public static partial class Util
{
    [BurstCompile]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ApproxEqual(float a, float b, float epsilon)
    {
        return math.abs(a - b) <= ((math.abs(a) < math.abs(b) ? math.abs(b) : math.abs(a)) * epsilon);
    }

    [BurstCompile]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Close(float3 a, float3 b, float epsilon)
    {
        return math.distancesq(a, b) <= (epsilon * epsilon);
    }
    [BurstCompile]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Close(float2 a, float2 b, float epsilon)
    {
        return math.distancesq(a, b) <= (epsilon * epsilon);
    }
    [BurstCompile]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Midpoint(float a, float b)
    {
        return (a + b) * 0.5f;
    }
    [BurstCompile]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float2 Midpoint(float2 a, float2 b)
    {
        return new float2(Midpoint(a.x, b.x), Midpoint(a.y, b.y));
    }
}

[BurstCompile]
public struct Bezier
{
    public float3 a;
    public float3 b;
    public float2 ctrl;

    [BurstCompile]
    public float3 Evaluate(float t)
    {
        float oneMinus = 1f - t;
        float A = oneMinus * oneMinus;
        float B = 2f * oneMinus * t;
        float C = t * t;

        return new float3(
                A * a.x + B * ctrl.x + C * b.x,
                (a.y + b.y) * 0.5f, // just the midpoint
                A * a.z + B * ctrl.y + C * b.z
            );
    }
}

public interface IMeshBuilder
{
    public int AddVertex(Vertex vertex);
    public void AddTriIndex(int3 triangle);
}

[BurstCompile]
public struct NetworkMeshBuilder : IMeshBuilder, IDisposable
{
    private static readonly Allocator dataAlloc = Allocator.TempJob;

    public struct DataArray : IDisposable
    {
        private static readonly MeshUpdateFlags updateFlags = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontResetBoneBounds | MeshUpdateFlags.DontNotifyMeshUsers;

        private NativeArray<VertexAttributeDescriptor> vertexParams;
        private NativeArray<SubMeshDescriptor> submeshParams;

        private NativeArray<VStream> convertedVerts; // not owned
        private NativeArray<ushort> convertedTris; // not owned
        private AtomicSafetyHandle vertsSafety;
        private AtomicSafetyHandle trisSafety;

        public DataArray(Allocator allocator)
        {
            vertexParams = new(4, allocator, NativeArrayOptions.UninitializedMemory);
            vertexParams[0] = new(Position, Float32, dimension: 3);
            vertexParams[1] = new(Normal, Float32, dimension: 3);
            vertexParams[2] = new(Tangent, Float16, dimension: 4);
            vertexParams[3] = new(TexCoord0, Float16, dimension: 2);

            submeshParams = new(1, allocator, NativeArrayOptions.UninitializedMemory);

            convertedVerts = default;
            convertedTris = default;
            vertsSafety = AtomicSafetyHandle.Create();
            trisSafety = AtomicSafetyHandle.Create();
        }

        // call mesh.MarkModified() if the flags are set MeshUpdateFlags.DontNotifyMeshUsers, mesh.UploadMeshData() seems only used for simpler mesh api
        public void ApplyAndResetBuilder(ref NetworkMeshBuilder builder, Mesh mesh)
        {
            Assert.IsFalse(builder.vertices.IsEmpty);
            Assert.IsFalse(builder.indices.IsEmpty);

            var vertCount = builder.vertices.Length;
            var triCount = builder.indices.Length;
            var indexCount = triCount * 3;

            unsafe
            {
                //convertedVerts = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<VStream>(builder.vertices.Ptr, vertCount, dataAlloc);
                //convertedTris = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<TriIndex>(builder.indices.Ptr, triCount, dataAlloc).Reinterpret<ushort>(6);
                //NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref convertedVerts, vertsSafety);
                //NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref convertedTris, trisSafety);
            }

            //builder.vertices = default;
            //builder.indices = default;

            mesh.SetVertexBufferParams(vertCount, vertexParams);
            mesh.SetIndexBufferParams(indexCount, IndexFormat.UInt16);
            mesh.SetVertexBufferData(builder.vertices.AsArray(), dataStart: 0, meshBufferStart: 0, vertCount, stream: 0, updateFlags);
            mesh.SetIndexBufferData(builder.indices.AsArray().Reinterpret<ushort>(6), dataStart: 0, meshBufferStart: 0, indexCount, updateFlags);

            builder.vertices.Dispose();
            builder.indices.Dispose();

            //convertedVerts = default;
            //convertedTris = default;

            submeshParams[0] = new SubMeshDescriptor
            {
                bounds = builder.bounds.ToBounds(),
                vertexCount = vertCount,
                indexCount = indexCount,
                topology = MeshTopology.Triangles
            };
            mesh.SetSubMeshes(submeshParams, updateFlags);

            mesh.MarkModified();
        }

        public void Dispose()
        {
            vertexParams.Dispose();
            submeshParams.Dispose();
        }
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    [BurstCompile]
    private struct VStream
    {
        public float3 position;
        public float3 normal;
        public half4 tangent;
        public half2 texCoord0;
    }

    // reinterpret as 3 ushorts
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    private struct TriIndex
    {
        public ushort a, b, c;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator TriIndex(int3 t)
        {
            return new()
            {
                a = (ushort)t.x,
                b = (ushort)t.y,
                c = (ushort)t.z
            };
        }
    }

    private NativeList<VStream> vertices;
    private NativeList<TriIndex> indices;
    private AABB bounds;
    public bool IsEmpty { get => !vertices.IsEmpty && !indices.IsEmpty; }

    public NetworkMeshBuilder(AABB bounds)
    {
        this.vertices = default;
        this.indices = default;
        this.bounds = bounds;
    }

    public void Allocate(int capacity)
    {
        Assert.IsFalse(vertices.IsCreated || indices.IsCreated);
        vertices = new NativeList<VStream>(capacity, dataAlloc);
        indices = new NativeList<TriIndex>((capacity * 2) / 3, dataAlloc);
    }

    [BurstCompile]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int AddVertex(Vertex vertex)
    {
        vertices.Add(new VStream
        {
            position = vertex.position,
            normal = vertex.normal,
            tangent = (half4)vertex.tangent,
            texCoord0 = (half2)vertex.texCoord0
        });
        return vertices.Length - 1;
    }

    [BurstCompile]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddTriIndex(int3 triangle)
    {
        Assert.IsTrue(math.all(triangle >= 0) && math.all(triangle < 100));
        indices.Add(triangle);
    }

    [BurstCompile]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int3 AddTriangle(Vertex a, Vertex b, Vertex c)
    {
        var tri = new int3(AddVertex(a), AddVertex(b), AddVertex(c));
        AddTriIndex(tri);
        return tri;
    }

    [BurstCompile]
    public void AddCurvedSegment(SegmentAspect main, SegmentAspect end)
    {
        // https://jeremykun.com/2013/05/11/bezier-curves-and-picasso/
        // https://www.redblobgames.com/articles/curved-paths/
        // https://gamedev.stackexchange.com/questions/5373/moving-ships-between-two-planets-along-a-bezier-missing-some-equations-for-acce/5427#5427
        // http://www.planetclegg.com/projects/WarpingTextToSplines.html
        // https://citeseerx.ist.psu.edu/document?repid=rep1&type=pdf&doi=786043deb56d3f9dec4f825b8db8940a28df20c6
        // https://homepage.cs.uiowa.edu/~kearney/pubs/CurvesAndSurfacesArcLength.pdf

        // use that thing that calculates the curve at any point, to base the subdivisions on
        // mainly by finding the perpendicular line at any point along the curve

        float width = 0.1f;
        float length = math.distance(main.Position, end.Position);

        for (float i = 0; i < 1f; i += 0.01f)
        {
            
        }
    }

    [BurstCompile]
    public void AddStraightSegment(SegmentAspect main, SegmentAspect end)
    {
        float width = 0.1f;
        float length = math.distance(main.Position, end.Position);

        const float roadWidth = 3.65f; // meters
        const float sidewalkWidth = 1.8288f;
        const float sidewalkHeight = 0.1524f;

        const int subdivsPerM = 1;
        int subdivs = (int)math.floor(length) / subdivsPerM;
        float subdivLength = length / (float)subdivs;

        for (int i = 0; i < subdivs; ++i)
        {
            float3 dir = end.Position - main.Position;
            float3 up = math.normalize(new float3(-dir.z, 0, dir.x));
            float3 y = new float3(0, main.Position.y, 0);

            int v0 = AddVertex(
                new()
                {
                    position = main.Position - (up * width) + y,
                    normal = new(0, 1, 0),
                    tangent = new(0, 0, 1, 0),
                    texCoord0 = new(0, 0)
                });
            int v1 = AddVertex(
                new()
                {
                    position = main.Position + (up * width) + y,
                    normal = new(0, 1, 0),
                    tangent = new(0, 0, 1, 0),
                    texCoord0 = new(1, 0)
                });
            int v2 = AddVertex(
                new()
                {
                    position = end.Position + (up * width) + y,
                    normal = new(0, 1, 0),
                    tangent = new(0, 0, 1, 0),
                    texCoord0 = new(1, 1)
                });
            int v3 = AddVertex(
                new()
                {
                    position = end.Position - (up * width) + y,
                    normal = new(0, 1, 0),
                    tangent = new(0, 0, 1, 0),
                    texCoord0 = new(0, 1)
                });

            AddTriIndex(new(v0, v1, v2));
            AddTriIndex(new(v0, v2, v3));
        }
    }

    [BurstCompile]
    public void AddSegment(SegmentAspect main, SegmentAspect end)
    {
        float2 midPoint = Util.Midpoint(main.Position.xz, end.Position.xz);
        
        if (Util.Close(main.ControlPoint, midPoint, 0.05f))
        {
            AddStraightSegment(main, end);
        }
        else
        {
            AddCurvedSegment(main, end);
        }

        
    }

    public void Dispose()
    {
        indices.Dispose();
        vertices.Dispose();
    }
}
