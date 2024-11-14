using System;
using System.Collections;
using System.Threading;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;
using Math = ZG.Mathematics.Math;

namespace ZG
{
    public interface INativeQuadTreeFilter<T>
    {
        bool Predicate(
            in float3 min,
            in float3 max, 
            in T value);
    }

    /*internal struct NativeQuadTreeItemPool : IDisposable
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
            NativeQuadTreeItem* item;
            while (__items != IntPtr.Zero)
            {
                item = (NativeQuadTreeItem*)(void*)__items;
                __items = (IntPtr)(void*)item->next;
                UnsafeUtility.Free(item->data, Allocator.Persistent);
                UnsafeUtility.Free(item, Allocator.Persistent);
            }
        }
    }*/

    internal struct NativeQuadTreeItem
    {
        public readonly int Flag;
        public readonly int Layer;
        
        public readonly float3 Min;
        public readonly float3 Max;

        //public NativeQuadTreeItemInfo info;

        public readonly NativeFactoryObject Target;

        public unsafe NativeQuadTreeItem* next;

        public unsafe NativeQuadTreeItem(
            int layer, 
            int flag, 
            in float3 min, 
            in float3 max, 
            in NativeFactoryObject target)
        {
            Layer = layer;
            Flag = flag;
            Min = min;
            Max = max;
            Target = target;

            next = null;
        }
        
        public ref T As<T>() where T : struct => ref Target.As<NativeQuadTreeObject<T>>().value;

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

