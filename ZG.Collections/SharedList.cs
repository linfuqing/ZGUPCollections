using System;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ZG
{
    public interface INativeListReader<T>
    {
        int length
        {
            get;
        }

        T this[int index]
        {
            get;
        }
    }

    public struct SharedList<T> where T : unmanaged
    {
        [NativeContainer]
        public struct Reader : INativeListReader<T>
        {
            [NativeDisableUnsafePtrRestriction]
            private unsafe UnsafeList<T>* __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;

            internal static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<Reader>();
#endif

            public unsafe int length
            {
                get
                {
                    __CheckRead();

                    return __values->Length;
                }
            }

            public unsafe T this[int index]
            {
                get
                {
                    __CheckRead();

                    return (*__values)[index];
                }
            }

            public unsafe Reader(ref SharedList<T> list)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = list.m_Safety;
                
                CollectionHelper.SetStaticSafetyId<Reader>(ref m_Safety, ref StaticSafetyID.Data);
#endif

                __values = (UnsafeList<T>*)UnsafeUtility.AddressOf(ref list.__data->values);
            }

            public unsafe NativeArray<T> AsArray()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckGetSecondaryDataPointerAndThrow(m_Safety);
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                var result = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(__values->Ptr, __values->Length, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                var arraySafety = m_Safety;
                AtomicSafetyHandle.UseSecondaryVersion(ref arraySafety);
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref result, arraySafety);
#endif

                return result;
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private void __CheckRead()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            }

        }

        [NativeContainer]
        public struct Writer : INativeListReader<T>
        {
            [NativeDisableUnsafePtrRestriction]
            private unsafe UnsafeList<T>* __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;

            internal static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<Writer>();
#endif

            public unsafe static implicit operator NativeList<T>(in Writer writer)
            {
                NativeList<T> result;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                result.m_Safety = writer.m_Safety;

                result.m_SafetyIndexHint = (writer.__values->Allocator.Handle).AddSafetyHandle(result.m_Safety);

                CollectionHelper.SetStaticSafetyId<NativeList<T>>(ref result.m_Safety, ref NativeList <T>.s_staticSafetyId.Data);
#endif
                result.m_ListData = writer.__values;

                return result;
            }

            public unsafe int length
            {
                get
                {
                    __CheckRead();

                    return __values->Length;
                }
            }

            public unsafe int capacity
            {
                get
                {
                    __CheckRead();

                    return __values->Capacity;
                }

                set
                {
                    __CheckWriteAndBumpSecondaryVersion();

                    __values->SetCapacity(value);
                }
            }

            public unsafe T this[int index]
            {
                get
                {
                    __CheckRead();

                    return (*__values)[index];
                }

                set
                {
                    __CheckWrite();

                    (*__values)[index] = value;
                }
            }

            public unsafe Writer(ref SharedList<T> list)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = list.m_Safety;

                CollectionHelper.SetStaticSafetyId<Writer>(ref m_Safety, ref StaticSafetyID.Data);
#endif

                __values = (UnsafeList<T>*)UnsafeUtility.AddressOf(ref list.__data->values);
            }

            public unsafe ref T ElementAt(int index)
            {
                __CheckWrite();

                return ref __values->ElementAt(index);// ref UnsafeUtility.ArrayElementAsRef<T>(__values->Ptr, index);
            }

            public unsafe void RemoveAt(int index)
            {
                __CheckWrite();

                __values->RemoveAt(index);
            }

            public unsafe void RemoveAtSwapBack(int index)
            {
                __CheckWrite();

                __values->RemoveAtSwapBack(index);
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
            unsafe void CheckIndexCount(int index, int count)
            {
                if (count < 0)
                {
                    throw new ArgumentOutOfRangeException($"Value for count {count} must be positive.");
                }

                if (index < 0)
                {
                    throw new IndexOutOfRangeException($"Value for index {index} must be positive.");
                }

                if (index >= __values->m_length)
                {
                    throw new IndexOutOfRangeException($"Value for index {index} is out of bounds.");
                }

                if (index + count > __values->m_length)
                {
                    throw new ArgumentOutOfRangeException($"Value for count {count} is out of bounds.");
                }
            }
            
            public unsafe void RemoveRange(int index, int count)
            {
                __CheckWrite();

                CheckIndexCount(index, count);

                index = CollectionHelper.AssumePositive(index);
                count = CollectionHelper.AssumePositive(count);

                if (count > 0)
                {
                    int copyFrom = Unity.Mathematics.math.min(index + count, __values->m_length);
                    void* dst = __values->Ptr + index;
                    void* src = __values->Ptr + copyFrom;
                    UnsafeUtility.MemMove(dst, src, (__values->m_length - copyFrom) * UnsafeUtility.SizeOf<T>());
                    __values->m_length -= count;
                }
                //__values->RemoveRange(index, count);
            }

            public unsafe void Add(in T value)
            {
                __CheckWriteAndBumpSecondaryVersion();

                __values->Add(value);
            }

            public unsafe void AddRange(in NativeArray<T> values)
            {
                __CheckWriteAndBumpSecondaryVersion();

                __values->AddRange(values.GetUnsafeReadOnlyPtr(), values.Length);
            }

            public unsafe void Clear()
            {
                __CheckWrite();

                __values->Clear();
            }

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

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private void __CheckWriteAndBumpSecondaryVersion()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
            }
        }

        [NativeContainer]
        [NativeContainerIsAtomicWriteOnly]
        public struct ParallelWriter
        {
            [NativeDisableUnsafePtrRestriction]
            private unsafe UnsafeList<T>* __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;

            internal static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<ParallelWriter>();
#endif

            public unsafe ParallelWriter(ref SharedList<T> list)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = list.m_Safety;

                CollectionHelper.SetStaticSafetyId<ParallelWriter>(ref m_Safety, ref StaticSafetyID.Data);
#endif

                __values = (UnsafeList<T>*)UnsafeUtility.AddressOf(ref list.__data->values);
            }

            public unsafe void AddNoResize(in T value)
            {
                __CheckWrite();

                __values->AsParallelWriter().AddNoResize(value);
            }

            public unsafe void AddRangeNoResize(in NativeArray<T> values)
            {
                __CheckWrite();

                __values->AsParallelWriter().AddRangeNoResize(values.GetUnsafeReadOnlyPtr(), values.Length);
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private void __CheckWrite()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            }
        }

        private struct Data
        {
            public UnsafeList<T> values;
            public LookupJobManager lookupJobManager;
        }

        [NativeDisableUnsafePtrRestriction]
        private unsafe Data* __data;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif

        public unsafe bool isCreated => __data != null && __data->values.IsCreated;

        public unsafe ref LookupJobManager lookupJobManager => ref __data->lookupJobManager;

        public Reader reader => new Reader(ref this);

        public Writer writer => new Writer(ref this);

        public ParallelWriter parallelWriter => new ParallelWriter(ref this);

        public unsafe SharedList(in AllocatorManager.AllocatorHandle allocator)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = CollectionHelper.CreateSafetyHandle(allocator);
