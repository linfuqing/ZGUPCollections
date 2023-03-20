using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ZG.Unsafe
{
    public unsafe struct NativeLinkedListNode
    {
        public int version;

        public void* value;

        public NativeLinkedListNode* next;
    }
    
    public struct NativeLinkedListPool
    {
        private struct Chunk
        {
            public int capacity;
            public int count;

            public unsafe NativeLinkedListNode* nodes;
            public unsafe Chunk* next;
        }

        public struct Concurrent
        {
            [NativeDisableUnsafePtrRestriction]
            private unsafe NativeLinkedListPool* __pool;
            
            public unsafe Concurrent(NativeLinkedListPool* pool)
            {
                __pool = pool;
            }

            public unsafe NativeLinkedListNode* Allocate()
            {
                int nodeSize = UnsafeUtility.SizeOf<NativeLinkedListNode>(), count;
                NativeLinkedListNode* result, node;
                Chunk* chunk;
                while (true)
                {
                    node = (NativeLinkedListNode*)(void*)__pool->__nodes;
                    if (node == null)
                    {
                        while(true)
                        {
                            chunk = (Chunk*)(void*)__pool->__activedChunks;
                            if(chunk == null)
                            {
                                chunk = __Allocate(__pool->__itemSize, __pool->__capacity + (__pool->__capacity >> 1));

                                result = chunk->nodes;

                                chunk->count = 1;
                                
                                do
                                {
                                    chunk->next = (Chunk*)(void*)__pool->__activedChunks;
                                }
                                while (Interlocked.CompareExchange(ref __pool->__activedChunks, (IntPtr)(void*)chunk, (IntPtr)(void*)(chunk->next)) != (IntPtr)(void*)(chunk->next));

                                break;
                            }

                            count = Interlocked.Increment(ref chunk->count);
                            if (count > chunk->capacity)
                                Interlocked.Decrement(ref chunk->count);
                            else
                            {
                                result = (NativeLinkedListNode*)((byte)chunk->nodes + (nodeSize + __pool->__itemSize) * (count - 1));

                                if (count == chunk->capacity)
                                {
                                    __pool->__activedChunks = (IntPtr)(void*)chunk->next;

                                    do
                                    {
                                        chunk->next = (Chunk*)(void*)__pool->__deactivedChunks;
                                    }
                                    while (Interlocked.CompareExchange(ref __pool->__deactivedChunks, (IntPtr)(void*)chunk, (IntPtr)(void*)(chunk->next)) != (IntPtr)(void*)(chunk->next));
                                }

                                break;
                            }
                        }

                        break;
                    }

                    if (Interlocked.CompareExchange(ref __pool->__nodes, (IntPtr)(void*)node->next, (IntPtr)(void*)node) == (IntPtr)(void*)node)
                    {
                        result = node;

                        break;
                    }
                }
                
                return result;
            }

            public unsafe void Free(NativeLinkedListNode* node)
            {
                ++node->version;
                do
                {
                    node->next = (NativeLinkedListNode*)(void*)__pool->__nodes;
                }
                while (Interlocked.CompareExchange(ref __pool->__nodes, (IntPtr)(void*)node, (IntPtr)(void*)(node->next)) != (IntPtr)(void*)(node->next));
            }
        }
        
        private readonly int __itemSize;
        private int __capacity;
        private IntPtr __nodes;
        private IntPtr __activedChunks;
        private IntPtr __deactivedChunks;

        public unsafe NativeLinkedListPool(int itemSize, int capacity)
        {
            __itemSize = itemSize;
            __capacity = capacity;
            __nodes = IntPtr.Zero;
            __activedChunks = (IntPtr)(void*)__Allocate(itemSize, capacity);
            __deactivedChunks = IntPtr.Zero;
        }
        
        public unsafe void Dispose()
        {
            Chunk* chunk;
            while (__activedChunks != null)
            {
                chunk = (Chunk*)(void*)__activedChunks;
                __activedChunks = (IntPtr)(void*)chunk->next;
                UnsafeUtility.Free(chunk, Allocator.Persistent);
            }

            while (__deactivedChunks != null)
            {
                chunk = (Chunk*)(void*)__deactivedChunks;
                __deactivedChunks = (IntPtr)(void*)chunk->next;
                UnsafeUtility.Free(chunk, Allocator.Persistent);
            }
        }

        private static unsafe Chunk* __Allocate(int itemSize, int capacity)
        {
            int nodeSize = UnsafeUtility.SizeOf<NativeLinkedListNode>(),
                chunkSize = UnsafeUtility.SizeOf<Chunk>(),
                size = chunkSize + (nodeSize + itemSize) * capacity;
            Chunk* chunk = (Chunk*)UnsafeUtility.Malloc(size, size, Allocator.Persistent);
            chunk->count = 0;
            chunk->capacity = capacity;
            chunk->nodes = (NativeLinkedListNode*)((byte*)chunk + chunkSize);
            
            NativeLinkedListNode* node = chunk->nodes;
            for (int i = 0; i < capacity; ++i)
            {
                node->version = 0;
                node->value = (byte*)chunk->nodes + nodeSize;
                node->next = null;
                node = (NativeLinkedListNode*)((byte*)node->value + itemSize);
            }

            chunk->next = null;

            return chunk;
        }
    }
}