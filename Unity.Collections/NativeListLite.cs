using System;
using System.Diagnostics;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ZG
{
    public unsafe struct NativeListLite<T> where T : unmanaged
    {
        [NativeDisableUnsafePtrRestriction]
        private UnsafeList<T>* __value;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif

        public bool isCreated => __value != null && __value->IsCreated;

        public bool IsEmpty
        {
            get
            {
                __CheckRead();

                return __value->IsEmpty;
            }
        }

        public int Length
        {
            get
            {
                __CheckRead();

                return __value->Length;
            }
        }

        public int Capacity
        {
            get
            {
                __CheckRead();

                return __value->Capacity;
            }

            set
            {
                __CheckWriteAndBumpSecondaryVersion();

                __value->SetCapacity(value);
            }
        }

        public AllocatorManager.AllocatorHandle allocatar => __value->Allocator;

        public unsafe T this[int index]
        {
            get
            {
                __CheckElementReadAccess(index);

                return UnsafeUtility.ReadArrayElement<T>(__value->Ptr, index);
            }

            set
            {
                __CheckElementWriteAccess(index);

                UnsafeUtility.WriteArrayElement(__value->Ptr, index, value);
            }
        }

        public NativeListLite(int capacity, Allocator allocator)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = AtomicSafetyHandle.Create();
#endif

            __value = UnsafeList<T>.Create(capacity, allocator);
        }

        public NativeListLite(Allocator allocator) : this(1, allocator)
        {

        }

        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(m_Safety);
#endif

            UnsafeList<T>.Destroy(__value);

            __value = null;
        }

        public void Resize(int length, NativeArrayOptions options)
        {
            __CheckWrite();

            __value->Resize(length, options);
        }

        public void ResizeUninitialized(int length) => Resize(length, NativeArrayOptions.UninitializedMemory);

        public void RemoveAt(int index)
        {
            __CheckWrite();

            __value->RemoveAt(index);
        }

        public void Add(in T value)
        {
            __CheckWrite();

            __value->Add(value);
        }

        public void AddNoResize(in T value)
        {
            __CheckWrite();

            __value->AddNoResize(value);
        }

        public void AddRange(in NativeArray<T> values)
        {
            __CheckWrite();

            __value->AddRange(values.GetUnsafeReadOnlyPtr(), values.Length);
        }

        public void Clear()
        {
            __CheckWrite();

            __value->Clear();
        }

        public NativeArray<T> AsArray()
        {
            var result = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(__value->Ptr, __value->Length, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref result, m_Safety);
#endif

            return result;
        }

        public NativeArray<T> AsDeferredJobArray()
        {
            /*#if ENABLE_UNITY_COLLECTIONS_CHECKS
                        AtomicSafetyHandle.CheckExistsAndThrow(m_Safety);
            #endif
                        byte* value = (byte*)__value;
                        // We use the first bit of the pointer to infer that the array is in list mode
                        // Thus the job scheduling code will need to patch it.
                        value += 1;
                        var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(value, 0, Allocator.None);

            #if ENABLE_UNITY_COLLECTIONS_CHECKS
                        NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, m_Safety);
            #endif

                        return array;*/

            return ((NativeList<T>)this).AsDeferredJobArrayEx();
        }

        public static unsafe implicit operator NativeList<T>(in NativeListLite<T> value)
        {
            NativeList<T> result = default;
            result.m_ListData = value.__value;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            result.m_Safety = value.m_Safety;
#endif

            return result;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private unsafe void __CheckRead()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private unsafe void __CheckWrite()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private unsafe void __CheckWriteAndBumpSecondaryVersion()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private unsafe void __CheckElementReadAccess(int index)
        {
            if (index < 0 || index >= __value->Length)
                __FailOutOfRangeError(index);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            int* ptr = (int*)(void*)m_Safety.versionNode;
            if (m_Safety.version != (*ptr & AtomicSafetyHandle.ReadCheck))
                AtomicSafetyHandle.CheckReadAndThrowNoEarlyOut(m_Safety);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private unsafe void __CheckElementWriteAccess(int index)
        {
            if (index < 0 || index >= __value->Length)
                __FailOutOfRangeError(index);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            int* ptr = (int*)(void*)m_Safety.versionNode;
            if (m_Safety.version != (*ptr & AtomicSafetyHandle.WriteCheck))
                AtomicSafetyHandle.CheckWriteAndThrowNoEarlyOut(m_Safety);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void __FailOutOfRangeError(int index)
        {
            throw new IndexOutOfRangeException($"Index {index} is out of range of '{__value->Length}' Length.");
        }
    }
}

namespace Unity.Collections
{ 
    public static partial class BugFixer
    {
        public unsafe static NativeArray<T> AsDeferredJobArrayEx<T>(this NativeList<T> instance) where T : unmanaged
        {
            return instance.AsDeferredJobArray();
        }

        /*public unsafe static void AddNoResizeEx<T>(this UnsafeList.ParallelWriter writer, in T value) where T : unmanaged
        {
            writer.AddNoResize(value);
        }*/

        public unsafe static void AddNoResizeEx<T>(this UnsafeList<T>.ParallelWriter writer, in T value) where T : unmanaged
        {
            writer.AddNoResize(value);
        }

        /// <summary>
        /// Adds an element to the list.
        /// </summary>
        /// <param name="value">The value to be added at the end of the list.</param>
        /// <remarks>
        /// If the list has reached its current capacity, internal array won't be resized, and exception will be thrown.
        /// </remarks>
        public unsafe static int AddNoResizeEx<T>(this NativeList<T>.ParallelWriter writer, in T value) where T : unmanaged
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(writer.m_Safety);
#endif
            var idx = Interlocked.Increment(ref writer.ListData->m_length) - 1;
            CheckSufficientCapacity(writer.ListData->Capacity, idx + 1);

            UnsafeUtility.WriteArrayElement(writer.ListData->Ptr, idx, value);

            return idx;
        }

        /// <summary>
        /// Adds elements from a buffer to this list.
        /// </summary>
        /// <param name="ptr">A pointer to the buffer.</param>
        /// <param name="length">The number of elements to add to the list.</param>
        /// <remarks>
        /// If the list has reached its current capacity, internal array won't be resized, and exception will be thrown.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if length is negative.</exception>
        public unsafe static void AddRangeNoResizeEx<T>(this NativeList<T>.ParallelWriter writer, void* ptr, int length) where T : unmanaged
        {
            writer.AddRangeNoResize(ptr, length);
        }

        /// <summary>
        /// Adds elements from a list to this list.
        /// </summary>
        /// <param name="list">Other container to copy elements from.</param>
        /// <remarks>
        /// If the list has reached its current capacity, internal array won't be resized, and exception will be thrown.
        /// </remarks>
        public unsafe static void AddRangeNoResize<T>(this NativeList<T>.ParallelWriter writer, in NativeArray<T> values) where T : unmanaged
        {
            writer.AddRangeNoResize(values.GetUnsafeReadOnlyPtr(), values.Length);
        }


        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckSufficientCapacity(int capacity, int length)
        {
            if (capacity < length)
                throw new Exception($"Length {length} exceeds capacity Capacity {capacity}");
        }

    }
}