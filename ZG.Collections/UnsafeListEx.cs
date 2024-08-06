using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ZG
{
    [DebuggerDisplay("Length = {length}, Capacity = {capacity}, IsCreated = {isCreated}, IsEmpty = {isEmpty}")]
    [DebuggerTypeProxy(typeof(UnsafeListExDebugView<>))]
    public unsafe struct UnsafeListEx<T> : IDisposable where T : unmanaged
    {
        [NativeContainer]
        [NativeContainerIsAtomicWriteOnly]
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new [] { typeof(int) })]
        public struct ParallelWriter
        {
            /// <summary>
            /// The data of the list.
            /// </summary>
            public readonly void* Ptr
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => ListData->Ptr;
            }

            /// <summary>
            /// The internal unsafe list.
            /// </summary>
            /// <value>The internal unsafe list.</value>
            [NativeDisableUnsafePtrRestriction]
            public UnsafeList<T>* ListData;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
            internal static readonly SharedStatic<int> s_staticSafetyId = SharedStatic<int>.GetOrCreate<ParallelWriter>();

            [GenerateTestsForBurstCompatibility(CompileTarget = GenerateTestsForBurstCompatibilityAttribute.BurstCompatibleCompileTarget.Editor)]
            internal unsafe ParallelWriter(UnsafeList<T>* listData, ref AtomicSafetyHandle safety)
            {
                ListData = listData;
                m_Safety = safety;
                CollectionHelper.SetStaticSafetyId<ParallelWriter>(ref m_Safety, ref s_staticSafetyId.Data);
            }
#else
            internal unsafe ParallelWriter(UnsafeList<T>* listData)
            {
                ListData = listData;
            }
#endif

            public ParallelWriter(ref NativeList<T> list) : this(list.m_ListData
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                , ref list.m_Safety
#endif
                )
            {
                
            }

            /// <summary>
            /// Appends an element to the end of this list.
            /// </summary>
            /// <param name="value">The value to add to the end of this list.</param>
            /// <remarks>
            /// Increments the length by 1 unless doing so would exceed the current capacity.
            /// </remarks>
            /// <exception cref="InvalidOperationException">Thrown if adding an element would exceed the capacity.</exception>
            public int AddNoResize(T value)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
                var idx = Interlocked.Increment(ref ListData->m_length) - 1;
                CheckSufficientCapacity(ListData->Capacity, idx + 1);

                UnsafeUtility.WriteArrayElement(ListData->Ptr, idx, value);

                return idx;
            }

            /// <summary>
            /// Appends elements from a buffer to the end of this list.
            /// </summary>
            /// <param name="ptr">The buffer to copy from.</param>
            /// <param name="count">The number of elements to copy from the buffer.</param>
            /// <remarks>
            /// Increments the length by `count` unless doing so would exceed the current capacity.
            /// </remarks>
            /// <exception cref="InvalidOperationException">Thrown if adding the elements would exceed the capacity.</exception>
            public int AddRangeNoResize(void* ptr, int count)
            {
                CheckArgPositive(count);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
                var idx = Interlocked.Add(ref ListData->m_length, count) - count;
                CheckSufficientCapacity(ListData->Capacity, idx + count);

                var sizeOf = sizeof(T);
                void* dst = (byte*)ListData->Ptr + idx * sizeOf;
                UnsafeUtility.MemCpy(dst, ptr, count * sizeOf);

                return idx;
            }

            /// <summary>
            /// Appends the elements of another list to the end of this list.
            /// </summary>
            /// <param name="list">The other list to copy from.</param>
            /// <remarks>
            /// Increments the length of this list by the length of the other list unless doing so would exceed the current capacity.
            /// </remarks>
            /// <exception cref="InvalidOperationException">Thrown if adding the elements would exceed the capacity.</exception>
            public int AddRangeNoResize(UnsafeList<T> list)
            {
                return AddRangeNoResize(list.Ptr, list.Length);
            }

            /// <summary>
            /// Appends the elements of another list to the end of this list.
            /// </summary>
            /// <param name="list">The other list to copy from.</param>
            /// <remarks>
            /// Increments the length of this list by the length of the other list unless doing so would exceed the current capacity.
            /// </remarks>
            /// <exception cref="InvalidOperationException">Thrown if adding the elements would exceed the capacity.</exception>
            public int AddRangeNoResize(NativeList<T> list)
            {
                return AddRangeNoResize(*list.m_ListData);
            }
            
            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
            static void CheckSufficientCapacity(int capacity, int length)
            {
                if (capacity < length)
                    throw new InvalidOperationException($"Length {length} exceeds Capacity {capacity}");
            }
            
            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
            static void CheckArgPositive(int value)
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException($"Value {value} must be positive.");
            }
        }

        [NativeDisableUnsafePtrRestriction]
        private UnsafeList<T>* __value;

        public bool isCreated => __value != null && __value->IsCreated;

        public bool isEmpty => __value->IsEmpty;

        public int length => __value->Length;

        public int capacity => __value->Capacity;

        public AllocatorManager.AllocatorHandle allocator => __value->Allocator;

        public unsafe T this[int index]
        {
            get
            {
                __CheckBounds(index);

                return UnsafeUtility.ReadArrayElement<T>(__value->Ptr, index);
            }

            set
            {
                __CheckBounds(index);

                UnsafeUtility.WriteArrayElement(__value->Ptr, index, value);
            }
        }

        public UnsafeListEx(int capacity, in AllocatorManager.AllocatorHandle allocator)
        {
            __value = UnsafeList<T>.Create(capacity, allocator);
        }

        public UnsafeListEx(in AllocatorManager.AllocatorHandle allocator) : this(1, allocator)
        {

        }

        public void Dispose()
        {
            UnsafeList<T>.Destroy(__value);

            __value = null;
        }

        public ref T ElementAt(int index) => ref UnsafeUtility.ArrayElementAsRef<T>(__value->Ptr, index);

        public void Resize(int length, NativeArrayOptions options)
        {
            __value->Resize(length, options);
        }

        public void ResizeUninitialized(int length) => Resize(length, NativeArrayOptions.UninitializedMemory);

        public void RemoveAt(int index) => __value->RemoveAt(index);

        public void Add(in T value) => __value->Add(value);

        public void AddNoResize(in T value) => __value->AddNoResize(value);

        public void AddRange(in UnsafeListEx<T> values) => __value->AddRange(*values.__value);

        public void AddRange(in NativeArray<T> values) => __value->AddRange(values.GetUnsafeReadOnlyPtr(), values.Length);

        public void Clear() => __value->Clear();

        public NativeArray<T> AsArray()
        {
            var result = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(__value->Ptr, __value->Length, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref result, AtomicSafetyHandle.GetTempMemoryHandle());
#endif

            return result;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void __CheckBounds(int index)
        {
            if (index < 0 || index >= length)
                throw new IndexOutOfRangeException();
        }
    }

    internal sealed class UnsafeListExDebugView<T> where T : unmanaged
    {
        UnsafeListEx<T> data;

        public UnsafeListExDebugView(UnsafeListEx<T> data)
        {
            this.data = data;
        }

        public T[] Items
        {
            get
            {
                return data.AsArray().ToArray();
            }
        }
    }
}