        public bool Test(in float3 min, in float3 max)
        {
            return Min.x <= max.x && 
                Max.x >= min.x && 
                Min.y <= max.y && 
                Max.y >= min.y && 
                Min.z <= max.z &&
                Max.z >= min.z;
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
            for (var item = (NativeQuadTreeItem*)(void*)items; item != null; item = item->next)
                localFlag |= item->Flag;

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

        public bool Search<T, TFilter>(
            ref TFilter filter, 
            in float3 min, 
            in float3 max, 
            int flag)
            where T : struct
            where TFilter : INativeQuadTreeFilter<T>
        {
            if ((localFlag & flag) == 0)
                return false;
            
            for (var item = (NativeQuadTreeItem*)(void*)items; item != null; item = item->next)
            {
                if ((item->Flag & flag) != 0 &&
                    item->Test(min, max) &&
                    filter.Predicate(
                    item->Min,
                    item->Max,
                    item->As<T>()))
                    return true;
            }

            return false;
        }

        public bool Search<T, TFilter>(ref TFilter filter, int flag)
            where T : struct
            where TFilter : INativeQuadTreeFilter<T>
        {
            if ((localFlag & flag) == 0)
                return false;
            
            for (var item = (NativeQuadTreeItem*)(void*)items; item != null; item = item->next)
            {
                if ((item->Flag & flag) != 0 &&
                    filter.Predicate(
                    item->Min,
                    item->Max,
                    item->As<T>()))
                    return true;
            }

            return false;
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
    
    internal struct NativeQuadTreeObject<T>
    {
        public NativeQuadTreeItem item;

        public NativeQuadTreeNodeInfo nodeInfo;

        public T value;

        public static ref NativeQuadTreeObject<T> Create(
            int layer, 
            int flag, 
            in float3 min, 
            in float3 max, 
            in NativeQuadTreeNodeInfo nodeInfo, 
            in T value, 
            in NativeFactoryObject target)
        {
            ref var instance = ref target.As<NativeQuadTreeObject<T>>();
            instance.item = new NativeQuadTreeItem(layer, flag, min, max, target);
            instance.nodeInfo = nodeInfo;
            instance.value = value;
            
            return ref instance;
        }
    }
    
    public struct NativeQuadTreeItem<T> : IEquatable<NativeQuadTreeItem<T>>
    {
        private NativeFactoryObject __target;

        public bool isCreated => __target.isCreated;

        public int layer => target.item.Layer;

        internal ref T value => ref target.value;

        internal readonly ref NativeQuadTreeObject<T> target => ref __target.As<NativeQuadTreeObject<T>>();

        internal ref NativeFactoryEnumerable enumerable => ref __target.enumerable;

        internal NativeQuadTreeItem(in NativeFactoryObject target)
        {
            __target = target;
        }
        
        internal void Dispose()
        {
            __target.Dispose();
        }

        public override int GetHashCode()
        {
            return __target.GetHashCode();
        }

        public readonly bool Equals(NativeQuadTreeItem<T> other)
        {
            return __target.Equals(other.__target);
        }

        public readonly void Get(out T value, out float3 min, out float3 max, out int layer)
        {
            ref readonly var target = ref this.target;
            
            value = target.value;
            min = target.item.Min;
            max = target.item.Max;
            layer = target.item.Layer;
        }
    }

    public struct UnsafeQuadTree<T> : IDisposable where T : unmanaged
    {
        public struct Enumerator
        {
            private NativeFactoryEnumerable.Enumerator __instance;

            public Enumerator(ref UnsafeQuadTree<T> instance)
            {
                __instance = instance.__factory.enumerable.GetEnumerator();
            }

            public NativeQuadTreeItem<T> Current => new NativeQuadTreeItem<T>(__instance.Current);

            public void MoveNext() => __instance.MoveNext();
        }
        
        [NativeContainer]
        [NativeContainerIsAtomicWriteOnly]
        public struct ParallelWriter
        {
            public readonly int Depth;
            
            public readonly int Layers;

            public readonly float3 Min;

            public readonly float3 Max;

            private UnsafeFactory.ParallelWriter __factory;

            [NativeDisableUnsafePtrRestriction]
            private unsafe NativeQuadTreeNode* __levelNodes;
            
            public unsafe ParallelWriter(ref UnsafeQuadTree<T> quadTree)
            {
                Depth = quadTree.Depth;
                Layers = quadTree.Layers;
                Min = quadTree.Min;
                Max = quadTree.Max;
                __factory = quadTree.__factory.parallelWriter;
                __levelNodes = quadTree.__levelNodes;
            }

            public unsafe NativeQuadTreeItem<T> Add(
                int layer, 
                in float3 min,
                in float3 max,
                in T value)
            {
                UnityEngine.Assertions.Assert.IsTrue(layer < Layers);
                if (layer >= Layers)
                    return default;
                
                Convert(
                    Min, 
                    Max, 
                    min, 
                    max, 
                    out var targetMin, 
                    out var targetMax, 
                    out var flag);

                targetMin = math.max(byte.MinValue, targetMin);
                targetMax = math.min(byte.MaxValue, targetMax);

                var nodeInfo = __FindTreeNodeInfo(targetMin.x, targetMin.y, targetMax.x, targetMax.y, Depth);

                var node = __GetNodeFromLevelXY(__levelNodes, nodeInfo, layer, Depth);
                
                UnityEngine.Assertions.Assert.IsFalse(node == null);

                if (node == null)
                    return default;

                var origin = __factory.Create<NativeQuadTreeObject<T>>();
                ref var target = ref NativeQuadTreeObject<T>.Create(
                    layer, 
                    flag, 
                    min,
                    max, 
                    nodeInfo, 
                    value, 
                    origin);

                do
                {
                    target.item.next = (NativeQuadTreeItem*)(void*)node->items;
                }
                while (Interlocked.CompareExchange(
                           ref node->items, 
                           (IntPtr)UnsafeUtility.AddressOf(ref target.item), 
                           (IntPtr)(void*)(target.item.next)) != (IntPtr)(void*)(target.item.next));

                node->SetLocalFlagSafe(flag);

                return new NativeQuadTreeItem<T>(origin);
            }
        }

        public const int MAX_DEPTH = 8;

        public readonly int Depth;

        public readonly int Layers;

        public readonly float3 Min;

        public readonly float3 Max;

        private UnsafeFactory __factory;

        [NativeDisableUnsafePtrRestriction]
        private unsafe NativeQuadTreeNode* __levelNodes;
        
        public static int GetNodeCount(int depth)
        {
            ++depth;

            return (((1 << (depth + depth)) - 1) & 0x15555);
        }

        public unsafe bool isCreated
        {
            get
            {
                return __levelNodes != null;
            }
        }
        
        public static void Convert(
            in float3 min, 
            in float3 max, 
            in float3 sourceMin, 
            in float3 sourceMax, 
            out int2 destinationMin, 
            out int2 destinationMax, 
            out int flag)
        {
            float3 source = max - min, destination = new float3(256.0f, 32.0f, 256.0f), result = destination / source;

            flag = 0;
            int3 targetMin = (int3)math.floor((sourceMin - min) * result), targetMax = (int3)math.floor((sourceMax - min) * result);
            for (int i = targetMin.y; i <= targetMax.y; ++i)
                flag |= 1 << i;

            destinationMin = targetMin.xz;
            destinationMax = targetMax.xz;
        }
        
        public unsafe UnsafeQuadTree(
            bool isParallelWritable, 
            int depth, 
            int layers, 
            float3 min, 
            float3 max, 
            in AllocatorManager.AllocatorHandle allocator)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!UnsafeUtility.IsBlittable<T>())
                throw new ArgumentException(string.Format("{0} used in NativeQuadTree<{0}> must be blittable", typeof(T)));

            if (depth < 0 || depth > MAX_DEPTH)
                throw new ArgumentOutOfRangeException();
#endif
            Depth = depth;

            Layers = layers;

            Min = min;

            Max = max;

            __factory = new UnsafeFactory(allocator, isParallelWritable);
            
            __levelNodes = AllocatorManager.Allocate<NativeQuadTreeNode>(allocator, GetNodeCount(depth) * layers);

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

        public UnsafeQuadTree(int depth, int layers, in float3 min, in float3 max, in AllocatorManager.AllocatorHandle allocator)
        {
            this = new UnsafeQuadTree<T>(true, depth, layers, min, max, allocator);
        }

        public UnsafeQuadTree(int layers, in float3 min, in float3 max, in AllocatorManager.AllocatorHandle allocator)
        {
            this = new UnsafeQuadTree<T>(MAX_DEPTH, layers, min, max, allocator);
        }

        public UnsafeQuadTree(in float3 min, in float3 max, in AllocatorManager.AllocatorHandle allocator)
        {
            this = new UnsafeQuadTree<T>(MAX_DEPTH, 1, min, max, allocator);
        }

        public unsafe void Dispose()
        {
            /*int nodeCount = GetNodeCount(Depth) * Layers;
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
            }*/

            var allocator = __factory.allocator;

            __factory.Dispose();

            AllocatorManager.Free(allocator, __levelNodes, GetNodeCount(Depth) * Layers);

            __levelNodes = null;
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(ref this);
        }

        public ParallelWriter AsParallelWriter()
        {
            return new ParallelWriter(ref this);
        }

        public unsafe bool IsBelongFrom(in NativeQuadTreeItem<T> item)
        {
            return UnsafeUtility.AddressOf(ref __factory.enumerable) == UnsafeUtility.AddressOf(ref item.enumerable);
        }

        public unsafe NativeQuadTreeItem<T> Add(
            int layer, 
            in float3 min, 
            in float3 max, 
            in T value)
        {
            UnityEngine.Assertions.Assert.IsTrue(layer < Layers);
            if (layer >= Layers)
                return default;

            Convert(Min, Max, min, max, out var targetMin, out var targetMax, out var flag);

            targetMin = math.max(byte.MinValue, targetMin);
            targetMax = math.min(byte.MaxValue, targetMax);
            
            var nodeInfo = __FindTreeNodeInfo(targetMin.x, targetMin.y, targetMax.x, targetMax.y, Depth);

            var node = __GetNodeFromLevelXY(nodeInfo, layer);
            
            UnityEngine.Assertions.Assert.IsFalse(node == null);
            if (node == null)
                return default;

            var origin = __factory.Create<NativeQuadTreeObject<T>>();
            ref var target = ref NativeQuadTreeObject<T>.Create(
                layer, 
                flag, 
                min,
                max, 
                nodeInfo, 
                value, 
                origin);
            target.item.next = (NativeQuadTreeItem*)(void*)node->items;

            node->items = (IntPtr)UnsafeUtility.AddressOf(ref target.item);

            node->SetLocalFlag(flag);
            
            return new NativeQuadTreeItem<T>(origin);
        }

        public unsafe bool Remove(in NativeQuadTreeItem<T> value)
        {
            ref var target = ref value.target;
            var node = __GetNodeFromLevelXY(target.item.Layer, target.nodeInfo.level, target.nodeInfo.x, target.nodeInfo.y);
            if (node == null || node->items == IntPtr.Zero)
                return false;
            
            NativeQuadTreeItem* source = (NativeQuadTreeItem*)UnsafeUtility.AddressOf(ref target.item), temp = (NativeQuadTreeItem*)(void*)node->items;
            if(temp == source)
            {
                node->items = (IntPtr)(void*)temp->next;

                //__itemPool.Free(temp);
                temp->Target.Dispose();

                node->ResetLocalFlag();

                return true;
            }

            for (var destination = temp; destination->next != null; destination = destination->next)
            {
                temp = destination->next;
                if (temp == source)
                {
                    destination->next = temp->next;

                    //__itemPool.Free(temp);
                    temp->Target.Dispose();
                    
                    node->ResetLocalFlag();

                    return true;
                }
            }

            return false;
        }
        
        public unsafe bool Search<TFilter>(
            ref TFilter filter, 
            in float3 min, 
            in float3 max, 
            int layer)
            where TFilter : INativeQuadTreeFilter<T>
        {
            UnityEngine.Assertions.Assert.IsTrue(layer < Layers);
            if (layer >= Layers)
                return false;

            Convert(Min, Max, min, max, out var targetMin, out var targetMax, out int flag);

            targetMin = math.max(byte.MinValue, targetMin);
            targetMax = math.min(byte.MaxValue, targetMax);

            bool isNextLevel;
            int level = 0, shift, currentMinX, currentMaxX, currentMinY, currentMaxY, i, j;
            NativeQuadTreeNode* node;

            do
            {
                isNextLevel = false;

                shift = MAX_DEPTH - level;
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
                                node->Search<T, TFilter>(ref filter, min, max, flag) :
                                node->Search<T, TFilter>(ref filter, flag))
                                return true;
                        }
                    }
                }

