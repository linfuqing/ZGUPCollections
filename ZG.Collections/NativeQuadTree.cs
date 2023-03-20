using System;
using System.Threading;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;
using Math = ZG.Mathematics.Math;

namespace ZG
{
    public interface INativeQuadTreeFilter<T, U> where T : struct
    {
        bool Predicate(
            float3 min,
            float3 max, 
            T source, 
            out U destination);
    }

    public interface INativeQuadTreeFilter<T, U, V> where U : struct
    {
        bool Predicate(
            float3 min,
            float3 max,
            T data, 
            U source, 
            out V destination);
    }

    internal struct NativeQuadTreeItemPool : IDisposable
    {
        public struct Concurrent<T> where T : struct
        {
            private static bool __isInit;
            private static NativeQuadTreeItemPool __instance;

            [NativeDisableUnsafePtrRestriction]
            private unsafe NativeQuadTreeItemPool* __pool;

            private int __capacity;

            public unsafe Concurrent(int capacity)
            {
                if(!__isInit)
                {
                    __isInit = true;

                    AppDomain.CurrentDomain.DomainUnload += __OnDomainUnload;
                }
                
                __pool = (NativeQuadTreeItemPool*)UnsafeUtility.AddressOf(ref __instance);
                __capacity = capacity;
            }

            public unsafe NativeQuadTreeItem* Allocate(T data)
            {
                if (__pool == null)
                    return null;

                NativeQuadTreeItem* result, item;
                while (true)
                {
                    item = (NativeQuadTreeItem*)(void*)__pool->__items;
                    if (item == null)
                    {
                        int index = Interlocked.Increment(ref __pool->__count);

                        result = (NativeQuadTreeItem*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<NativeQuadTreeItem>(), UnsafeUtility.AlignOf<NativeQuadTreeItem>(), Allocator.Persistent);

                        result->info.index = index;
                        result->info.version = __pool->__version;
                        result->data = UnsafeUtility.Malloc(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>(), Allocator.Persistent);

                        break;
                    }

                    if (Interlocked.CompareExchange(ref __pool->__items, (IntPtr)(void*)item->next, (IntPtr)(void*)item) != (IntPtr)(void*)item)
                        continue;

                    result = item;

                    break;
                }

                UnsafeUtility.WriteArrayElement<T>(result->data, 0, data);

                return result;
            }

            public unsafe void Free(NativeQuadTreeItem* item)
            {
                if (__pool == null)
                {
                    if (item != null)
                    {
                        UnsafeUtility.Free(item->data, Allocator.Persistent);
                        UnsafeUtility.Free(item, Allocator.Persistent);
                    }
                }
                else
                    __pool->Free(item, __capacity);
            }
            
            private static void __OnDomainUnload(object sender, EventArgs e)
            {
                __instance.Dispose();
            }
        }

        private IntPtr __items;
        private int __version;
        private int __count;
        
        public unsafe void Free(NativeQuadTreeItem* item, int capacity)
        {
            if (__count > capacity)
            {
                Interlocked.Increment(ref __version);

                if (Interlocked.Decrement(ref __count) + 1 > capacity)
                {
                    UnsafeUtility.Free(item->data, Allocator.Persistent);
                    UnsafeUtility.Free(item, Allocator.Persistent);

                    return;
                }

                Interlocked.Increment(ref __count);
                Interlocked.Decrement(ref __version);
            }

            do
            {
                item->next = (NativeQuadTreeItem*)(void*)__items;
            }
            while (Interlocked.CompareExchange(ref __items, (IntPtr)(void*)item, (IntPtr)(void*)(item->next)) != (IntPtr)(void*)(item->next));
        }

        public unsafe void Dispose()
        {
            while (__items != IntPtr.Zero)
            {
                NativeQuadTreeItem* item = (NativeQuadTreeItem*)(void*)__items;
                __items = (IntPtr)(void*)item->next;
                UnsafeUtility.Free(item->data, Allocator.Persistent);
                UnsafeUtility.Free(item, Allocator.Persistent);
            }
        }
    }

    internal struct NativeQuadTreeItem
    {
        public int flag;
        public float3 min;
        public float3 max;

        public NativeQuadTreeItemInfo info;

        public unsafe void* data;

        public unsafe NativeQuadTreeItem* next;

        /*public bool IsContain(float3 min, float3 max)
        {
            return this.min.x <= min.x &&
                this.min.y <= min.y &&
                this.min.z <= min.z &&
                this.max.x >= max.x &&
                this.max.y >= max.y && 
                this.max.z >= max.z;
        }

        public bool IsIntersect(float3 min, float3 max)
        {
            return math.abs(this.min.x + this.max.x - min.x - max.x) <=
                (this.max.x + max.x - this.min.x - min.x) &&
                math.abs(this.min.y + this.max.y - min.y - max.y) <=
                (this.max.y + max.y - this.min.y - min.y) &&
                math.abs(this.min.z + this.max.z - min.z - max.z) <=
                (this.max.z + max.z - this.min.z - min.z);
        }*/

        public bool Test(float3 min, float3 max)
        {
            return this.min.x <= max.x && 
                this.max.x >= min.x && 
                this.min.y <= max.y && 
                this.max.y >= min.y && 
                this.min.z <= max.z &&
                this.max.z >= min.z;
        }
    }
    
    internal unsafe struct NativeQuadTreeNode
    {
        public int localFlag;
        public int worldFlag;

        public IntPtr items;
        
