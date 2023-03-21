using System;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ZG
{
    public struct NativeHashMapLite<TKey, TValue> 
        where TKey : unmanaged, IEquatable<TKey> 
        where TValue : unmanaged
    {
        [NativeContainer]
        [NativeContainerIsReadOnly]
        public struct Enumerator : IEnumerator<KeyValue<TKey, TValue>>
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif
            internal UnsafeParallelHashMapDataEnumerator m_Enumerator;

            /// <summary>
            /// Disposes enumerator.
            /// </summary>
            public void Dispose() { }

            /// <summary>
            /// Advances the enumerator to the next element of the container.
            /// </summary>
            /// <returns>Returns true if the iterator is successfully moved to the next element, otherwise it returns false.</returns>
            public bool MoveNext()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                return m_Enumerator.MoveNext();
            }

            /// <summary>
            /// Resets the enumerator to the first element of the container.
            /// </summary>
            public void Reset()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                m_Enumerator.Reset();
            }

            /// <summary>
            /// Gets the element at the current position of the enumerator in the container.
            /// </summary>
            public KeyValue<TKey, TValue> Current => m_Enumerator.GetCurrent<TKey, TValue>();

            object IEnumerator.Current => Current;
        }

        private UnsafeParallelHashMap<TKey, TValue> __value;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;

        private static readonly SharedStatic<int> StaticSafetyId = SharedStatic<int>.GetOrCreate<NativeHashMapLite<TKey, TValue>>();

        [BurstDiscard]
        private static void CreateStaticSafetyId()
        {
            StaticSafetyId.Data = AtomicSafetyHandle.NewStaticSafetyId<NativeHashMapLite<TKey, TValue>>();
        }
#endif

        public bool isCreated => __value.IsCreated;

        public bool isEmpty => __value.IsEmpty;

        public int Capacity
        {
            get
            {
                __CheckRead();

                return __value.Capacity;
            }

            set
            {
                __CheckWrite();

                __value.Capacity = value;
            }
        }

        public TValue this[in TKey key]
        {
            get
            {
                __CheckRead();

                return __value[key];
            }

            set
            {
                __CheckWrite();

                __value[key] = value;
            }
        }

        public NativeHashMapLite(int capacity, Allocator allocator)
        {
            __value = new UnsafeParallelHashMap<TKey, TValue>(capacity, allocator);

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

            __value.Dispose();
        }

        public bool ContainsKey(in TKey key)
        {
            __CheckRead();

            return __value.ContainsKey(key);
        }

        public bool TryGetValue(in TKey key, out TValue value)
        {
            __CheckRead();

            return __value.TryGetValue(key, out value);
        }

        public void Add(in TKey key, in TValue value)
        {
            __CheckWrite();

            __value.Add(key, value);
        }

        public bool Remove(in TKey key)
        {
            __CheckWrite();

            /*if (!__values.TryGetValue(handle, out var value))
                return false;

            value.Dispose();*/

            return __value.Remove(key);
        }

        public void Clear()
        {
            __CheckWrite();

            __value.Clear();
        }

        public unsafe Enumerator GetEnumerator()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckGetSecondaryDataPointerAndThrow(m_Safety);
            var ash = m_Safety;
            AtomicSafetyHandle.UseSecondaryVersion(ref ash);
            AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(ash, true);
#endif
            return new Enumerator
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = ash,
#endif
                m_Enumerator = new UnsafeParallelHashMapDataEnumerator(__value.m_Buffer),
            };
        }

        public static unsafe implicit operator NativeParallelHashMap<TKey, TValue>(in NativeHashMapLite<TKey, TValue> value)
        {
            NativeParallelHashMap<TKey, TValue> result = default;
            result.m_HashMapData = value.__value;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            result.m_Safety = value.m_Safety;
#endif

            return result;
        }

        /*public static implicit operator UnsafeHashMap<TKey, TValue>(in NativeHashMapLite<TKey, TValue> value)
        {
            return value.__value;
        }*/

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void __CheckRead()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void __CheckWrite()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
        }
    }

    public static class NativeHashMapUnsafeUtility
    {
        public static NativeParallelHashMap<TKey, TValue> ConvertExistingDataToNativeHashMap<TKey, TValue>(UnsafeParallelHashMap<TKey, TValue> value) 
            where TKey : unmanaged, IEquatable<TKey>
             where TValue : unmanaged
        {
            NativeParallelHashMap<TKey, TValue> result = default;
            result.m_HashMapData = value;

            return result;
        }

        public static UnsafeParallelHashMap<TKey, TValue> GetUnsafe<TKey, TValue>(this ref NativeParallelHashMap<TKey, TValue> value)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            return value.m_HashMapData;
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        public static void SetAtomicSafetyHandle<TKey, TValue>(ref NativeParallelHashMap<TKey, TValue> value, AtomicSafetyHandle safety)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            value.m_Safety = safety;
        }
#endif
    }
}