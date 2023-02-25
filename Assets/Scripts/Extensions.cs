using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Assertions;

public static class Extensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [BurstCompile]
    public static bool IsNull(this Entity entity)
    {
        return entity == Entity.Null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [BurstCompile]
    public static unsafe void UnsafeCopyFromNoResize<T>(this NativeArray<T> dst, UnsafeList<T> src, int offset = 0)
        where T : unmanaged
    {
        Assert.IsTrue(offset >= 0);
        Assert.IsTrue(dst.Length >= src.Length + offset);
        var dstPtr = (T*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(dst) + offset;
        UnsafeUtility.MemCpy(dstPtr, src.Ptr, src.Length * sizeof(T));
    }
}