        public NativeQuadTreeNode* parent;
        public NativeQuadTreeNode* leftUp;
        public NativeQuadTreeNode* leftDown;
        public NativeQuadTreeNode* rightUp;
        public NativeQuadTreeNode* rightDown;

        public void SetLocalFlagSafe(int flag)
        {
            int source, destination;
            do
            {
                source = localFlag;
                destination = source | flag;
            } while (Interlocked.CompareExchange(ref localFlag, destination, source) != source);

            SetWorldFlagSafe(flag);
        }

        public void SetWorldFlagSafe(int flag)
        {
            int source, destination;
            do
            {
                source = worldFlag;
                destination = source | flag;
            } while (Interlocked.CompareExchange(ref worldFlag, destination, source) != source);

            if (parent != null)
                parent->SetWorldFlagSafe(flag);
        }

        public void SetLocalFlag(int flag)
        {
            localFlag |= flag;

            SetWorldFlag(flag);
        }

        public void SetWorldFlag(int flag)
        {
            worldFlag |= flag;

            if (parent != null)
                parent->SetWorldFlag(flag);
        }
        
        public void ResetLocalFlag()
        {
            localFlag = 0;
            for (NativeQuadTreeItem* item = (NativeQuadTreeItem*)(void*)items; item != null; item = item->next)
                localFlag |= item->flag;

            ResetWorldFlag();
        }

        public void ResetWorldFlag()
        {
            worldFlag = localFlag;

            if (leftUp != null)
                worldFlag |= leftUp->worldFlag;

            if (leftDown != null)
                worldFlag |= leftDown->worldFlag;

            if (rightUp != null)
                worldFlag |= rightUp->worldFlag;

            if (rightDown != null)
                worldFlag |= rightDown->worldFlag;

            if (parent != null)
                parent->ResetWorldFlag();
        }

        public NativeQuadTreeItem* Search(NativeQuadTreeItemInfo itemInfo, float3 min, float3 max, int flag)
        {
            if ((localFlag & flag) == 0)
                return null;
            
            for (NativeQuadTreeItem* item = (NativeQuadTreeItem*)(void*)items; item != null; item = item->next)
            {
                if ((item->flag & flag) != 0 && 
                    item->Test(min, max) && 
                    (!itemInfo.isVail || itemInfo.Equals(item->info)))
                    return item;
            }

            return null;
        }

        public NativeQuadTreeItem* Search(NativeQuadTreeItemInfo itemInfo, int flag)
        {
            if ((localFlag & flag) == 0)
                return null;

            for (NativeQuadTreeItem* item = (NativeQuadTreeItem*)(void*)items; item != null; item = item->next)
            {
                if ((item->flag & flag) != 0 && 
                    (!itemInfo.isVail || itemInfo.Equals(item->info)))
                    return item;
            }

            return null;
        }

        public bool Search<T, U, V>(V filter, float3 min, float3 max, int flag, out NativeQuadTreeData<T, U> result)
            where T : struct
            where U : struct
            where V : struct, INativeQuadTreeFilter<T, U>
        {
            result = default;
            if ((localFlag & flag) == 0)
                return false;
            
            for (NativeQuadTreeItem* item = (NativeQuadTreeItem*)(void*)items; item != null; item = item->next)
            {
                if ((item->flag & flag) != 0 &&
                    item->Test(min, max) &&
                    filter.Predicate(
                    item->min,
                    item->max,
                    result.info.instance = UnsafeUtility.ReadArrayElement<T>(item->data, 0),
                    out result.instance))
                {
                    result.info.item = item->info;

                    return true;
                }
            }

            return false;
        }

        public bool Search<T, U, V>(V filter, int flag, out NativeQuadTreeData<T, U> result)
            where T : struct
            where U : struct
            where V : struct, INativeQuadTreeFilter<T, U>
        {
            result = default;
            if ((localFlag & flag) == 0)
                return false;
            
            for (NativeQuadTreeItem* item = (NativeQuadTreeItem*)(void*)items; item != null; item = item->next)
            {
                if ((item->flag & flag) != 0 &&
                    filter.Predicate(
                    item->min,
                    item->max,
                    result.info.instance = UnsafeUtility.ReadArrayElement<T>(item->data, 0),
                    out result.instance))
                {
                    result.info.item = item->info;

                    return true;
                }
            }

            return false;
        }

        public bool Search<T, U, V, W>(V filter, W data, float3 min, float3 max, int flag, out NativeQuadTreeData<T, U> result)
            where T : struct
            where U : struct
            where V : struct, INativeQuadTreeFilter<W, T, U>
        {
            result = default;
            if ((localFlag & flag) == 0)
                return false;

            for (NativeQuadTreeItem* item = (NativeQuadTreeItem*)(void*)items; item != null; item = item->next)
            {
                if ((item->flag & flag) != 0 &&
                    item->Test(min, max) &&
                    filter.Predicate(
                    item->min,
                    item->max,
                    data,
                    result.info.instance = UnsafeUtility.ReadArrayElement<T>(item->data, 0),
                    out result.instance))
                {
                    result.info.item = item->info;

                    return true;
                }
            }

            return false;
        }

        public bool Search<T, U, V, W>(V filter, W data, int flag, out NativeQuadTreeData<T, U> result)
            where T : struct
            where U : struct
            where V : struct, INativeQuadTreeFilter<W, T, U>
        {
            result = default;
            if ((localFlag & flag) == 0)
                return false;

            for (NativeQuadTreeItem* item = (NativeQuadTreeItem*)(void*)items; item != null; item = item->next)
            {
                if ((item->flag & flag) != 0 &&
                    filter.Predicate(
                    item->min,
                    item->max,
                    data,
                    result.info.instance = UnsafeUtility.ReadArrayElement<T>(item->data, 0),
                    out result.instance))
                {
                    result.info.item = item->info;
                    
                    return true;
                }
            }

            return false;
        }

