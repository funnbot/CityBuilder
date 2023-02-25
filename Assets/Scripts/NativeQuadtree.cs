using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;

//[BurstCompile]
public struct Square
{
    public float2 center;
    public float extent; // from center to edge

    public Square(float2 center, float extent)
    {
        this.center = center;
        this.extent = extent;
    }

    public float2 Min => center - new float2(extent, extent);
    public float2 Max => center + new float2(extent, extent);

    public void DebugDraw(Color color)
    {
        var a = new Vector3(center.x + extent, 0, center.y + extent);
        var b = new Vector3(center.x + extent, 0, center.y - extent);
        var c = new Vector3(center.x - extent, 0, center.y - extent);
        var d = new Vector3(center.x - extent, 0, center.y + extent);
        Debug.DrawLine(a, b, color, 100);
        Debug.DrawLine(b, c, color, 100);
        Debug.DrawLine(c, d, color, 100);
        Debug.DrawLine(d, a, color, 100);
    }
}

//[BurstCompile]
//[GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(Entity) })]
public struct UnsafeQuadtree<T>
    where T : unmanaged
{
    private readonly struct Node
    {
        public readonly T? data;
        public readonly int childIdx;
        public readonly float2 position;

        public bool IsLeaf => childIdx == 0;
        public bool IsEmptyLeaf => childIdx == 0 && !data.HasValue;

        private Node(T? data, int childIdx, float2 position)
        {
            this.data = data;
            this.childIdx = childIdx;
            this.position = position;
        }

        public static Node Leaf(T data, float2 position) => new Node(data, 0, position);
        public static Node EmptyLeaf() => new Node(null, 0, float2.zero);
        public static Node Branch(int childIdx) => new Node(null, childIdx, float2.zero);
    }

    private UnsafeList<Node> tree;
    private Square bounds;
    private float minSubdivideSqr;

    public UnsafeQuadtree(Square bounds, float minSubdivide)
    {
        this.bounds = bounds;
        minSubdivideSqr = minSubdivide * minSubdivide;
        tree = new UnsafeList<Node>(8, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        tree.Add(Node.EmptyLeaf());
    }

    public void Insert(in T value, float2 pos)
    {
        Insert(value, pos, 0, bounds.center, bounds.extent);
    }

    private void Insert(in T value, float2 pos, int index, float2 center, float extent)
    {
        while (!tree[index].IsLeaf)
        {
            // based on current node/leaf, what quadrant to traverse to
            int quadrantIndex = QuadrantIndex(pos, center);
            index = tree[index].childIdx + quadrantIndex;
            extent *= 0.5f;
            center = QuadrantCenter(center, extent, quadrantIndex);
        }

        if (tree[index].data.HasValue) // leaf that needs to be split and moved
        {
            int newChildIdx = tree.Length;
            // add the 4 subdivisions
            tree.AddReplicate(Node.EmptyLeaf(), 4);
            // gets moved deeper into the tree
            var temp = tree[index];
            tree[index] = Node.Branch(newChildIdx);

            if (math.distancesq(pos, temp.position) <= minSubdivideSqr)
            {
                // don't allow more subdivision
                return;
            }

            // gauranteed to be branch, follow traverse
            Insert(temp.data.Value, temp.position, index, center, extent);
            // then insert actual value, again guaranteed that index points to branch
            Insert(value, pos, index, center, extent);
        }
        else // empty leaf
        {
            tree[index] = Node.Leaf(value, pos);
        }
    }

    public bool Remove(float2 pos)
    {
        int index = NearestNeighbor(pos);
        ref var node = ref tree.ElementAt(index);
        if (node.data.HasValue)
        {
            node = Node.EmptyLeaf();
            return true;
        }
        return false;
    }

    private int NearestNeighbor(float2 pos)
    {
        int index = 0;
        float2 center = bounds.center;
        float extent = bounds.extent;
        while (!tree[index].IsLeaf)
        {
            int quadrantIndex = QuadrantIndex(pos, center);
            index = tree[index].childIdx + quadrantIndex;
            extent *= 0.5f;
            center = QuadrantCenter(center, extent, quadrantIndex);
        }

        return index;
    }

    public void Visualize()
    {
        Visualize(0, bounds.center, bounds.extent);
    }

    public void Visualize(int index, float2 center, float extent)
    {
        new Square(center, extent).DebugDraw(Color.green);

        if (!tree[index].IsLeaf)
        {
            // based on current node/leaf, what quadrant to traverse to
            for (int i = 0; i < 4; ++i)
            {
                Visualize(tree[index].childIdx + i, QuadrantCenter(center, extent * 0.5f, i), extent * 0.5f);
            }
        }
        else if (tree[index].data.HasValue)
        {
            new Square(center, extent).DebugDraw(Color.red);
        }
        else
        {
            new Square(center, extent).DebugDraw(Color.green);
        }
    }


    // +x +y = 0, +x -y = 1, -x -y = 2, -x +y = 3 
    private int QuadrantIndex(float2 pos, float2 quadrantCenter)
    {
        float2 p = pos - quadrantCenter;
        if (p.x >= 0)
        {
            return p.y >= 0 ? 0 : 1;
        }
        else
        {
            return p.y >= 0 ? 3 : 2;
        }
    }

    private float2 QuadrantOffset(int quadrant)
    {
        return quadrant switch
        {
            0 => new float2(1, 1),
            1 => new float2(1, -1),
            2 => new float2(-1, -1),
            3 => new float2(-1, 1),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    private float2 QuadrantCenter(float2 parentCenter, float halfExtent, int quadrant)
    {
        return parentCenter + (halfExtent * QuadrantOffset(quadrant));
    }
}