#endif

            __data = AllocatorManager.Allocate<Data>(allocator);
            __data->values = new UnsafeList<T>(0, allocator);
            __data->lookupJobManager = new LookupJobManager();
        }

        public unsafe void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            CollectionHelper.DisposeSafetyHandle(ref m_Safety);
#endif

            var allocator = __data->values.Allocator;

            __data->values.Dispose();

            AllocatorManager.Free(allocator, __data);

            __data = null;
        }

        public unsafe NativeArray<T> AsArray()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckGetSecondaryDataPointerAndThrow(m_Safety);
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

            NativeArray<T> result = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(__data->values.Ptr, __data->values.Length, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var arraySafety = m_Safety;
            AtomicSafetyHandle.UseSecondaryVersion(ref arraySafety);
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref result, arraySafety);
#endif

            return result;
        }

        public unsafe NativeList<T> AsList()
        {
            NativeList<T> result;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            result.m_Safety = m_Safety;

            result.m_SafetyIndexHint = (__data->values.Allocator.Handle).AddSafetyHandle(result.m_Safety);

            CollectionHelper.SetStaticSafetyId<NativeList<T>>(ref result.m_Safety, ref NativeList<T>.s_staticSafetyId.Data);
#endif
            result.m_ListData = (UnsafeList<T>*)UnsafeUtility.AddressOf(ref __data->values);

            return result;
        }

    }

    public static class SharedListUtility
    {
        public static int IndexOf<TReader, TValue>(this ref TReader reader, in TValue value) 
            where TReader : struct, INativeListReader<TValue>
            where TValue : IEquatable<TValue>
        {
            int numCollidersToIgrone = reader.length;
            for (int i = 0; i < numCollidersToIgrone; ++i)
            {
                if (reader[i].Equals(value))
                    return i;
            }

            return -1;
        }

        public static bool Contains<TReader, TValue>(this ref TReader reader, in TValue value)
            where TReader : struct, INativeListReader<TValue>
            where TValue : IEquatable<TValue>
        {
            return IndexOf(ref reader, value) != -1;
        }
    }
}