        public int Search<T>(NativeQueue<NativeQuadTreeInfo<T>>.ParallelWriter infos, float3 min, float3 max, int flag) where T : unmanaged
        {
            if ((localFlag & flag) == 0)
                return 0;

            int count = 0;
            NativeQuadTreeInfo<T> info;
            for (NativeQuadTreeItem* item = (NativeQuadTreeItem*)(void*)items; item != null; item = item->next)
            {
                if ((item->flag & flag) != 0 &&
                    item->Test(min, max))
                {
                    info.instance = UnsafeUtility.ReadArrayElement<T>(item->data, 0);
                    info.item = item->info;
                    infos.Enqueue(info);

                    ++count;
                }
            }

            return count;
        }

        public int Search<T>(NativeQueue<NativeQuadTreeInfo<T>>.ParallelWriter infos, int flag) where T : unmanaged
        {
            if ((localFlag & flag) == 0)
                return 0;

            int count = 0;
            NativeQuadTreeInfo<T> info;
            for (NativeQuadTreeItem* item = (NativeQuadTreeItem*)(void*)items; item != null; item = item->next)
            {
                if ((item->flag & flag) != 0)
                {
                    info.instance = UnsafeUtility.ReadArrayElement<T>(item->data, 0);
                    info.item = item->info;
                    
                    infos.Enqueue(info);

                    ++count;
                }
            }

            return count;
        }

        public int Search<T, U, V>(NativeQueue<NativeQuadTreeData<T, U>>.ParallelWriter results, V filter, float3 min, float3 max, int flag) 
            where T : unmanaged
            where U : unmanaged
            where V : unmanaged, INativeQuadTreeFilter<T, U>
        {
            if ((localFlag & flag) == 0)
                return 0;

            int count = 0;
            NativeQuadTreeData<T, U> result;
            for (NativeQuadTreeItem* item = (NativeQuadTreeItem*)(void*)items; item != null; item = item->next)
            {
                if ((item->flag & flag) != 0 &&
                    item->Test(min, max) &&
                    filter.Predicate(
                    item->min, 
                    item->max, 
                    result.info.instance = UnsafeUtility.ReadArrayElement<T>(item->data, 0), 
                    out result.instance))
                {
                    result.info.item = item->info;
                    
                    results.Enqueue(result);

                    ++count;
                }
            }

            return count;
        }

        public int Search<T, U, V>(NativeQueue<NativeQuadTreeData<T, U>>.ParallelWriter results, V filter, int flag)
            where T : unmanaged
            where U : unmanaged
            where V : unmanaged, INativeQuadTreeFilter<T, U>
        {
            if ((localFlag & flag) == 0)
                return 0;

            int count = 0;
            NativeQuadTreeData<T, U> result;
            for (NativeQuadTreeItem* item = (NativeQuadTreeItem*)(void*)items; item != null; item = item->next)
            {
                if ((item->flag & flag) != 0 && 
                    filter.Predicate(
                    item->min, 
                    item->max,
                    result.info.instance = UnsafeUtility.ReadArrayElement<T>(item->data, 0), 
                    out result.instance))
                {
                    result.info.item = item->info;

                    results.Enqueue(result);

                    ++count;
                }
            }

            return count;
        }

        public int Search<T, U, V, W>(NativeQueue<NativeQuadTreeData<T, U>>.ParallelWriter results, V filter, W data, float3 min, float3 max, int flag)
            where T : unmanaged
            where U : unmanaged
            where V : unmanaged, INativeQuadTreeFilter<W, T, U>
        {
            if ((localFlag & flag) == 0)
                return 0;

            int count = 0;
            NativeQuadTreeData<T, U> result;
            for (NativeQuadTreeItem* item = (NativeQuadTreeItem*)(void*)items; item != null; item = item->next)
            {
                if ((item->flag & flag) != 0 &&
                    item->Test(min, max) &&
                    filter.Predicate(
                    item->min,
                    item->max,
                    data, 
                    result.info.instance = UnsafeUtility.ReadArrayElement<T>(item->data, 0),
                    out result.instance))
                {
                    result.info.item = item->info;

                    results.Enqueue(result);

                    ++count;
                }
            }

            return count;
        }

        public int Search<T, U, V, W>(NativeQueue<NativeQuadTreeData<T, U>>.ParallelWriter results, V filter, W data, int flag)
            where T : unmanaged
            where U : unmanaged
            where V : unmanaged, INativeQuadTreeFilter<W, T, U>
        {
            if ((localFlag & flag) == 0)
                return 0;

            int count = 0;
            NativeQuadTreeData<T, U> result;
            for (NativeQuadTreeItem* item = (NativeQuadTreeItem*)(void*)items; item != null; item = item->next)
            {
                if ((item->flag & flag) != 0 &&
                    filter.Predicate(
                    item->min,
                    item->max,
                    data, 
                    result.info.instance = UnsafeUtility.ReadArrayElement<T>(item->data, 0),
                    out result.instance))
                {
                    result.info.item = item->info;

                    results.Enqueue(result);

                    ++count;
                }
            }

            return count;
        }

    }

    public struct NativeQuadTreeNodeInfo : IEquatable<NativeQuadTreeNodeInfo>
    {
        public int level;
        public int x;
        public int y;

