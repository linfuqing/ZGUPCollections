using System.Runtime.CompilerServices;
using System;
using System.Threading;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Burst;
using Unity.Jobs;
using Math = ZG.Mathematics.Math;

namespace ZG
{
    public struct NativeFactoryEnumerable : IEnumerable<NativeFactoryObject>
    {
        internal struct Chunks
        {
            public int mutex;
            public UnsafeList<NativeFactoryChunk> values;

            public AllocatorManager.AllocatorHandle allocator => values.Allocator;

            public int count => values.Length;

            public ref NativeFactoryChunk this[int index]
            {
                get => ref values.ElementAt(index);
            }

            public Chunks(AllocatorManager.AllocatorHandle allocator)
            {
                mutex = 0;

                values = new UnsafeList<NativeFactoryChunk>(0, allocator);
            }

            public void Dispose()
            {
                UnityEngine.Assertions.Assert.AreEqual(0, mutex);

                values.Dispose();
            }

            public int Alloc(in NativeFactoryChunk chunk)
            {
                int chunkIndex = values.Length;

                __CheckChunkIndex(chunkIndex);

                if (chunkIndex < values.Capacity)
                    values.AddNoResize(chunk);
                else
                {
                    int mutex;
                    do
                    {
                        mutex = this.mutex;
                    } while (mutex > 0 || Interlocked.CompareExchange(ref this.mutex, mutex - 1, mutex) != mutex);

                    values.Add(chunk);

                    Interlocked.Increment(ref this.mutex);
                }

                return chunkIndex;
            }

            public void Lock()
            {
                int mutex;
                do
                {
                    mutex = this.mutex;
                } while (mutex < 0 || Interlocked.CompareExchange(ref this.mutex, mutex + 1, mutex) != mutex);
            }

            public void Unlock()
            {
                Interlocked.Decrement(ref mutex);
            }
        }

        [BurstCompile]
        private unsafe struct ClearJob : IJob
        {
            [NativeDisableUnsafePtrRestriction]
            public NativeFactoryEnumerable* enumerable;

            public void Execute()
            {
                //enumerable->__CheckMutex();

                enumerable->__head = CHUNK_HANDLE_NULL;
            }
        }

        [BurstCompile]
        private unsafe struct ParallelClearJob : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction]
            public Chunks* chunks;

