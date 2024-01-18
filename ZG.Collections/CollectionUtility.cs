using System;
using System.Threading;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Math = ZG.Mathematics.Math;

namespace ZG
{
    public interface INativeMultiHashMapEnumeratorObject
    {
        void Reatain();

        void Release();
    }

    public struct NativeMultiHashMapEnumeratorObject : INativeMultiHashMapEnumeratorObject
    {
        public void Reatain()
        {

        }

        public void Release()
        {
        }
    }

    public struct NativeMultiHashMapEnumerator<TKey, TValue, TObject> : IEnumerator<TValue>
        where TKey : unmanaged, IEquatable<TKey>
        where TValue : unmanaged
        where TObject : unmanaged, INativeMultiHashMapEnumeratorObject
    {
        private bool __isInit;
        private TKey __key;
        private TValue __item;
        private TObject __object;
        private NativeParallelMultiHashMapIterator<TKey> __iterator;
        private NativeParallelMultiHashMap<TKey, TValue> __map;

        public TValue Current
        {
            get
            {
                return __item;
            }
        }

        public NativeMultiHashMapEnumerator(TKey key, ref TObject target, ref NativeParallelMultiHashMap<TKey, TValue> map)
        {
            target.Reatain();

            __isInit = false;
            __key = key;
            __item = default(TValue);
            __object = target;
            __iterator = default(NativeParallelMultiHashMapIterator<TKey>);
            __map = map;
        }

        public bool MoveNext()
        {
            if (!__map.IsCreated)
                return false;

            if (__isInit)
                return __map.TryGetNextValue(out __item, ref __iterator);

            __isInit = true;

            return __map.TryGetFirstValue(__key, out __item, out __iterator);
        }

        public void Dispose()
        {
            __object.Release();
        }

        void IEnumerator.Reset()
        {
            __isInit = false;
        }

        object IEnumerator.Current
        {
            get
            {
                return Current;
            }
        }
    }