        public static NativeQuadTreeNodeInfo Invail
        {
            get
            {
                NativeQuadTreeNodeInfo result;
                result.level = -1;
                result.x = -1;
                result.y = -1;

                return result;
            }
        }

        public bool isVail
        {
            get
            {
                return level >= 0 && x >= 0 && y >= 0;
            }
        }

        public bool Equals(NativeQuadTreeNodeInfo other)
        {
            return level == other.level && x == other.x && y == other.y;
        }
    }

    [Serializable]
    public struct NativeQuadTreeItemInfo : IEquatable<NativeQuadTreeItemInfo>
    {
        public int index;
        public int version;

        public static NativeQuadTreeItemInfo Invail
        {
            get
            {
                NativeQuadTreeItemInfo result;
                result.index = -1;
                result.version = -1;

                return result;
            }
        }

        public bool isVail
        {
            get
            {
                return index >= 0 && version >= 0;
            }
        }

        public bool Equals(NativeQuadTreeItemInfo other)
        {
            return index == other.index && version == other.version;
        }

        public override int GetHashCode()
        {
            return index;
        }
    }

    [Serializable]
    public struct NativeQuadTreeInfo<T> where T : struct
    {
        public T instance;

        public NativeQuadTreeItemInfo item;
    }

    [Serializable]
    public struct NativeQuadTreeData<T, U> where T : struct
    {
        public U instance;

        public NativeQuadTreeInfo<T> info;
    }

    [NativeContainer]
    public unsafe struct NativeQuadTree<T> : IDisposable where T : unmanaged
    {
        [NativeContainer]
        [NativeContainerIsAtomicWriteOnly]
        public struct Concurrent
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            private int __depth;
            
            private float3 __min;

            private float3 __max;

            private NativeQuadTreeItemPool.Concurrent<T> __itemPool;

            [NativeDisableUnsafePtrRestriction]
            private unsafe NativeQuadTreeNode* __levelNodes;
            
            public unsafe static implicit operator Concurrent(NativeQuadTree<T> quadTree)
            {
                Concurrent result;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(quadTree.m_Safety);
                result.m_Safety = quadTree.m_Safety;
                AtomicSafetyHandle.UseSecondaryVersion(ref result.m_Safety);
#endif
                result.__depth = quadTree.__depth;
                result.__min = quadTree.__min;
                result.__max = quadTree.__max;
                result.__itemPool = quadTree.__itemPool;
                result.__levelNodes = quadTree.__levelNodes;

                return result;
            }

            public unsafe bool Add(
                int layer, 
                float3 min,
                float3 max,
                T data, 
                out NativeQuadTreeNodeInfo nodeInfo,
                out NativeQuadTreeItemInfo itemInfo)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
                int flag;
                int2 targetMin, targetMax;
                Convert(__min, __max, min, max, out targetMin, out targetMax, out flag);

                targetMin = math.max(byte.MinValue, targetMin);
                targetMax = math.min(byte.MaxValue, targetMax);

                nodeInfo = __FindTreeNodeInfo(targetMin.x, targetMin.y, targetMax.x, targetMax.y, __depth);

                NativeQuadTreeNode* node = __GetNodeFromLevelXY(__levelNodes, nodeInfo, layer, __depth);
                if (node == null)
                {
                    itemInfo = NativeQuadTreeItemInfo.Invail;

                    return false;
                }

                NativeQuadTreeItem* item = __itemPool.Allocate(data);
                item->flag = flag;
                item->min = min;
                item->max = max;

                do
                {
                    item->next = (NativeQuadTreeItem*)(void*)node->items;
                }
                while (Interlocked.CompareExchange(ref node->items, (IntPtr)(void*)item, (IntPtr)(void*)(item->next)) != (IntPtr)(void*)(item->next));

                node->SetLocalFlagSafe(flag);

                itemInfo = item->info;

                return true;
            }
        }

        public const int MAXINUM_DEPTH = 8;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;

        [NativeSetClassTypeToNullOnSchedule]
        internal DisposeSentinel m_DisposeSentinel;
#endif

        private Allocator __allocator;
        
        private int __depth;

        private int __layers;

        private float3 __min;

        private float3 __max;

        private NativeQuadTreeItemPool.Concurrent<T> __itemPool;

        [NativeDisableUnsafePtrRestriction]
        private NativeQuadTreeNode* __levelNodes;
        
        public static int GetNodeCount(int depth)
        {
            ++depth;

            return (((1 << (depth + depth)) - 1) & 0x15555);
        }

        public bool isCreated
        {
            get
            {
                return __levelNodes != null;
            }
        }
        
        public int depth
        {
            get
            {
                return __depth;
            }
        }
        
        public static void Convert(float3 min, float3 max, float3 sourceMin, float3 sourceMax, out int2 destinationMin, out int2 destinationMax, out int flag)
        {
            float3 source = max - min, destination = new float3(256.0f, 32.0f, 256.0f), result = destination / source;

            sourceMin -= min;
            sourceMax -= min;

            flag = 0;
            int3 targetMin = (int3)math.floor(sourceMin * result), targetMax = (int3)math.floor(sourceMax * result);
            for (int i = targetMin.y; i <= targetMax.y; ++i)
                flag |= 1 << i;

            destinationMin = targetMin.xz;
            destinationMax = targetMax.xz;
        }
        
        public NativeQuadTree(Allocator allocator, int capacity, int depth, int layers, float3 min, float3 max)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!UnsafeUtility.IsBlittable<T>())
                throw new ArgumentException(string.Format("{0} used in NativeQuadTree<{0}> must be blittable", typeof(T)));

            if (depth < 0 || depth > MAXINUM_DEPTH)
                throw new ArgumentOutOfRangeException();

