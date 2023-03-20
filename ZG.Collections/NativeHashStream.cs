using System;
using Unity.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ZG
{
    public struct NativeHashStream<T> where T : unmanaged, IEquatable<T>
    {
        public struct Reader
        {
            [ReadOnly]
            private NativeParallelHashMap<T, int> __foreachIndices;
            [ReadOnly]
            private NativeStream.Reader __reader;

            public int remainingItemCount => __reader.RemainingItemCount;

            public NativeArray<T> GetKeys(Allocator allocator)
            {
                return __foreachIndices.GetKeyArray(allocator);
            }

            public Reader(NativeHashStream<T> stream)
            {
                __foreachIndices = stream.__foreachIndices;
                __reader = stream.__stream.AsReader();
            }

            public void Begin(T value)
            {
                __reader.BeginForEachIndex(__foreachIndices[value]);
            }

            public bool TryBegin(T value)
            {
                if (__foreachIndices.TryGetValue(value, out int index))
                {
                    __reader.BeginForEachIndex(index);

                    return true;
                }

                return false;
            }

            public void End()
            {
                __reader.EndForEachIndex();
            }

            public ref U Read<U>() where U : unmanaged
            {
                return ref __reader.Read<U>();
            }

            public unsafe void* Read(int size)
            {
                return __reader.ReadUnsafePtr(size);
            }
        }
        
        public struct Writer
        {
            [NativeDisableUnsafePtrRestriction]
            private unsafe int* __index;
            [WriteOnly]
            private NativeParallelHashMap<T, int>.ParallelWriter __foreachIndices;
            [WriteOnly, NativeDisableParallelForRestriction]
            private NativeStream.Writer __writer;

            public unsafe Writer(NativeHashStream<T> stream)
            {
                __index = stream.__index;
                __foreachIndices = stream.__foreachIndices.AsParallelWriter();
                __writer = stream.__stream.AsWriter();
            }

            public unsafe void Begin(T value)
            {
                int source, destination;
                do
                {
                    source = *__index;
                    destination = source + 1;
                } while (System.Threading.Interlocked.CompareExchange(ref *__index, destination, source) != source);

                __foreachIndices.TryAdd(value, source);

                __writer.BeginForEachIndex(source);
            }

            public unsafe bool TryBegin(T value)
            {
                int source, destination;
                do
                {
                    source = *__index;
                    destination = source + 1;
                } while (System.Threading.Interlocked.CompareExchange(ref *__index, destination, source) == destination);

                if (!__foreachIndices.TryAdd(value, source))
                    return false;

                __writer.BeginForEachIndex(source);

                return true;
            }

            public void End()
            {
                __writer.EndForEachIndex();
            }

            public void Write<U>(U value) where U : unmanaged
            {
                __writer.Write<U>(value);
            }
        }


        [Unity.Burst.BurstCompile]
        private struct DisposeJob : IJob
        {
            [NativeDisableUnsafePtrRestriction]
            public unsafe void* ptr;
            public Allocator allocator;

            public unsafe void Execute()
            {
                UnsafeUtility.Free(ptr, allocator);
            }
        }

        private Allocator __allocator;
        [NativeDisableUnsafePtrRestriction]
        private unsafe int* __index;

        private NativeParallelHashMap<T, int> __foreachIndices;
        private NativeStream __stream;

        public int foreachCount => __stream.ForEachCount;
        
        public NativeArray<T> GetKeys(Allocator allocator)
        {
            return __foreachIndices.GetKeyArray(allocator);
        }

        public unsafe NativeHashStream(int count, Allocator allocator)
        {
            __allocator = allocator;
            __index = (int*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<int>(), UnsafeUtility.AlignOf<int>(), allocator);
            *__index = 0;

            __foreachIndices = new NativeParallelHashMap<T, int>(count, allocator);
            __stream = new NativeStream(count, allocator);
        }

        public unsafe void Reset(int count)
        {
            *__index = 0;

            __foreachIndices.Clear();
            __foreachIndices.Capacity = Math.Max(__foreachIndices.Capacity, count);

            __stream.Dispose();
            __stream = new NativeStream(count, __allocator);
        }

        public unsafe void Dispose()
        {
            __foreachIndices.Dispose();
            __stream.Dispose();

            UnsafeUtility.Free(__index, __allocator);

            __index = null;
        }

        public unsafe JobHandle Dispose(JobHandle inputDeps)
        {
            DisposeJob disposeJob;
            disposeJob.ptr = __index;
            disposeJob.allocator = __allocator;
            inputDeps = JobHandle.CombineDependencies(__foreachIndices.Dispose(inputDeps), __stream.Dispose(inputDeps), disposeJob.Schedule(inputDeps));

            __index = null;

            return inputDeps;
        }

        public Reader reader => new Reader(this);

        public Writer writer => new Writer(this);
    }
}