                ++level;
            }
            while (isNextLevel && level <= Depth);

            return false;
        }

        public bool SearchAll<TFilter>(
            ref TFilter filter, 
            in float3 min, 
            in float3 max, 
            uint layerMask)
            where TFilter : INativeQuadTreeFilter<T>
        {
            if (layerMask == 0)
                return false;

            int from = Math.GetLowerstBit(layerMask) - 1, to = math.min(Math.GetHighestBit(layerMask), Layers) - 1;
            for (int i = from; i <= to; ++i)
            {
                if ((layerMask & (1 << i)) == 0)
                    continue;

                if (Search(ref filter, min, max, i))
                    return true;
            }

            return false;
        }

        public unsafe void Clear(uint layerMask)
        {
            if (layerMask == 0)
                return;

            int nodeCount = GetNodeCount(Depth), 
                from = Math.GetLowerstBit(layerMask) - 1, 
                to = math.min(Math.GetHighestBit(layerMask), Layers) - 1;
            var node = __levelNodes + from * nodeCount;
            for (int i = from; i <= to; ++i)
            {
                if ((layerMask & (1 << i)) != 0)
                    __Clear(node);

                node += nodeCount;
            }
        }

        private unsafe void __Clear(NativeQuadTreeNode* node)
        {
            if (node == null || node->worldFlag == 0)
                return;

            NativeQuadTreeItem* item = (NativeQuadTreeItem*)(void*)node->items, temp;
            while (item != null)
            {
                temp = item;
                item = temp->next;
                
                item->Target.Dispose();

                //__itemPool.Free(temp);
            }

            node->items = IntPtr.Zero;

            node->localFlag = 0;
            
            __Clear(node->leftUp);
            __Clear(node->leftDown);
            __Clear(node->rightUp);
            __Clear(node->rightDown);

            node->worldFlag = 0;
        }

        private unsafe NativeQuadTreeNode* __GetNodeFromLevelXY(int layer, int level, int x, int y)
        {
            NativeQuadTreeNodeInfo nodeInfo;
            nodeInfo.level = level;
            nodeInfo.x = x;
            nodeInfo.y = y;
            return __GetNodeFromLevelXY(__levelNodes, nodeInfo, layer, Depth);
        }

        private unsafe NativeQuadTreeNode* __GetNodeFromLevelXY(NativeQuadTreeNodeInfo nodeInfo, int layer)
        {
            return __GetNodeFromLevelXY(__levelNodes, nodeInfo, layer, Depth);
        }

        private static unsafe NativeQuadTreeNode* __GetNodeFromLevelXY(NativeQuadTreeNode* levelNodes, NativeQuadTreeNodeInfo nodeInfo, int layer, int depth)
        {
            if (nodeInfo.x < 0 || nodeInfo.y < 0)
                return null;

            int count = 1 << nodeInfo.level;
            if (nodeInfo.x >= count || nodeInfo.y >= count)
                return null;

            var nodes = __GetNodesFromLevel(levelNodes, depth, nodeInfo.level, layer);
            if (nodes == null)
                return null;
            
            return nodes + ((nodeInfo.y << nodeInfo.level) + nodeInfo.x);
        }
        
        private static unsafe NativeQuadTreeNode* __GetNodesFromLevel(NativeQuadTreeNode* levelNodes, int depth, int level, int layer)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
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
                highBit = bitPattern <= byte.MaxValue ? Math.GetHighestBit((byte)bitPattern) : MAX_DEPTH;

            NativeQuadTreeNodeInfo info;
            info.level = math.min(MAX_DEPTH - highBit, depth);
            
            int shift = MAX_DEPTH - info.level;

            info.x = maxX >> shift;
            info.y = maxY >> shift;

            return info;
        }
    }

    [NativeContainer]
    public struct NativeQuadTree<T> : IDisposable where T : unmanaged
    {
        [NativeContainer]
        [NativeContainerIsAtomicWriteOnly]
        public struct ParallelWriter
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            [NativeDisableUnsafePtrRestriction]
            private UnsafeQuadTree<T>.ParallelWriter __instance;
            
            public ParallelWriter(ref NativeQuadTree<T> instance)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(instance.m_Safety);
                m_Safety = instance.m_Safety;
                AtomicSafetyHandle.UseSecondaryVersion(ref m_Safety);
#endif
                
                __instance = instance.__instance.AsParallelWriter();
            }

            public NativeQuadTreeItem<T> Add(
                int layer, 
                in float3 min,
                in float3 max,
                in T value)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif

                return __instance.Add(layer, min, max, value);
            }
        }

        private UnsafeQuadTree<T> __instance;
        
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif

        public bool isCreated
        {
            get
            {
                return __instance.isCreated;
            }
        }
        
        public NativeQuadTree(
            bool isParallelWritable, 
            int depth, 
            int layers, 
            in float3 min, 
            in float3 max, 
            in AllocatorManager.AllocatorHandle allocator)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = CollectionHelper.CreateSafetyHandle(allocator);