#endif
            __allocator = allocator;

            __depth = depth;

            __layers = layers;

            __min = min;

            __max = max;

            __itemPool = new NativeQuadTreeItemPool.Concurrent<T>(capacity);
            
            __levelNodes = (NativeQuadTreeNode*)UnsafeUtility.Malloc((long)UnsafeUtility.SizeOf<NativeQuadTreeNode>() * GetNodeCount(depth) * layers, UnsafeUtility.AlignOf<NativeQuadTreeNode>(), allocator);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
#if UNITY_2018_3_OR_NEWER
            DisposeSentinel.Create(out m_Safety, out m_DisposeSentinel, 0, allocator);
#else
            DisposeSentinel.Create(out m_Safety, out m_DisposeSentinel, 0);
#endif
#endif

            NativeQuadTreeNode* nodes = __levelNodes;
            int i, j, x, y, levelDimension;
            for (i = 0; i < layers; ++i)
            {
                for (j = 0; j <= depth; ++j)
                {
                    levelDimension = 1 << j;

                    for (y = 0; y < levelDimension; ++y)
                    {
                        for (x = 0; x < levelDimension; ++x)
                        {
                            nodes->localFlag = 0;
                            nodes->worldFlag = 0;
                            nodes->items = IntPtr.Zero;
                            nodes->parent = __GetNodeFromLevelXY(i, j - 1, (x >> 1), (y >> 1));
                            nodes->leftUp = __GetNodeFromLevelXY(i, j + 1, (x << 1), (y << 1));
                            nodes->leftDown = __GetNodeFromLevelXY(i, j + 1, (x << 1) + 1, (y << 1));
                            nodes->rightUp = __GetNodeFromLevelXY(i, j + 1, (x << 1), (y << 1) + 1);
                            nodes->rightDown = __GetNodeFromLevelXY(i, j + 1, (x << 1) + 1, (y << 1) + 1);

                            ++nodes;
                        }
                    }
                }
            }
        }

        public NativeQuadTree(Allocator allocator, int depth, int layers, float3 min, float3 max)
        {
            this = new NativeQuadTree<T>(allocator, int.MaxValue, depth, layers, min, max);
        }

        public NativeQuadTree(Allocator allocator, int layers, float3 min, float3 max)
        {
            this = new NativeQuadTree<T>(allocator, MAXINUM_DEPTH, layers, min, max);
        }

        public NativeQuadTree(Allocator allocator, float3 min, float3 max)
        {
            this = new NativeQuadTree<T>(allocator, MAXINUM_DEPTH, 1, min, max);
        }

        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (__levelNodes == null)
                throw new Exception("NativeQuadTree has yet to be allocated or has been dealocated!");

#if UNITY_2018_3_OR_NEWER
            DisposeSentinel.Dispose(ref m_Safety, ref m_DisposeSentinel);
#else
            DisposeSentinel.Dispose(m_Safety, ref m_DisposeSentinel);