            public void Execute(int index)
            {
                ref var chunks = ref this.chunks[index];

                int numChunks = chunks.count;
                for (int i = 0; i < numChunks; ++i)
                {
                    ref var chunk = ref chunks[i];

                    chunk.status = NativeFactoryChunk.STATUS_DETACH;
                    chunk.count = 0;
                    chunk.flag = 0;
                    chunk.next = CHUNK_HANDLE_NULL;
                }
            }
        }

        [BurstCompile]
        private unsafe struct ParallelDisposeJob : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction]
            public Chunks* chunks;

            public void Execute(int index)
            {
                ref var chunks = ref this.chunks[index];

                int numChunks = chunks.count;
                for (int i = 0; i < numChunks; ++i)
                {
                    ref var chunk = ref chunks[i];

                    AllocatorManager.Free(chunks.allocator, chunk.values);

                    chunk.values = null;
                }

                chunks.Dispose();
            }
        }

        [BurstCompile]
        private unsafe struct DisposeJob : IJob
        {
            public AllocatorManager.AllocatorHandle allocator;

            [NativeDisableUnsafePtrRestriction]
            public NativeFactoryEnumerable* enumerable;

            public void Execute()
            {
                //enumerable->__CheckMutex();

                AllocatorManager.Free(allocator, enumerable->__chunks, enumerable->ThreadCount);
            }
        }

        public struct Enumerator : IEnumerator<NativeFactoryObject>
        {
            private int __entityIndex;
            private int __chunkHandle;

            [NativeDisableUnsafePtrRestriction]
            private unsafe NativeFactoryEnumerable* __enumerable;

            public unsafe NativeFactoryObject Current
            {
                get
                {
                    return new NativeFactoryObject(
                        ref UnsafeUtility.AsRef<NativeFactoryEnumerable>(__enumerable),
                        __chunkHandle,
                        __entityIndex);
                }
            }

            internal unsafe Enumerator(int chunkHandle, ref NativeFactoryEnumerable enumerable)
            {
                enumerable.__CheckEndlessLoop();

                __entityIndex = -1;
                __chunkHandle = chunkHandle;
                __enumerable = (NativeFactoryEnumerable*)UnsafeUtility.AddressOf(ref enumerable);
            }

            public unsafe ref T As<T>() where T : struct
            {
                return ref __enumerable->_As<T>(__chunkHandle, __entityIndex).value;
            }

            public unsafe bool MoveNext()
            {
                if (__enumerable == null)
                    return false;

                int numEntities;
                while (__chunkHandle != CHUNK_HANDLE_NULL)
                {
                    var chunk = __enumerable->__GetChunk(__chunkHandle);

                    UnityEngine.Assertions.Assert.AreNotEqual(0, chunk->count);
                    UnityEngine.Assertions.Assert.AreNotEqual(0, chunk->flag);

                    if (__entityIndex == -1)
                        __entityIndex = Math.GetLowerstBit(chunk->flag) - 2;// 30 - math.lzcnt(chunk->flag ^ (chunk->flag - 1));

                    numEntities = Math.GetHighestBit(chunk->flag);// 32 - math.lzcnt(chunk->flag);
                    while (++__entityIndex < numEntities)
                    {
                        if ((chunk->flag & (1 << __entityIndex)) != 0)
                            return true;
                    }

                    __entityIndex = -1;

                    __chunkHandle = chunk->next;
                }

                return false;
            }

            public unsafe void Reset()
            {
                __entityIndex = -1;
                __chunkHandle = __enumerable->__head;
            }

            void IDisposable.Dispose()
            {

            }

            object IEnumerator.Current => Current;
        }

        public const int CHUNK_HANDLE_NULL = 0;

        public static readonly int ThreadShift = 1;
        public static readonly int ChunkShift = Math.GetHighestBit(JobsUtility.MaxJobThreadCount - 1)/*32 - math.lzcnt(JobsUtility.MaxJobThreadCount - 1)*/ + ThreadShift;
        public static readonly int ThreadMask = (1 << (ChunkShift - ThreadShift)) - 1;
        public static readonly int MaxChunkCount = int.MaxValue >> ChunkShift;

        public readonly int ThreadCount;

        //private volatile int __mutex;
        private int __head;
        private unsafe Chunks* __chunks;

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static unsafe void __CheckHandle(int handle)
        {
            if (handle <= CHUNK_HANDLE_NULL)
                throw new IndexOutOfRangeException();
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static unsafe void __CheckHandleUnsafe(int handle)
        {
            if (handle < CHUNK_HANDLE_NULL)
                throw new IndexOutOfRangeException();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ToChunkHandle(int threadIndex, int chunkIndex)
        {
            return (chunkIndex << ChunkShift) | (threadIndex << ThreadShift) | 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void FromChunkHandle(int handle, out int threadIndex, out int chunkIndex)
        {
            __CheckHandle(handle);

            threadIndex = (handle >> ThreadShift) & ThreadMask;
            chunkIndex = handle >> ChunkShift;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SafeChunkHandle(int handle)
        {
            __CheckHandleUnsafe(handle);

            return -handle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int AbsChunkHandle(int handle)
        {
            handle = math.abs(handle);

            __CheckHandleUnsafe(handle);

            return handle;
        }

        public int innerloopBatchCount => ThreadCount;// > 16 ? ThreadCount >> 4 : 1;

        public AllocatorManager.AllocatorHandle allocator => _GetChunks(0).allocator;

        public unsafe NativeFactoryEnumerable(AllocatorManager.AllocatorHandle allocator, bool isParallelWritable = false)
        {
            ThreadCount = isParallelWritable ? JobsUtility.MaxJobThreadCount : 1;
            __chunks = AllocatorManager.Allocate<Chunks>(allocator, ThreadCount);

            for (int i = 0; i < ThreadCount; ++i)
                __chunks[i] = new Chunks(allocator);

            //__mutex = 0;
            __head = CHUNK_HANDLE_NULL;
        }

        public unsafe void Dispose()
        {
            var allocator = this.allocator;

            ParallelDisposeJob parallelDisposeJob;
            parallelDisposeJob.chunks = __chunks;
            for (int i = 0; i < ThreadCount; ++i)
                parallelDisposeJob.Execute(i);

            DisposeJob disposeJob;
            disposeJob.allocator = allocator;
            disposeJob.enumerable = (NativeFactoryEnumerable*)UnsafeUtility.AddressOf(ref this);
            disposeJob.Execute();
        }

        public unsafe JobHandle Dispose(in JobHandle inputDeps)
        {
            var allocator = this.allocator;

            ParallelDisposeJob parallelDisposeJob;
            parallelDisposeJob.chunks = __chunks;
            var jobHandle = parallelDisposeJob.Schedule(ThreadCount, innerloopBatchCount, inputDeps);

            DisposeJob disposeJob;
            disposeJob.allocator = allocator;
            disposeJob.enumerable = (NativeFactoryEnumerable*)UnsafeUtility.AddressOf(ref this);
            return disposeJob.Schedule(jobHandle);
        }

        public unsafe JobHandle Clear(in JobHandle inputDeps)
        {
            ClearJob clear;
            clear.enumerable = (NativeFactoryEnumerable*)UnsafeUtility.AddressOf(ref this);
            var jobHandle = clear.Schedule(inputDeps);

            ParallelClearJob parallelClearJob;
            parallelClearJob.chunks = __chunks;
            return JobHandle.CombineDependencies(jobHandle, parallelClearJob.Schedule(ThreadCount, innerloopBatchCount, inputDeps));
        }

        public unsafe void Clear()
        {
            NativeFactoryChunk* chunk;
            while (__head != CHUNK_HANDLE_NULL)
            {
                chunk = __GetChunk(__head);

                __head = chunk->next;

                UnityEngine.Assertions.Assert.AreEqual(NativeFactoryChunk.STATUS_ATTACH, chunk->status);

                chunk->status = NativeFactoryChunk.STATUS_DETACH;
                chunk->count = 0;
                chunk->flag = 0;
                chunk->next = CHUNK_HANDLE_NULL;
            }
        }

        public unsafe int Count()
        {
            int count = 0, handle = __head;
            NativeFactoryChunk* chunk;
            while (handle != CHUNK_HANDLE_NULL)
            {
                chunk = __GetChunk(handle);

                count += chunk->count;

                handle = chunk->next;
            }

            return count;
        }

        public unsafe void Free(int chunkHandle, int entityIndex, int version)
        {
            FromChunkHandle(chunkHandle, out int threadIndex, out int chunkIndex);

            ref var chunks = ref _GetChunks(threadIndex);

            chunks.Lock();

            ref var chunk = ref chunks[chunkIndex];

            var result = chunk.Free(entityIndex, version);

            __CheckResult(result);

            if ((result & NativeFactoryChunk.FreeResult.Empty) == NativeFactoryChunk.FreeResult.Empty)
                __Detach(chunkHandle, ref chunk);

            chunks.Unlock();
        }

        public unsafe bool TryPopUnsafe<T>(out T value) where T : struct
        {
            if (__head != CHUNK_HANDLE_NULL)
            {
                var chunk = __GetChunk(__head);

                var result = chunk->PopUnsafe(out int index);
                if ((result & NativeFactoryChunk.FreeResult.Empty) == NativeFactoryChunk.FreeResult.Empty)
                {
                    __head = chunk->next;

                    chunk->next = CHUNK_HANDLE_NULL;
                    chunk->status = NativeFactoryChunk.STATUS_DETACH;
                }

                UnityEngine.Assertions.Assert.AreEqual(NativeFactoryChunk.FreeResult.OK, result & NativeFactoryChunk.FreeResult.OK);

                value = chunk->As<T>(index).value;

                return true;
            }

            value = default;

            return false;
        }

        public unsafe NativeFactoryObject Create<T>(int threadIndex)
        {
            ref var chunks = ref _GetChunks(threadIndex);

            int stride = UnsafeUtility.SizeOf<NativeFactoryEntity<T>>(),
                numChunks = chunks.count,
                chunkHandle, 
                entityIndex;
            NativeFactoryChunk.AllocResult result;
            for (int i = numChunks - 1; i >= 0; --i)
            {
                ref var chunk = ref chunks[i];
                if (chunk.stride != stride || chunk.count == 32)
                    continue;

                result = chunk.Alloc(out entityIndex);
                if ((result & NativeFactoryChunk.AllocResult.OK) == NativeFactoryChunk.AllocResult.OK)
                {
                    chunkHandle = ToChunkHandle(threadIndex, i);
                    if ((result & NativeFactoryChunk.AllocResult.Init) == NativeFactoryChunk.AllocResult.Init)
                        __Attach(chunkHandle, ref chunk);

                    return new NativeFactoryObject(ref this, chunkHandle, entityIndex, chunk.GetVersion(entityIndex));
                }
            }

            {
                NativeFactoryChunk chunk;
                chunk.status = NativeFactoryChunk.STATUS_DETACH;
                chunk.flag = 1;
                chunk.count = 1;
                chunk.stride = stride;
                chunk.next = CHUNK_HANDLE_NULL;

                chunk.values = AllocatorManager.Allocate(chunks.allocator, stride, UnsafeUtility.AlignOf<NativeFactoryEntity<T>>(), 32);
                UnsafeUtility.MemClear(chunk.values, stride * 32L);
                int chunkIndex = chunks.Alloc(chunk);

                chunkHandle = ToChunkHandle(threadIndex, chunkIndex);

                __Attach(chunkHandle, ref chunks[chunkIndex]);
            }

            /*int chunkIndex = chunks.Length;

            __CheckChunkIndex(chunkIndex);

            if (chunkIndex < chunks.Capacity)
                chunks.AddNoResize(default);
            else
            {
                __CheckEndlessLoop();

                int mutex;
                do
                {
                    mutex = __mutex;
                } while (mutex > 0 || Interlocked.CompareExchange(ref __mutex, mutex - 1, mutex) != mutex);

                chunks.Add(default);

                Interlocked.Increment(ref __mutex);
            }

            chunkHandle = ToChunkHandle(threadIndex, chunkIndex);
            {
                ref var chunk = ref chunks.ElementAt(chunkIndex);
                chunk.flag = 1;
                chunk.count = 1;
                chunk.stride = stride;
                chunk.next = CHUNK_HANDLE_NULL;

                chunk.values = AllocatorManager.Allocate(chunks.Allocator, stride, UnsafeUtility.AlignOf<NativeFactoryEntity<T>>(), 32);
                UnsafeUtility.MemClear(chunk.values, stride * 32L);

                __Attach(chunkHandle, ref chunk);
            }*/

            return new NativeFactoryObject(ref this, chunkHandle, 0, 0);
        }

        public int CountOf(int threadIndex)
        {
            ref var chunks = ref _GetChunks(threadIndex);

            int count = 0, numChunks = chunks.count, i;
            for (i = 0; i < numChunks; ++i)
            {
                ref var chunk = ref chunks[i];

                count += chunk.count;
            }

            return count;
        }

        public ref T As<T>(int threadIndex, int chunkIndex, int entityIndex) where T : struct
        {
            return ref _As<T>(threadIndex, chunkIndex, entityIndex).value;
        }

        public ref T As<T>(int chunkHandle, int entityIndex) where T : struct
        {
            return ref _As<T>(chunkHandle, entityIndex).value;
        }

        public unsafe int GetVersion(int chunkHandle, int entityIndex)
        {
            return __GetChunk(chunkHandle)->GetVersion(entityIndex);
        }

        public unsafe bool IsVail(int chunkHandle, int entityIndex, int version)
        {
            if (__chunks == null)
                return false;

            FromChunkHandle(chunkHandle, out int threadIndex, out int chunkIndex);

            if (threadIndex >= ThreadCount)
                return false;

            ref var chunks = ref _GetChunks(threadIndex);

            if (chunkIndex >= chunks.count)
                return false;

            var chunk = chunks[chunkIndex];// __GetChunk(ref chunks, chunkIndex);

            if ((chunk.flag & (1 << entityIndex)) == 0)
                return false;

            return chunk.GetVersion(entityIndex) == version;
        }

        public Enumerator GetEnumerator() => new Enumerator(__head, ref this);

        internal ref NativeFactoryEntity<T> _As<T>(int threadIndex, int chunkIndex, int entityIndex) where T : struct
        {
            ref var chunk = ref _GetChunk(threadIndex, chunkIndex);

            __CheckEntity(entityIndex, chunk);

            return ref chunk.As<T>(entityIndex);
        }

        internal ref NativeFactoryEntity<T> _As<T>(int chunkHandle, int entityIndex) where T : struct
        {
            FromChunkHandle(chunkHandle, out int threadIndex, out int chunkIndex);

            return ref _As<T>(threadIndex, chunkIndex, entityIndex);
        }

        internal unsafe void* _Get(int threadIndex, int chunkIndex, int entityIndex, int version, out int stride)
        {
            ref var chunk = ref _GetChunk(threadIndex, chunkIndex);

            __CheckEntity(entityIndex, chunk);

            __CheckEntityVersion(chunk, entityIndex, version);

            stride = chunk.stride;

            return chunk.Get(entityIndex);
        }

        internal unsafe void* _Get(int chunkHandle, int entityIndex, int version, out int stride)
        {
            FromChunkHandle(chunkHandle, out int threadIndex, out int chunkIndex);

            return _Get(threadIndex, chunkIndex, entityIndex, version, out stride);
        }

        internal unsafe ref Chunks _GetChunks(int threadIndex)
        {
            __CheckThread(threadIndex);

            return ref __chunks[threadIndex];
        }

        internal unsafe ref NativeFactoryChunk _GetChunk(int threadIndex, int chunkIndex)
        {
            ref var chunks = ref _GetChunks(threadIndex);

            return ref chunks[chunkIndex];
        }

        private unsafe NativeFactoryChunk* __GetChunk(int handle)
        {
            FromChunkHandle(handle, out int threadIndex, out int chunkIndex);

            return (NativeFactoryChunk*)UnsafeUtility.AddressOf(ref _GetChunk(threadIndex, chunkIndex));
        }

        /*private void __BeginChange()
        {
            int mutex;
            do
            {
                mutex = __mutex;
            } while (mutex < 0 || Interlocked.CompareExchange(ref __mutex, mutex + 1, mutex) != mutex);
        }

        private void __EndChange()
        {
            Interlocked.Decrement(ref __mutex);
        }*/

        private unsafe void __Attach(int handle, ref NativeFactoryChunk chunk)
        {
            UnityEngine.Assertions.Assert.AreNotEqual(NativeFactoryChunk.STATUS_ATTACHING, chunk.status);

            if (!chunk._SetStatus(
                NativeFactoryChunk.STATUS_ATTACHING, 
                NativeFactoryChunk.STATUS_DETACH, 
                NativeFactoryChunk.STATUS_ATTACH))
                return;

            //__BeginChange();

            int head;
            do
            {
                head = __head;

                chunk.next = SafeChunkHandle(head);
            } while (Interlocked.CompareExchange(ref __head, handle, head) != head);

            __CheckEndlessLoop(-1);

            //__EndChange();

            chunk.next = AbsChunkHandle(chunk.next);
            chunk.status = NativeFactoryChunk.STATUS_ATTACH;
        }

        private unsafe void __Detach(int handle, ref NativeFactoryChunk chunk)
        {
            UnityEngine.Assertions.Assert.AreNotEqual(NativeFactoryChunk.STATUS_DETACHING, chunk.status);

            if (!chunk._SetStatus(
                NativeFactoryChunk.STATUS_DETACHING, 
                NativeFactoryChunk.STATUS_ATTACH,
                NativeFactoryChunk.STATUS_DETACH))
                return;

            if (chunk.count > 0)
            {
                chunk.status = NativeFactoryChunk.STATUS_ATTACH;

                return;
            }

            int next;
            do
            {
                next = chunk.next;

            } while (Interlocked.CompareExchange(ref chunk.next, SafeChunkHandle(next), next) != next);

            //__BeginChange();

            int absHandle;//, head;
            NativeFactoryChunk* temp;
            while(true)
            {
                absHandle = Interlocked.CompareExchange(ref __head, next, handle);
                if (absHandle == handle)
                {
                    __CheckEndlessLoop(handle);

                    break;
                }

                UnityEngine.Assertions.Assert.AreNotEqual(CHUNK_HANDLE_NULL, absHandle);

                //head = absHandle;
                do
                {
                    temp = __GetChunk(absHandle);
                    absHandle = temp->next;
                    if (absHandle == CHUNK_HANDLE_NULL)
                        break;

                    absHandle = AbsChunkHandle(absHandle);

                } while (absHandle != handle/* && absHandle != head*/) ;

                absHandle = Interlocked.CompareExchange(ref temp->next, next, handle);
                if (absHandle == handle)
                {
                    __CheckEndlessLoop(handle);

                    break;
                }
            }

            //__EndChange();

            chunk.next = CHUNK_HANDLE_NULL;

            chunk.status = NativeFactoryChunk.STATUS_DETACH;
        }

        IEnumerator<NativeFactoryObject> IEnumerable<NativeFactoryObject>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        unsafe void __CheckEndlessLoop(int origin)
        {
            /*int head = __head, handle = head;
            while (handle != CHUNK_HANDLE_NULL)
            {
                if (handle == origin)
                {
                    __EndChange();

                    throw new InvalidProgramException();
                }

                handle = __GetChunk(handle)->next;

                handle = AbsChunkHandle(handle);
                if (handle == head)
                {
                    __EndChange();

                    throw new InvalidProgramException();
                }
            }*/
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        unsafe void __CheckEndlessLoop()
        {
            /*int head = __head, handle = head;
            while(handle != CHUNK_HANDLE_NULL)
            {
                handle = __GetChunk(handle)->next;
                handle = AbsChunkHandle(handle);
                if (handle == head)
                    throw new InvalidProgramException();
            }*/
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void __CheckThread(int index)
        {
            if (index >= ThreadCount)
                throw new IndexOutOfRangeException();
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void __CheckEntity(int index, in NativeFactoryChunk chunk)
        {
            if ((chunk.flag & (1 << index)) == 0)
                throw new IndexOutOfRangeException();
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void __CheckEntityVersion(in NativeFactoryChunk chunk, int index, int version)
        {
            if (chunk.GetVersion(index) != version)
                throw new InvalidOperationException();
        }

        /*[Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void __CheckMutex()
        {
            if (__mutex != 0)
                throw new InvalidOperationException();
        }*/

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void __CheckResult(NativeFactoryChunk.FreeResult result)
        {
            if ((result & NativeFactoryChunk.FreeResult.OK) != NativeFactoryChunk.FreeResult.OK)
                throw new IndexOutOfRangeException();
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static unsafe void __CheckChunkIndex(int chunkIndex)
        {
            if (chunkIndex >= MaxChunkCount)
                throw new InvalidOperationException();
        }

    }

    internal struct NativeFactoryChunk
    {
        [Flags]
        public enum AllocResult
        {
            OK = 0x01,
            Init = 0x02, 
            Fail = 0x04
        }

        [Flags]
        public enum FreeResult
        {
            OK = 0x01,
            Empty = 0x02,
            OutOfIndex = 0x04, 
            OutOfVersion = 0x08
        }

        public const int STATUS_DETACHING = -1;
        public const int STATUS_DETACH = 0;
        public const int STATUS_ATTACH = 1;
        public const int STATUS_ATTACHING = 2;

        public int status;
        public int flag;
        public int count;
        public int next;
        public int stride;
        public unsafe void* values;

        public unsafe void SetVersion(int index, int value)
        {
            UnsafeUtility.WriteArrayElementWithStride(values, index, stride, value);
        }

        public unsafe int GetVersion(int index)
        {
            return UnsafeUtility.ReadArrayElementWithStride<int>(values, index, stride);
        }

        public AllocResult Alloc(out int index)
        {
            int count = Interlocked.Increment(ref this.count);
            if (count > 32)
            {
                Interlocked.Decrement(ref this.count);

                index = -1;

                return AllocResult.Fail;
            }

            int origin;
            do
            {
                origin = flag;

                index = ~origin;
                index = Math.GetLowerstBit(index) - 1;// 31 - math.lzcnt(index ^ (index - 1));
            } while (Interlocked.CompareExchange(ref flag, origin | (1 << index), origin) != origin);

            var result = AllocResult.OK;
            if (count == 1)
                result |= AllocResult.Init;

            return result;
        }

        public FreeResult Free(int index)
        {
            int origin;
            do
            {
                origin = flag;
                if ((origin & (1 << index)) == 0)
                    return FreeResult.OutOfIndex;

            } while (Interlocked.CompareExchange(ref flag, origin & ~(1 << index), origin) != origin);

            if (Interlocked.Decrement(ref count) > 0)
                return FreeResult.OK;

            return FreeResult.OK | FreeResult.Empty;
        }

        public FreeResult Free(int index, int version)
        {
            if (GetVersion(index) != version)
                return FreeResult.OutOfVersion;

            var result = Free(index);
            if ((result & FreeResult.OK) == FreeResult.OK)
                SetVersion(index, version + 1);

            return result;
        }

        public FreeResult PopUnsafe(out int index)
        {
            if (flag != 0)
            {
                index = Math.GetHighestBit(flag) - 1;// 31 - math.lzcnt(flag);

                flag &= ~(1 << index);

                if (--count > 0)
                    return FreeResult.OK;

                return FreeResult.OK | FreeResult.Empty;
            }

            index = -1;

            return FreeResult.OutOfIndex;
        }

        public unsafe ref NativeFactoryEntity<T> As<T>(int index) where T : struct
        {
            __CheckSize<T>(stride);

            return ref UnsafeUtility.ArrayElementAsRef<NativeFactoryEntity<T>>(values, index);
        }

        public unsafe void* Get(int index)
        {
            return ((byte*)values) + index * stride + UnsafeUtility.SizeOf<int>();
        }

        internal unsafe bool _SetStatus(int destination, int source, int comparand)
        {
            int value;
            do
            {
                value = Interlocked.CompareExchange(ref status, destination, source);
                if (value == comparand)
                    return false;

            } while (value != source);

            return true;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void __CheckSize<T>(int stride)
        {
            if (stride != UnsafeUtility.SizeOf<NativeFactoryEntity<T>>())
                throw new InvalidOperationException();
        }
    }

    internal struct NativeFactoryEntity<T>
    {
//#pragma warning disable 649
        public int version;
        public T value;
    }

    public unsafe struct NativeFactoryObject
    {
        [NativeDisableUnsafePtrRestriction]
        private NativeFactoryEnumerable* __enumerable;
        private int __chunkHandle;
        private int __entityIndex;
        private int __version;

        public bool isCreated => __enumerable != null;

        public bool isVail
        {
            get
            {
                return __enumerable != null && __enumerable->IsVail(__chunkHandle, __entityIndex, __version);
            }
        }

        public int version
        {
            get
            {
                return __enumerable->GetVersion(__chunkHandle, __entityIndex);
            }
        }

        public readonly ref NativeFactoryEnumerable enumerable => ref *__enumerable;

        internal NativeFactoryObject(ref NativeFactoryEnumerable enumerable, int chunkHandle, int entityIndex, int version)
        {
            __enumerable = (NativeFactoryEnumerable*)UnsafeUtility.AddressOf(ref enumerable);
            __chunkHandle = chunkHandle;
            __entityIndex = entityIndex;
            __version = version;
        }

        internal NativeFactoryObject(ref NativeFactoryEnumerable enumerable, int chunkHandle, int entityIndex) : 
            this(ref enumerable, chunkHandle, entityIndex, enumerable.GetVersion(chunkHandle, entityIndex))
        {
        }

        public void Dispose()
        {
            __enumerable->Free(__chunkHandle, __entityIndex, __version);
        }

        public ref T As<T>() where T : struct
        {
            ref var entity = ref __enumerable->_As<T>(__chunkHandle, __entityIndex);

            __CheckVersion(__version, entity.version);

            return ref entity.value;
        }

        public void* GetUnsafePtr(out int stride)
        {
            return __enumerable->_Get(__chunkHandle, __entityIndex, __version, out stride);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void __CheckVersion(int source, int destination)
        {
            if (source != destination)
                throw new InvalidOperationException();
        }
    }

    public unsafe struct NativeFactoryObject<T> where T : struct
    {
        private NativeFactoryObject __value;

        public bool isVail => __value.isVail;

        public int version => __value.version;

        public ref T value => ref __value.As<T>();

        internal NativeFactoryObject(NativeFactoryObject value)
        {
            __value = value;
        }

        public void Dispose() => __value.Dispose();
    }

    public unsafe struct UnsafeFactory : IDisposable
    {
        [BurstCompile]
        private struct DisposeJob : IJob
        {
            public AllocatorManager.AllocatorHandle allocator;
            [NativeDisableUnsafePtrRestriction]
            public NativeFactoryEnumerable* enumerable;

            public void Execute()
            {
                AllocatorManager.Free(allocator, enumerable);
            }
        }

        public struct ParallelWriter
        {
            [NativeSetThreadIndex]
            internal int _threadIndex;

            [NativeDisableUnsafePtrRestriction]
            private readonly NativeFactoryEnumerable* __enumerable;

            public bool isCreated => __enumerable != null;

            internal ParallelWriter(NativeFactoryEnumerable* enumerable)
            {
                _threadIndex = 0;
                __enumerable = enumerable;
            }

            public unsafe NativeFactoryObject Create<T>()
            {
                return __enumerable->Create<T>(_threadIndex);
            }
        }

        public struct Thread : IEnumerable<NativeFactoryObject>
        {
            private readonly int __index;

            [NativeDisableUnsafePtrRestriction]
            private readonly NativeFactoryEnumerable* __enumerable;

            internal Thread(int index, NativeFactoryEnumerable* enumerable)
            {
                __index = index;
                __enumerable = enumerable;
            }

            public int Count() => __enumerable->CountOf(__index);

            public NativeArray<T> ToArray<T>(Allocator allocator) where T : struct
            {
                int index = 0;
                var results = new NativeArray<T>(Count(), allocator, NativeArrayOptions.UninitializedMemory);
                var enumerator = GetEnumerator();
                while (enumerator.MoveNext())
                    results[index++] = enumerator.As<T>();

                return results;
            }

            public ThreadEnumerator GetEnumerator()
            {
                return new ThreadEnumerator(__index, __enumerable);
            }

            IEnumerator<NativeFactoryObject> IEnumerable<NativeFactoryObject>.GetEnumerator() => GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public struct ThreadEnumerator : IEnumerator<NativeFactoryObject>
        {
            private int __entityIndex;
            private int __chunkIndex;

            private readonly int __index;

            [NativeDisableUnsafePtrRestriction]
            private readonly NativeFactoryEnumerable* __enumerable;

            public NativeFactoryObject Current
            {
                get
                {
                    return new NativeFactoryObject(
                        ref UnsafeUtility.AsRef<NativeFactoryEnumerable>(__enumerable), 
                        NativeFactoryEnumerable.ToChunkHandle(__index, __chunkIndex), 
                        __entityIndex);
                }
            }

            internal ThreadEnumerator(int index, NativeFactoryEnumerable* enumerable)
            {
                __entityIndex = -1;
                __chunkIndex = 0;

                __index = index;
                __enumerable = enumerable;
            }

            public ref T As<T>() where T : struct => ref __enumerable->As<T>(__index, __chunkIndex, __entityIndex);

            public bool MoveNext()
            {
                if (__enumerable == null)
                    return false;

                ref var chunks = ref __enumerable->_GetChunks(__index);
                int numChunks = chunks.count, numEntities;
                while (__chunkIndex < numChunks)
                {
                    ref var chunk = ref chunks[__chunkIndex];
                    if (chunk.count > 0)
                    {
                        UnityEngine.Assertions.Assert.AreNotEqual(0, chunk.flag);

                        numEntities = Math.GetHighestBit(chunk.flag);

                        if (__entityIndex == -1)
                            __entityIndex = Math.GetLowerstBit(chunk.flag) - 2;// 30 - math.lzcnt(chunk.flag ^ (chunk.flag - 1));

                        while (++__entityIndex < numEntities)
                        {
                            UnityEngine.Assertions.Assert.IsTrue(__entityIndex >= 0 && __entityIndex < numEntities);

                            if ((chunk.flag & (1 << __entityIndex)) != 0)
                                return true;
                        }

                        __entityIndex = -1;
                    }
                    ++__chunkIndex;
                }

                return false;
            }

            public void Reset()
            {
                __entityIndex = -1;
                __chunkIndex = 0;
            }

            void IDisposable.Dispose()
            {

            }

            object IEnumerator.Current => Current;

            /*[Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void __CheckChunk(in UnsafeList chunks)
            {
                if (__chunkIndex >= chunks.Length)
                    throw new IndexOutOfRangeException();
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void __CheckObject(in NativeFactoryChunk chunk)
            {
                if ((chunk.flag & (1 << __entityIndex)) == 0)
                    throw new IndexOutOfRangeException();
            }*/
        }

        [NativeDisableUnsafePtrRestriction]
        private NativeFactoryEnumerable* __enumerable;

        public bool isCreated => __enumerable != null;

        public readonly int length => __enumerable->ThreadCount;

        public int innerloopBatchCount => __enumerable->innerloopBatchCount;

        public AllocatorManager.AllocatorHandle allocator => __enumerable->allocator;

        public ref NativeFactoryEnumerable enumerable => ref UnsafeUtility.AsRef<NativeFactoryEnumerable>(__enumerable);

        public ParallelWriter parallelWriter => new ParallelWriter(__enumerable);

        public Thread this[int index]
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (index < 0 || index >= length)
                    throw new IndexOutOfRangeException();
#endif

                return new Thread(index, __enumerable);
            }
        }

        public UnsafeFactory(in AllocatorManager.AllocatorHandle allocator, bool isParallelWritable = false)
        {
            __enumerable = AllocatorManager.Allocate<NativeFactoryEnumerable>(allocator);

            *__enumerable = new NativeFactoryEnumerable(allocator, isParallelWritable);
        }

        public int Count()
        {
            return __enumerable->Count();
        }

        public int CountOf(int index)
        {
            return __enumerable->CountOf(index);
        }

        public void Dispose()
        {
            var allocator = this.allocator;

            __enumerable->Dispose();

            DisposeJob disposeJob;
            disposeJob.allocator = allocator;
            disposeJob.enumerable = __enumerable;
            disposeJob.Execute();
        }

        public JobHandle Dispose(in JobHandle inputDeps)
        {
            var allocator = this.allocator;

            var jobHandle = __enumerable->Dispose(inputDeps);

            DisposeJob disposeJob;
            disposeJob.allocator = allocator;
            disposeJob.enumerable = __enumerable;
            jobHandle = disposeJob.Schedule(jobHandle);

            __enumerable = null;

            return jobHandle;
        }

        public JobHandle Clear(in JobHandle inputDeps) => __enumerable->Clear(inputDeps);

        public void Clear() => __enumerable->Clear();

        public NativeFactoryObject Create<T>()
        {
            return __enumerable->Create<T>(0);
        }
    }

    [NativeContainer]
    public struct NativeFactory<T> : IDisposable where T : struct
    {
        [NativeContainer]
        [NativeContainerIsAtomicWriteOnly]
        public struct ParallelWriter
        {
            [NativeDisableUnsafePtrRestriction]
            internal UnsafeFactory.ParallelWriter _value;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;

            private static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<ParallelWriter>();
#endif

            internal ParallelWriter(ref NativeFactory<T> instance)
            {

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = instance.m_Safety;

                AtomicSafetyHandle.SetStaticSafetyId(ref m_Safety, StaticSafetyID.Data);
#endif

                _value = instance._value.parallelWriter;
            }

            public unsafe NativeFactoryObject<T> Create()
            {
                __CheckWrite();

                return new NativeFactoryObject<T>(_value.Create<T>());
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void __CheckWrite()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            }

        }

        public struct ThreadEnumerator : IEnumerator<NativeFactoryObject<T>>
        {
            internal UnsafeFactory.ThreadEnumerator _value;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif
            public NativeFactoryObject<T> Current
            {
                get
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                    return new NativeFactoryObject<T>(_value.Current);
                }
            }

            public T value
            {
                get
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                    return _value.As<T>();
                }
            }

            public bool MoveNext()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                return _value.MoveNext();
            }

            public void Reset()
            {
                _value.Reset();
            }

            void IDisposable.Dispose()
            {

            }

            object IEnumerator.Current => Current;
        }

        public struct Enumerator : IEnumerator<T>
        {
            private NativeFactoryEnumerable.Enumerator __enumerator;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            public T Current
            {
                get
                {
                    __CheckRead();

                    return __enumerator.Current.As<T>();
                }
            }

            public Enumerator(NativeFactory<T> factory)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                //AtomicSafetyHandle.CheckReadAndThrow(factory.m_Safety);
                m_Safety = factory.m_Safety;
                //AtomicSafetyHandle.UseSecondaryVersion(ref m_Safety);
                //AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, true);
#endif

                __enumerator = factory._value.enumerable.GetEnumerator();
            }

            public bool MoveNext() => __enumerator.MoveNext();

            public void Reset() => __enumerator.Reset();

            object IEnumerator.Current => Current;

            void IDisposable.Dispose()
            {

            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void __CheckRead()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            }

        }

        internal UnsafeFactory _value;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;

        private static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<NativeFactory<T>>();
#endif

        public bool isCreated => _value.isCreated;

        public unsafe ParallelWriter parallelWriter
        {
            get
            {
                return new ParallelWriter(ref this);
            }
        }

        public ThreadEnumerator this[int index]
        {
            get
            {
                ThreadEnumerator enumerator;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);

                enumerator.m_Safety = m_Safety;
#endif

                enumerator._value = _value[index].GetEnumerator();

                return enumerator;
            }
        }

        public unsafe NativeFactory(AllocatorManager.AllocatorHandle allocator, bool isParallelWritable = false)
        {
            _value = new UnsafeFactory(allocator, isParallelWritable);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = default;

            __Init(allocator);
#endif
        }

        public NativeFactory(UnsafeFactory value)
        {
            _value = value;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = default;

            __Init(value.allocator);
#endif
        }

        public unsafe JobHandle Dispose(JobHandle inputDeps)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckDeallocateAndThrow(m_Safety);

            AtomicSafetyHandle.Release(m_Safety);
#endif

            return _value.Dispose(inputDeps);
        }

        public unsafe void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckDeallocateAndThrow(m_Safety);

            AtomicSafetyHandle.Release(m_Safety);
#endif

            _value.Dispose();
        }

        public void Clear()
        {
            __CheckWrite();

            _value.Clear();
        }

        public unsafe NativeFactoryObject<T> Create()
        {
            __CheckWrite();

            return new NativeFactoryObject<T>(_value.Create<T>());
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void __Init(in AllocatorManager.AllocatorHandle allocator)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = CollectionHelper.CreateSafetyHandle(allocator);

            if (UnsafeUtility.IsNativeContainerType<T>())
                AtomicSafetyHandle.SetNestedContainer(m_Safety, true);

            CollectionHelper.SetStaticSafetyId<NativeFactory<T>>(ref m_Safety, ref StaticSafetyID.Data);

            AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, true);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void __CheckWrite()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
        }

    }

    public static class NativeFactoryUtility
    {
        [BurstCompile]
        private struct CountJob : IJobParallelFor
        {
            public UnsafeFactory factory;
            public NativeCounter.Concurrent counter;

            public void Execute(int index)
            {
                counter.Add(factory.CountOf(index));
            }
        }

        public static JobHandle CountOf(this in UnsafeFactory factory, NativeCounter.Concurrent counter, JobHandle inputDeps)
        {
            CountJob countJob;
            countJob.factory = factory;
            countJob.counter = counter;
            return countJob.Schedule(factory.length, factory.innerloopBatchCount, inputDeps);
        }
    }
}