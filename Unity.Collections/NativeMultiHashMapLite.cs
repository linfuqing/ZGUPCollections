using System;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ZG
{
    public struct NativeMultiHashMapLite<TKey, TValue> 
        where TKey : unmanaged, IEquatable<TKey>
        where TValue : unmanaged
    {
        private UnsafeParallelMultiHashMap<TKey, TValue> __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;

        private static readonly SharedStatic<int> StaticSafetyId = SharedStatic<int>.GetOrCreate<NativeMultiHashMapLite<TKey, TValue>>();

        [BurstDiscard]
        private static void CreateStaticSafetyId()
        {
            StaticSafetyId.Data = AtomicSafetyHandle.NewStaticSafetyId<NativeMultiHashMapLite<TKey, TValue>>();
        }
#endif

        public bool isCreated => __values.IsCreated;

        public NativeMultiHashMapLite(int capacity, Allocator allocator)
        {
            __values = new UnsafeParallelMultiHashMap<TKey, TValue>(capacity, allocator);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = AtomicSafetyHandle.Create();

            if (StaticSafetyId.Data == 0)
                CreateStaticSafetyId();

            AtomicSafetyHandle.SetStaticSafetyId(ref m_Safety, StaticSafetyId.Data);
#endif
        }

        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(m_Safety);
#endif

            __values.Dispose();
        }

        public void Clear()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif

            __values.Clear();
        }

        public void Add(in TKey key, in TValue value)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif

            __values.Add(key, value);
        }

        public static unsafe implicit operator NativeParallelMultiHashMap<TKey, TValue>(in NativeMultiHashMapLite<TKey, TValue> value)
        {
            NativeParallelMultiHashMap<TKey, TValue> result = default;
            result.m_MultiHashMapData = value.__values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            result.m_Safety = value.m_Safety;
#endif

            return result;
        }
    }

    public static class NativeMultiHashMapUnsafeUtility
    {
        public static NativeParallelMultiHashMap<TKey, TValue> ConvertExistingDataToNativeHashMap<TKey, TValue>(this in UnsafeParallelMultiHashMap<TKey, TValue> value)
            where TKey : unmanaged, IEquatable<TKey>
             where TValue : unmanaged
        {
            NativeParallelMultiHashMap<TKey, TValue> result = default;
            result.m_MultiHashMapData = value;

            return result;
        }

        public static UnsafeParallelMultiHashMap<TKey, TValue> GetUnsafe<TKey, TValue>(this ref NativeParallelMultiHashMap<TKey, TValue> value)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            return value.m_MultiHashMapData;
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        public static void SetAtomicSafetyHandle<TKey, TValue>(ref NativeParallelMultiHashMap<TKey, TValue> value, AtomicSafetyHandle safety)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            value.m_Safety = safety;
        }
#endif
    }
}