#endif
            
            __instance = new UnsafeQuadTree<T>(isParallelWritable, depth, layers, min, max, allocator);
        }

        public NativeQuadTree(
            int depth, 
            int layers, 
            in float3 min, 
            in float3 max, 
            in AllocatorManager.AllocatorHandle allocator)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = CollectionHelper.CreateSafetyHandle(allocator);
#endif
            
            __instance = new UnsafeQuadTree<T>(depth, layers, min, max, allocator);
        }

        public NativeQuadTree(
            int layers, 
            in float3 min, 
            in float3 max, 
            in AllocatorManager.AllocatorHandle allocator)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = CollectionHelper.CreateSafetyHandle(allocator);
#endif
            
            __instance = new UnsafeQuadTree<T>(layers, min, max, allocator);
        }

        public NativeQuadTree(
            in float3 min, 
            in float3 max, 
            in AllocatorManager.AllocatorHandle allocator)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = CollectionHelper.CreateSafetyHandle(allocator);
#endif
            
            __instance = new UnsafeQuadTree<T>(min, max, allocator);
        }

        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            CollectionHelper.DisposeSafetyHandle(ref m_Safety);
#endif
            __instance.Dispose();
        }

        public void Change(ref NativeQuadTreeItem<T> item, in T value)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);

            if (!__instance.IsBelongFrom(item))
                throw new InvalidOperationException();
#endif

            item.value = value;
        }

        public NativeQuadTreeItem<T> Add(
            int layer, 
            in float3 min, 
            in float3 max, 
            in T value)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif

            return __instance.Add(layer, min, max, value);
        }

        public bool Remove(in NativeQuadTreeItem<T> item)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            
            return __instance.Remove(item);
        }
        
        public bool Search<TFilter>(
            ref TFilter filter, 
            in float3 min, 
            in float3 max, 
            int layer)
            where TFilter : struct, INativeQuadTreeFilter<T>
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

            return __instance.Search(ref filter, min, max, layer);
        }

        public bool SearchAll<TFilter>(
            ref TFilter filter, 
            in float3 min, 
            in float3 max, 
            uint layerMask)
            where TFilter : struct, INativeQuadTreeFilter<T>
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

            return __instance.SearchAll(ref filter, min, max, layerMask);
        }

        public void Clear(uint layerMask)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            __instance.Clear(layerMask);
        }
    }
}