#endif
#endif

            int nodeCount = GetNodeCount(__depth) * __layers;
            NativeQuadTreeNode* node;
            NativeQuadTreeItem* item;
            for (int i = 0; i < nodeCount; ++i)
            {
                node = __levelNodes + i;
                while (node->items != IntPtr.Zero)
                {
                    item = (NativeQuadTreeItem*)(void*)node->items;
                    node->items = (IntPtr)(void*)item->next;
                    __itemPool.Free(item);
                }
            }

            UnsafeUtility.Free(__levelNodes, __allocator);

            __levelNodes = null;
        }

        public bool Add(int layer, float3 min, float3 max, T data, out NativeQuadTreeNodeInfo nodeInfo, out NativeQuadTreeItemInfo itemInfo)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            int flag;
            int2 targetMin, targetMax;
            Convert(__min, __max, min, max, out targetMin, out targetMax, out flag);

            targetMin = math.max(byte.MinValue, targetMin);
            targetMax = math.min(byte.MaxValue, targetMax);
            
            nodeInfo = __FindTreeNodeInfo(targetMin.x, targetMin.y, targetMax.x, targetMax.y, __depth);

            NativeQuadTreeNode* node = __GetNodeFromLevelXY(nodeInfo, layer);
            if (node == null)
            {
                itemInfo = NativeQuadTreeItemInfo.Invail;

                return false;
            }
            
            NativeQuadTreeItem* item = __itemPool.Allocate(data);
            item->flag = flag;
            item->min = min;
            item->max = max;
            item->next = (NativeQuadTreeItem*)(void*)node->items;

            node->items = (IntPtr)(void*)item;

            node->SetLocalFlag(flag);
            
            itemInfo = item->info;

            return true;
        }

        public bool Remove(int layer, NativeQuadTreeNodeInfo nodeInfo, NativeQuadTreeItemInfo itemInfo)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            NativeQuadTreeNode* node = __GetNodeFromLevelXY(layer, nodeInfo.level, nodeInfo.x, nodeInfo.y);
            if (node == null || node->items == IntPtr.Zero)
                return false;
            
            NativeQuadTreeItem* temp = (NativeQuadTreeItem*)(void*)node->items;
            if(temp->info.Equals(itemInfo))
            {
                node->items = (IntPtr)(void*)temp->next;

                __itemPool.Free(temp);

                node->ResetLocalFlag();

                return true;
            }

            for (NativeQuadTreeItem* item = temp; item->next != null; item = item->next)
            {
                temp = item->next;
                if (temp->info.Equals(itemInfo))
                {
                    item->next = temp->next;

                    __itemPool.Free(temp);
                    
                    node->ResetLocalFlag();

                    return true;
                }
            }

            return false;
        }
        
        public bool Search(NativeQuadTreeItemInfo itemInfo, float3 min, float3 max, int layer)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            int flag;
            int2 targetMin, targetMax;
            Convert(__min, __max, min, max, out targetMin, out targetMax, out flag);

            targetMin = math.max(byte.MinValue, targetMin);
            targetMax = math.min(byte.MaxValue, targetMax);

            bool isNextLevel;
            int level = 0, shift, currentMinX, currentMaxX, currentMinY, currentMaxY, i, j;
            NativeQuadTreeNode* node;
            NativeQuadTreeItem* item;

            do
            {
                isNextLevel = false;

                shift = MAXINUM_DEPTH - level;
                currentMinX = targetMin.x >> shift;
                currentMaxX = targetMax.x >> shift;
                currentMinY = targetMin.y >> shift;
                currentMaxY = targetMax.y >> shift;
                for (j = currentMinY; j <= currentMaxY; ++j)
                {
                    for (i = currentMinX; i <= currentMaxX; ++i)
                    {
                        node = __GetNodeFromLevelXY(layer, level, i, j);

                        if (node != null && (node->worldFlag & flag) != 0)
                        {
                            isNextLevel = true;

                            item = j == currentMinY || j == currentMaxY || i == currentMinX || i == currentMaxX ?
                                node->Search(itemInfo, min, max, flag) :
                                node->Search(itemInfo, flag);
                            if (item != null)
                                return true;
                        }
                    }
                }

                ++level;
            }
            while (isNextLevel && level <= __depth);

            return false;
        }
        
        public bool Search(float3 min, float3 max, int layer)
        {
            return Search(NativeQuadTreeItemInfo.Invail, min, max, layer);
        }

        public bool Search<U, V>(V filter, float3 min, float3 max, int layer, out NativeQuadTreeData<T, U> result)
            where U : struct
            where V : struct, INativeQuadTreeFilter<T, U>
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            result = default;

            int flag;
            int2 targetMin, targetMax;
            Convert(__min, __max, min, max, out targetMin, out targetMax, out flag);

            targetMin = math.max(byte.MinValue, targetMin);
            targetMax = math.min(byte.MaxValue, targetMax);

            bool isNextLevel;
            int level = 0, shift, currentMinX, currentMaxX, currentMinY, currentMaxY, i, j;
            NativeQuadTreeNode* node;

            do
            {
                isNextLevel = false;

                shift = MAXINUM_DEPTH - level;
                currentMinX = targetMin.x >> shift;
                currentMaxX = targetMax.x >> shift;
                currentMinY = targetMin.y >> shift;
                currentMaxY = targetMax.y >> shift;
                for (j = currentMinY; j <= currentMaxY; ++j)
                {
                    for (i = currentMinX; i <= currentMaxX; ++i)
                    {
                        node = __GetNodeFromLevelXY(layer, level, i, j);

                        if (node != null && (node->worldFlag & flag) != 0)
                        {
                            isNextLevel = true;
                            
                            if (j == currentMinY || j == currentMaxY || i == currentMinX || i == currentMaxX ?
                                node->Search(filter, min, max, flag, out result) :
                                node->Search(filter, flag, out result))
                                return true;
                        }
                    }
                }

                ++level;
            }
            while (isNextLevel && level <= __depth);

            return false;
        }

        public bool SearchAll<U, V>(V filter, float3 min, float3 max, uint layerMask, out NativeQuadTreeData<T, U> result)
            where U : struct
            where V : struct, INativeQuadTreeFilter<T, U>
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

            result = default;

            if (layerMask == 0)
                return false;

            int from = Math.GetLowerstBit(layerMask) - 1, to = Math.GetHighestBit(layerMask) - 1;
            for (int i = from; i <= to; ++i)
            {
                if ((layerMask & (1 << i)) == 0)
                    continue;

                if (Search(filter, min, max, i, out result))
                    return true;
            }

            return false;
        }

        public bool Search<U, V, W>(V filter, W data, float3 min, float3 max, int layer, out NativeQuadTreeData<T, U> result)
            where U : struct
            where V : struct, INativeQuadTreeFilter<W, T, U>
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            result = default;

            int flag;
            int2 targetMin, targetMax;
            Convert(__min, __max, min, max, out targetMin, out targetMax, out flag);

            targetMin = math.max(byte.MinValue, targetMin);
            targetMax = math.min(byte.MaxValue, targetMax);

            bool isNextLevel;
            int level = 0, shift, currentMinX, currentMaxX, currentMinY, currentMaxY, i, j;
            NativeQuadTreeNode* node;
            do
            {
                isNextLevel = false;

                shift = MAXINUM_DEPTH - level;
                currentMinX = targetMin.x >> shift;
                currentMaxX = targetMax.x >> shift;
                currentMinY = targetMin.y >> shift;
                currentMaxY = targetMax.y >> shift;
                for (j = currentMinY; j <= currentMaxY; ++j)
                {
                    for (i = currentMinX; i <= currentMaxX; ++i)
                    {
                        node = __GetNodeFromLevelXY(layer, level, i, j);

                        if (node != null && (node->worldFlag & flag) != 0)
                        {
                            isNextLevel = true;
                            if (j == currentMinY || j == currentMaxY || i == currentMinX || i == currentMaxX ?
                                node->Search(filter, data, min, max, flag, out result) :
                                node->Search(filter, data, flag, out result))
                                return true;
                        }
                    }
                }

                ++level;
            }
            while (isNextLevel && level <= __depth);

            return false;
        }

        public bool SearchAll<U, V, W>(V filter, W data, float3 min, float3 max, uint layerMask, out NativeQuadTreeData<T, U> result)
            where U : struct
            where V : struct, INativeQuadTreeFilter<W, T, U>
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

            result = default;

            if (layerMask == 0)
                return false;

            int from = Math.GetLowerstBit(layerMask) - 1, to = Math.GetHighestBit(layerMask) - 1;
            for (int i = from; i <= to; ++i)
            {
                if ((layerMask & (1 << i)) == 0)
                    continue;

                if (Search(filter, data, min, max, i, out result))
                    return true;
            }

            return false;
        }

        public int Search(NativeQueue<NativeQuadTreeInfo<T>>.ParallelWriter infos, float3 min, float3 max, int layer)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            int flag;
            int2 targetMin, targetMax;
            Convert(__min, __max, min, max, out targetMin, out targetMax, out flag);

            targetMin = math.max(byte.MinValue, targetMin);
            targetMax = math.min(byte.MaxValue, targetMax);

            bool isNextLevel;
            int level = 0, count = 0, shift, currentMinX, currentMaxX, currentMinY, currentMaxY, i, j;
            NativeQuadTreeNode* node;
            do
            {
                isNextLevel = false;

                shift = MAXINUM_DEPTH - level;
                currentMinX = targetMin.x >> shift;
                currentMaxX = targetMax.x >> shift;
                currentMinY = targetMin.y >> shift;
                currentMaxY = targetMax.y >> shift;
                for (j = currentMinY; j <= currentMaxY; ++j)
                {
                    for (i = currentMinX; i <= currentMaxX; ++i)
                    {
                        node = __GetNodeFromLevelXY(layer, level, i, j);

                        if (node != null && (node->worldFlag & flag) != 0)
                        {
                            isNextLevel = true;

                            if (j == currentMinY || j == currentMaxY || i == currentMinX || i == currentMaxX)
                                count += node->Search(infos, min, max, flag);
                            else
                                count += node->Search(infos, flag);
                        }
                    }
                }

                ++level;
            }
            while (isNextLevel && level <= __depth);

            return count;
        }

        public int SearchAll(NativeQueue<NativeQuadTreeInfo<T>>.ParallelWriter infos, float3 min, float3 max, uint layerMask)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            int from = Math.GetLowerstBit(layerMask), to = Math.GetHighestBit(layerMask), count = 0;
            for (int i = from; i <= to; ++i)
            {
                if ((layerMask & (1 << i)) == 0)
                    continue;

                count += Search(infos, min, max, i);
            }

            return count;
        }

        public int Search<U, V>(NativeQueue<NativeQuadTreeData<T, U>>.ParallelWriter results, V filter, float3 min, float3 max, int layer)
            where U : unmanaged
            where V : unmanaged, INativeQuadTreeFilter<T, U>
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            int flag;
            int2 targetMin, targetMax;
            Convert(__min, __max, min, max, out targetMin, out targetMax, out flag);
            
            targetMin = math.max(byte.MinValue, targetMin);
            targetMax = math.min(byte.MaxValue, targetMax);

            bool isNextLevel;
            int level = 0, count = 0, shift, currentMinX, currentMaxX, currentMinY, currentMaxY, i, j;
            NativeQuadTreeNode* node;
            do
            {
                isNextLevel = false;

                shift = MAXINUM_DEPTH - level;
                currentMinX = targetMin.x >> shift;
                currentMaxX = targetMax.x >> shift;
                currentMinY = targetMin.y >> shift;
                currentMaxY = targetMax.y >> shift;
                for (j = currentMinY; j <= currentMaxY; ++j)
                {
                    for (i = currentMinX; i <= currentMaxX; ++i)
                    {
                        node = __GetNodeFromLevelXY(layer, level, i, j);

                        if (node != null && (node->worldFlag & flag) != 0)
                        {
                            isNextLevel = true;

                            if (j == currentMinY || j == currentMaxY || i == currentMinX || i == currentMaxX)
                                count += node->Search(results, filter, min, max, flag);
                            else
                                count += node->Search(results, filter, flag);
                        }
                    }
                }

                ++level;
            }
            while (isNextLevel && level <= __depth);

            return count;
        }

        public int SearchAll<U, V>(NativeQueue<NativeQuadTreeData<T, U>>.ParallelWriter results, V filter, float3 min, float3 max, uint layerMask)
            where U : unmanaged
            where V : unmanaged, INativeQuadTreeFilter<T, U>
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            if (layerMask == 0)
                return 0;

            int from = Math.GetLowerstBit(layerMask) - 1, to = Math.GetHighestBit(layerMask) - 1, count = 0;
            for (int i = from; i <= to; ++i)
            {
                if ((layerMask & (1 << i)) == 0)
                    continue;

                count += Search(results, filter, min, max, i);
            }

            return count;
        }

        public int Search<U, V, W>(NativeQueue<NativeQuadTreeData<T, U>>.ParallelWriter results, V filter, W data, float3 min, float3 max, int layer)
            where U : unmanaged
            where V : unmanaged, INativeQuadTreeFilter<W, T, U>
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            int flag;
            int2 targetMin, targetMax;
            Convert(__min, __max, min, max, out targetMin, out targetMax, out flag);

            targetMin = math.max(byte.MinValue, targetMin);
            targetMax = math.min(byte.MaxValue, targetMax);

            bool isNextLevel;
            int level = 0, count = 0, shift, currentMinX, currentMaxX, currentMinY, currentMaxY, i, j;
            NativeQuadTreeNode* node;
            do
            {
                isNextLevel = false;

                shift = MAXINUM_DEPTH - level;
                currentMinX = targetMin.x >> shift;
                currentMaxX = targetMax.x >> shift;
                currentMinY = targetMin.y >> shift;
                currentMaxY = targetMax.y >> shift;
                for (j = currentMinY; j <= currentMaxY; ++j)
                {
                    for (i = currentMinX; i <= currentMaxX; ++i)
                    {
                        node = __GetNodeFromLevelXY(layer, level, i, j);

                        if (node != null && (node->worldFlag & flag) != 0)
                        {
                            isNextLevel = true;

                            if (j == currentMinY || j == currentMaxY || i == currentMinX || i == currentMaxX)
                                count += node->Search(results, filter, data, min, max, flag);
                            else
                                count += node->Search(results, filter, data, flag);
                        }
                    }
                }

                ++level;
            }
            while (isNextLevel && level <= __depth);

            return count;
        }

        public int SearchAll<U, V, W>(NativeQueue<NativeQuadTreeData<T, U>>.ParallelWriter results, V filter, W data, float3 min, float3 max, uint layerMask)
            where U : unmanaged
            where V : unmanaged, INativeQuadTreeFilter<W, T, U>
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            if (layerMask == 0)
                return 0;

            int from = Math.GetLowerstBit(layerMask) - 1, to = Math.GetHighestBit(layerMask) - 1, count = 0;
            for (int i = from; i <= to; ++i)
            {
                if ((layerMask & (1 << i)) == 0)
                    continue;

                count += Search(results, filter, data, min, max, i);
            }

            return count;
        }

        public void Clear(uint layerMask)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            if (layerMask == 0)
                return;

            int nodeCount = GetNodeCount(__depth), 
                from = Math.GetLowerstBit(layerMask) - 1, 
                to = Math.GetHighestBit(layerMask) - 1;
            NativeQuadTreeNode* node = __levelNodes + from * nodeCount;
            for (int i = from; i <= to; ++i)
            {
                if ((layerMask & (1 << i)) != 0)
                    __Clear(node);

                node += nodeCount;
            }
        }

        private void __Clear(NativeQuadTreeNode* node)
        {
            if (node == null || node->worldFlag == 0)
                return;

            NativeQuadTreeItem* item = (NativeQuadTreeItem*)(void*)node->items, temp;
            while (item != null)
            {
                temp = item;
                item = temp->next;

                __itemPool.Free(temp);
            }

            node->items = IntPtr.Zero;

            node->localFlag = 0;
            
            __Clear(node->leftUp);
            __Clear(node->leftDown);
            __Clear(node->rightUp);
            __Clear(node->rightDown);

            node->worldFlag = 0;
        }

        private NativeQuadTreeNode* __GetNodeFromLevelXY(int layer, int level, int x, int y)
        {
            NativeQuadTreeNodeInfo nodeInfo;
            nodeInfo.level = level;
            nodeInfo.x = x;
            nodeInfo.y = y;
            return __GetNodeFromLevelXY(__levelNodes, nodeInfo, layer, __depth);
        }

        private NativeQuadTreeNode* __GetNodeFromLevelXY(NativeQuadTreeNodeInfo nodeInfo, int layer)
        {
            return __GetNodeFromLevelXY(__levelNodes, nodeInfo, layer, __depth);
        }

        private static NativeQuadTreeNode* __GetNodeFromLevelXY(NativeQuadTreeNode* levelNodes, NativeQuadTreeNodeInfo nodeInfo, int layer, int depth)
        {
            if (nodeInfo.x < 0 || nodeInfo.y < 0)
                return null;

            int count = 1 << nodeInfo.level;
            if (nodeInfo.x >= count || nodeInfo.y >= count)
                return null;

            NativeQuadTreeNode* nodes = __GetNodesFromLevel(levelNodes, depth, nodeInfo.level, layer);
            if (nodes == null)
                return null;
            
            return nodes + ((nodeInfo.y << nodeInfo.level) + nodeInfo.x);
        }
        
        private static NativeQuadTreeNode* __GetNodesFromLevel(NativeQuadTreeNode* levelNodes, int depth, int level, int layer)
        {
#if DEBUG
            if (levelNodes == null)
                throw new Exception("NativeQuadTree has yet to be allocated or has been dealocated!");
#endif
            if (level < 0 || level > depth)
                return null;
            
            return levelNodes + ((((1 << (level + level)) - 1) & 0x5555) + GetNodeCount(depth) * layer);
        }
        
        private static NativeQuadTreeNodeInfo __FindTreeNodeInfo(int minX, int minY, int maxX, int maxY, int depth)
        {
            int patternX = minX ^ maxX,
                patternY = minY ^ maxY,
                bitPattern = math.max(patternX, patternY),
                highBit = bitPattern <= byte.MaxValue ? Math.GetHighestBit((byte)bitPattern) : MAXINUM_DEPTH;

            NativeQuadTreeNodeInfo info;
            info.level = math.min(MAXINUM_DEPTH - highBit, depth);
            
            int shift = MAXINUM_DEPTH - info.level;

            info.x = maxX >> shift;
            info.y = maxY >> shift;

            return info;
        }

    }
}