    public struct NativeMultiHashMapEnumerable<TKey, TValue, TObject> : IEnumerable<TValue>
        where TKey : unmanaged, IEquatable<TKey>
        where TValue : unmanaged
        where TObject : unmanaged, INativeMultiHashMapEnumeratorObject
    {
        private TKey __key;
        private TObject __object;
        private NativeParallelMultiHashMap<TKey, TValue> __map;

        public NativeMultiHashMapEnumerable(TKey key, ref TObject target, ref NativeParallelMultiHashMap<TKey, TValue> map)
        {
            __key = key;
            __object = target;
            __map = map;
        }

        public NativeMultiHashMapEnumerator<TKey, TValue, TObject> GetEnumerator()
        {
            return new NativeMultiHashMapEnumerator<TKey, TValue, TObject>(__key, ref __object, ref __map);
        }

        IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    public struct NativeListWriteOnlyWrapper<T> : IWriteOnlyListWrapper<T, NativeList<T>> where T : unmanaged
    {
        public int GetCount(in NativeList<T> list) => list.Length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetCount(ref NativeList<T> list, int value)
        {
            list.ResizeUninitialized(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(ref NativeList<T> list, T value, int index)
        {
            list[index] = value;
        }
    }

    public struct DynamicBufferWriteOnlyWrapper<T> : IWriteOnlyListWrapper<T, DynamicBuffer<T>> where T : unmanaged
    {
        public int GetCount(in DynamicBuffer<T> list) => list.Length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetCount(ref DynamicBuffer<T> list, int value)
        {
            list.ResizeUninitialized(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(ref DynamicBuffer<T> list, T value, int index)
        {
            list[index] = value;
        }
    }

    public struct NativeSliceWrapper<T> : IReadOnlyListWrapper<T, NativeSlice<T>> where T : struct
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetCount(NativeSlice<T> list) => list.Length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Get(NativeSlice<T> list, int index) => list[index];
    }

    [Serializable]
    public struct BufferElementData<T> where T : struct, IBufferElementData
    {
        public Entity entity;
        public T value;
    }

    public struct NativeReadOnlyQueue<T> where T : unmanaged
    {
        private NativeQueue<T> __instance;

        public static implicit  operator NativeReadOnlyQueue<T>(NativeQueue<T> instance)
        {
            NativeReadOnlyQueue<T> result;
            result.__instance = instance;

            return result;
        }

        public int count => __instance.Count;
        
        public NativeReadOnlyQueue(NativeQueue<T> instance)
        {
            __instance = instance;
        }

        public bool TryDequeue(out T value) => __instance.TryDequeue(out value);
    }

    public static class CollectionUtilityEx
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int BinarySearch<TValue, TComparer>(this in NativeSlice<TValue> list, in TValue value, in TComparer comparer) 
            where TValue : struct
            where TComparer : IComparer<TValue>
        {
            return list.BinarySearch(value, comparer, new NativeSliceWrapper<TValue>());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int BinarySearch<T>(this in NativeSlice<T> list, in T value) where T : struct, IComparable<T>
        {
            return BinarySearch(list, value, new Comparer<T>());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int BinarySearch<T>(this in NativeArray<T> list, in T value) where T : struct, IComparable<T>
        {
            return BinarySearch(list, value, new Comparer<T>());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int BinaryInsert<TValue, TComparer>(this NativeArray<TValue> list, int listLength, in TValue value, in TComparer comparer) 
            where TValue : struct
            where TComparer : IComparer<TValue>
        {
            __CheckIndex(listLength, list.Length);

            int index = BinarySearch(list.Slice(0, listLength), value, comparer) + 1;
            while(index < listLength)
            {
                if (comparer.Compare(list[index], value) > 0)
                    break;

                ++index;
            }
            
            MemMove(list, index, index + 1, listLength - index);
            /*for (int i = listLength - 1; i > index; --i)
                list[i + 1] = list[i];*/

            list[/*++*/index] = value;

            return index;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int BinaryInsert<T>(this NativeArray<T> list, int listLength, in T value) where T : struct, IComparable<T>
        {
            return BinaryInsert(list, listLength, value, new Comparer<T>());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int BinaryInsert<TValue, TComparer>(this NativeList<TValue> list, in TValue value, in TComparer comparer)
            where TValue : unmanaged
            where TComparer : IComparer<TValue>
        {
            int length = list.Length;
            list.ResizeUninitialized(length + 1);

            return BinaryInsert(list.AsArray(), length, value, comparer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int BinaryInsert<T>(this NativeList<T> list, in T value) where T : unmanaged, IComparable<T>
        {
            return BinaryInsert(list, value, new Comparer<T>());
        }

        public static int IndexOf<T>(this NativeSlice<T> list, in T value) where T : struct, IEquatable<T>
        {
            int length = list.Length;
            for (int i = 0; i < length; ++i)
            {
                if (list[i].Equals(value))
                    return i;
            }

            return -1;
        }

        public static unsafe void MemMove<T>(this NativeArray<T> instance, int fromIndex, int toIndex, int count) where T : struct
        {
            int length = instance.Length;

            UnityEngine.Assertions.Assert.IsFalse(fromIndex < 0);
            UnityEngine.Assertions.Assert.IsFalse(toIndex < 0);
            UnityEngine.Assertions.Assert.IsFalse(fromIndex + count > length);
            UnityEngine.Assertions.Assert.IsFalse(toIndex + count > length);

            UnsafeUtility.MemMove(instance.Slice(toIndex).GetUnsafePtr(), instance.Slice(fromIndex).GetUnsafePtr(), UnsafeUtility.SizeOf<T>() * count);
        }

        public static unsafe void MemClear<T>(this NativeArray<T> instance, int startIndex = 0, int count = 0) where T : struct
        {
            int length = instance.Length;

            if (count == 0)
                count = length;

            UnityEngine.Assertions.Assert.IsFalse(startIndex < 0);
            UnityEngine.Assertions.Assert.IsFalse(startIndex + count > length);
            
            UnsafeUtility.MemClear(instance.Slice(startIndex).GetUnsafePtr(), UnsafeUtility.SizeOf<T>() * count);
        }

        public static unsafe void MemClear<T>(this T[] instance, int startIndex, int count) where T : unmanaged
        {
            int length = instance.Length;

            UnityEngine.Assertions.Assert.IsFalse(startIndex < 0);
            UnityEngine.Assertions.Assert.IsFalse(startIndex + count > length);

            fixed (T* ptr = instance)
                UnsafeUtility.MemClear(ptr + startIndex, sizeof(T) * count);
        }

        public static int ConvertToUniqueArray<T>(this NativeArray<T> instance) where T : struct, IEquatable<T>
        {
            int length = instance.Length, i, j;
            T value;
            for(i = 0; i < length; ++i)
            {
                value = instance[i];
                for(j = i + 1; j < length; ++j)
                {
                    if(value.Equals(instance[j]))
                        instance[j--] = instance[--length];
                }
            }
            
            return length;
        }

        public static int ConvertToUniqueArray<T, U>(this NativeArray<T> instance, U comparer, NativeArray<int> indices = default)
            where T : struct
            where U : struct, IEqualityComparer<T>
        {
            NativeArray<int> origins;
            int numIndices = indices.IsCreated ? indices.Length : 0, i;
            if (numIndices > 0)
            {
                origins = new NativeArray<int>(numIndices, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (i = 0; i < numIndices; ++i)
                    origins[i] = i;
            }
            else
                origins = default;

            int length = instance.Length, source, destination, j;
            T value;
            for (i = 0; i < length; ++i)
            {
                value = instance[i];

                if (i < numIndices)
                {
                    destination = i;
                    do
                    {
                        source = destination;
                        destination = origins[source];

                        indices[destination] = i;

                    } while (source != destination);
                }

                for (j = i + 1; j < length; ++j)
                {
                    if (comparer.Equals(value, instance[j]))
                    {
                        --length;

                        if (j < numIndices)
                        {
                            destination = j;
                            do
                            {
                                source = destination;
                                destination = origins[source];

                                indices[destination] = i;

                            } while (source != destination);

                            if (j < length)
                            {
                                source = length;

                                destination = origins[source];

                                origins[j] = destination;

                                if (source == destination)
                                    indices[destination] = j;
                                else if (indices[destination] > j)
                                {
                                    do
                                    {
                                        indices[destination] = j;

                                        source = destination;
                                        destination = origins[source];
                                    } while (source != destination);
                                }
                            }
                        }

                        instance[j--] = instance[length];
                    }
                }
            }

            if (origins.IsCreated)
                origins.Dispose();

            return length;
        }

        public static NativeMultiHashMapEnumerable<TKey, TValue, TObject> GetEnumerable<TKey, TValue, TObject>(this NativeParallelMultiHashMap<TKey, TValue> map, TKey key, ref TObject target)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
            where TObject : unmanaged, INativeMultiHashMapEnumeratorObject
        {
            return new NativeMultiHashMapEnumerable<TKey, TValue, TObject>(key, ref target, ref map);
        }

        public static NativeMultiHashMapEnumerable<TKey, TValue, NativeMultiHashMapEnumeratorObject> GetEnumerable<TKey, TValue>(this NativeParallelMultiHashMap<TKey, TValue> map, TKey key)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            NativeMultiHashMapEnumeratorObject target = default;
            return new NativeMultiHashMapEnumerable<TKey, TValue, NativeMultiHashMapEnumeratorObject>(key, ref target, ref map);
        }

        public static unsafe int Increment(this ref NativeArray<int> instance, int index)
        {
            __CheckIndex(index, instance.Length);

            return Interlocked.Increment(ref UnsafeUtility.ArrayElementAsRef<int>(instance.GetUnsafePtr(), index));
        }

        public static unsafe int Decrement(this ref NativeArray<int> instance, int index)
        {
            __CheckIndex(index, instance.Length);

            return Interlocked.Decrement(ref UnsafeUtility.ArrayElementAsRef<int>(instance.GetUnsafePtr(), index));
        }

        public static unsafe int Add(this ref NativeArray<int> instance, int index, int value)
        {
            __CheckIndex(index, instance.Length);

            return Interlocked.Add(ref UnsafeUtility.ArrayElementAsRef<int>(instance.GetUnsafePtr(), index), value);
        }

        public static unsafe float Add(this ref NativeArray<float> instance, int index, float value)
        {
            __CheckIndex(index, instance.Length);

            return Math.InterlockedAdd(ref UnsafeUtility.ArrayElementAsRef<float>(instance.GetUnsafePtr(), index), value);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void __CheckIndex(int index, int length)
        {
            if (index >= length)
                throw new IndexOutOfRangeException();
        }
    }
}


namespace ZG.Unsafe
{
    public static partial class CollectionUtility
    {
        [Unity.Burst.BurstCompile]
        private unsafe struct DisposeJob : IJob
        {
            [NativeDisableUnsafePtrRestriction]
            public void* ptr;
            public AllocatorManager.AllocatorHandle allocator;

            public void Execute()
            {
                AllocatorManager.Free(allocator, ptr);
            }
        }

        public static unsafe JobHandle Dispose(void* ptr, in AllocatorManager.AllocatorHandle allocator, in JobHandle inputDeps)
        {
            DisposeJob disposeJob;
            disposeJob.ptr = ptr;
            disposeJob.allocator = allocator;

            return disposeJob.ScheduleByRef(inputDeps);
        }

        public static unsafe NativeArray<T> ToNativeArray<T>(void* dataPointer, int length) where T : struct
        {
            NativeArray<T> shadow = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(dataPointer, length, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref shadow, AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            return shadow;
        }

        public static unsafe NativeArray<T> ToNativeArray<T>(ref this T instance) where T : unmanaged
        {
            return ToNativeArray<T>(UnsafeUtility.AddressOf(ref instance), 1);
        }
        
        public static unsafe UnsafeList<T> ToUnsafeListReadOnly<T>(this in NativeArray<T> instance) where T : unmanaged
        {
            return new UnsafeList<T>((T*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(instance), instance.Length);
        }

        public static unsafe NativeArray<T> AsArray<T>(in this UnsafeList<T> instance) where T : unmanaged
        {
            return ToNativeArray<T>(instance.Ptr, instance.Length);
        }

        public static unsafe ref T ElementAt<T>(this ref NativeArray<T> instance, int index) where T : struct
        {
            __CheckIndex(index, instance.Length);

            return ref UnsafeUtility.ArrayElementAsRef<T>(instance.GetUnsafePtr(), index);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void __CheckIndex(int index, int length)
        {
            if (index < 0 || index >= length)
                throw new IndexOutOfRangeException();
        }
    }
}