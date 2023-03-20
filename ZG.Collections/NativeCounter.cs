using System;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ZG
{
    [StructLayout(LayoutKind.Sequential)]
    [NativeContainer]
    public unsafe struct NativeCounter : IDisposable
    {
        [Unity.Burst.BurstCompile]
        internal struct DisposeJob : IJob
        {
            // Copy of the pointer from the full NativeCounter
            [NativeDisableUnsafePtrRestriction]
            public int* ptr;

            // Keep track of where the memory for this was allocated
            public Allocator allocator;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            public void Execute()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.Release(m_Safety);
#endif

                UnsafeUtility.Free(ptr, allocator);
            }
        }

        [NativeContainer]
        // This attribute is what makes it possible to use NativeCounter.Concurrent in a ParallelFor job
        [NativeContainerIsAtomicWriteOnly]
        unsafe public struct Concurrent
        {
            // Copy of the AtomicSafetyHandle from the full NativeCounter. The dispose sentinel is not copied since this inner struct does not own the memory and is not responsible for freeing it
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            // Copy of the pointer from the full NativeCounter
            [NativeDisableUnsafePtrRestriction]
            private int* __counter;

            // This is what makes it possible to assign to NativeCounter.Concurrent from NativeCounter
            public static implicit operator Concurrent(NativeCounter cnt)
            {
                Concurrent concurrent;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                //AtomicSafetyHandle.CheckWriteAndThrow(cnt.m_Safety);
                concurrent.m_Safety = cnt.m_Safety;
                AtomicSafetyHandle.UseSecondaryVersion(ref concurrent.m_Safety);
#endif

                concurrent.__counter = cnt._ptr;
                return concurrent;
            }

            public int Decrement()
            {
                // Increment still needs to check for write permissions
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
                // The actual increment is implemented with an atomic since it can be incremented by multiple threads at the same time
                return Interlocked.Decrement(ref *__counter);
            }

            public int Increment()
            {
                // Increment still needs to check for write permissions
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
                // The actual increment is implemented with an atomic since it can be incremented by multiple threads at the same time
                return Interlocked.Increment(ref *__counter);
            }

            public int Add(int value)
            {
                // Increment still needs to check for write permissions
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
                // The actual increment is implemented with an atomic since it can be incremented by multiple threads at the same time
                return Interlocked.Add(ref *__counter, value);
            }
        }

        // The actual pointer to the allocated count needs to have restrictions relaxed so jobs can be schedled with this container
        [NativeDisableUnsafePtrRestriction]
        internal int* _ptr;

        // Keep track of where the memory for this was allocated
        internal Allocator _allocator;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
        // The dispose sentinel tracks memory leaks. It is a managed type so it is cleared to null when scheduling a job
        // The job cannot dispose the container, and no one else can dispose it until the job has run so it is ok to not pass it along
        // This attribute is required, without it this native container cannot be passed to a job since that would give the job access to a managed object
        [NativeSetClassTypeToNullOnSchedule]
        internal DisposeSentinel m_DisposeSentinel;
#endif

        public bool isCreated
        {
            get
            {
                return _ptr != null;
            }
        }

        public int count
        {
            get
            {
                // Verify that the caller has read permission on this data.
                // This is the race condition protection, without these checks the AtomicSafetyHandle is useless
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                return *_ptr;
            }

            set
            {
                // Verify that the caller has write permission on this data. This is the race condition protection, without these checks the AtomicSafetyHandle is useless
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
                *_ptr = value;
            }
        }

        public NativeCounter(Allocator allocator)
        {
            _allocator = allocator;

            // Allocate native memory for a single integer
            _ptr = (int*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<int>(), UnsafeUtility.AlignOf<int>(), allocator);

            // Create a dispose sentinel to track memory leaks. This also creates the AtomicSafetyHandle
#if ENABLE_UNITY_COLLECTIONS_CHECKS
#if UNITY_2018_3_OR_NEWER
            DisposeSentinel.Create(out m_Safety, out m_DisposeSentinel, 0, allocator);
#else
            DisposeSentinel.Create(out m_Safety, out m_DisposeSentinel, 0);
#endif
#endif
            // Initialize the count to 0 to avoid uninitialized data
            count = 0;
        }

        public int Increment()
        {
            // Verify that the caller has write permission on this data.
            // This is the race condition protection, without these checks the AtomicSafetyHandle is useless
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            return ++(*_ptr);
        }

        public int Decrement()
        {
            // Verify that the caller has write permission on this data.
            // This is the race condition protection, without these checks the AtomicSafetyHandle is useless
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            return --(*_ptr);
        }

        public void Dispose()
        {
            // Let the dispose sentinel know that the data has been freed so it does not report any memory leaks
#if ENABLE_UNITY_COLLECTIONS_CHECKS
#if UNITY_2018_3_OR_NEWER
            DisposeSentinel.Dispose(ref m_Safety, ref m_DisposeSentinel);
#else
            DisposeSentinel.Dispose(m_Safety, ref m_DisposeSentinel);
#endif
#endif

            UnsafeUtility.Free(_ptr, _allocator);
            _ptr = null;
        }

        public unsafe JobHandle Dispose(in JobHandle inputDeps)
        {
            DisposeJob disposeJob;
            disposeJob.ptr = _ptr;
            disposeJob.allocator = _allocator;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            disposeJob.m_Safety = m_Safety;

            DisposeSentinel.Clear(ref m_DisposeSentinel);
#endif
            return disposeJob.Schedule(inputDeps);
        }
    }

    public unsafe struct NativeCounterLite : IDisposable
    {
        unsafe public struct Concurrent
        {
            // Copy of the AtomicSafetyHandle from the full NativeCounter. The dispose sentinel is not copied since this inner struct does not own the memory and is not responsible for freeing it
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            // Copy of the pointer from the full NativeCounter
            [NativeDisableUnsafePtrRestriction]
            private int* __counter;

            // This is what makes it possible to assign to NativeCounter.Concurrent from NativeCounter
            public static implicit operator Concurrent(NativeCounterLite cnt)
            {
                Concurrent concurrent;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                //AtomicSafetyHandle.CheckWriteAndThrow(cnt.m_Safety);
                concurrent.m_Safety = cnt.m_Safety;
                AtomicSafetyHandle.UseSecondaryVersion(ref concurrent.m_Safety);
#endif

                concurrent.__counter = cnt.__ptr;
                return concurrent;
            }

            public int Decrement()
            {
                // Increment still needs to check for write permissions
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
                // The actual increment is implemented with an atomic since it can be incremented by multiple threads at the same time
                return Interlocked.Decrement(ref *__counter);
            }

            public int Increment()
            {
                // Increment still needs to check for write permissions
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
                // The actual increment is implemented with an atomic since it can be incremented by multiple threads at the same time
                return Interlocked.Increment(ref *__counter);
            }

            public int Add(int value)
            {
                // Increment still needs to check for write permissions
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
                // The actual increment is implemented with an atomic since it can be incremented by multiple threads at the same time
                return Interlocked.Add(ref *__counter, value);
            }
        }

        // The actual pointer to the allocated count needs to have restrictions relaxed so jobs can be schedled with this container
        [NativeDisableUnsafePtrRestriction]
        private int* __ptr;

        // Keep track of where the memory for this was allocated
        private Allocator __allocator;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif

        public bool isCreated
        {
            get
            {
                return __ptr != null;
            }
        }

        public int count
        {
            get
            {
                // Verify that the caller has read permission on this data.
                // This is the race condition protection, without these checks the AtomicSafetyHandle is useless
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                return *__ptr;
            }

            set
            {
                // Verify that the caller has write permission on this data. This is the race condition protection, without these checks the AtomicSafetyHandle is useless
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
                *__ptr = value;
            }
        }

        public NativeCounterLite(Allocator allocator)
        {
            __allocator = allocator;

            // Allocate native memory for a single integer
            __ptr = (int*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<int>(), UnsafeUtility.AlignOf<int>(), allocator);

            // Create a dispose sentinel to track memory leaks. This also creates the AtomicSafetyHandle
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = AtomicSafetyHandle.Create();
#endif
            // Initialize the count to 0 to avoid uninitialized data
            count = 0;
        }

        public int Increment()
        {
            // Verify that the caller has write permission on this data.
            // This is the race condition protection, without these checks the AtomicSafetyHandle is useless
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            return ++(*__ptr);
        }

        public int Decrement()
        {
            // Verify that the caller has write permission on this data.
            // This is the race condition protection, without these checks the AtomicSafetyHandle is useless
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            return --(*__ptr);
        }

        public void Dispose()
        {
            // Let the dispose sentinel know that the data has been freed so it does not report any memory leaks
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(m_Safety);
#endif

            UnsafeUtility.Free(__ptr, __allocator);
            __ptr = null;
        }

        public static unsafe implicit operator NativeCounter(in NativeCounterLite value)
        {
            NativeCounter result = default;
            result._ptr = value.__ptr;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            result.m_Safety = value.m_Safety;
#endif

            result._allocator = Allocator.None;

            return result;
        }

    }
}