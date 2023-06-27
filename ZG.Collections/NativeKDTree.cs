using System;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Burst;
using ZG.Unsafe;
using Math = ZG.Mathematics.Math;
using static ZG.NativeKDTreeUtility;

namespace ZG
{
    public enum KDTreeInserMethod
    {
        Fast, 
        Variance
    }

    public interface IKDTreeValue
    {
        float Get(int dimension);
    }

    public interface IKDTreeCollector<T>
    {
        bool Test(in T value);

        bool Add(in T value);
    }

    internal struct UnsafeKDTreeObject<T> where T : struct
    {
        public UnsafeKDTreeNode node;
        public T value;
    }

    internal struct UnsafeKDTreeNode
    {
        private int __dimension;

        private NativeFactoryObject __target;

        public unsafe UnsafeKDTreeNode* parent;
        public unsafe UnsafeKDTreeNode* forward;
        public unsafe UnsafeKDTreeNode* backward;

        public unsafe UnsafeKDTreeNode* siblingBackward
        {
            get
            {
                if (parent == null)
                    return null;

                if (parent->forward == UnsafeUtility.AddressOf(ref this))
                {
                    if(parent->backward != null)
                        return parent->backward;
                }
                else
                    UnityEngine.Assertions.Assert.AreEqual((long)parent->backward, (long)UnsafeUtility.AddressOf(ref this));

                UnsafeKDTreeNode* parentSibling = parent->siblingBackward;
                while (parentSibling != null)
                {
                    if (parentSibling->forward != null)
                        return parentSibling->forward;

                    if (parentSibling->backward != null)
                        return parentSibling->backward;

                    parentSibling = parentSibling->siblingBackward;
                }

                return null;
            }
        }

        public unsafe UnsafeKDTreeNode* siblingForward
        {
            get
            {
                if (parent == null)
                    return null;

                if (parent->backward == UnsafeUtility.AddressOf(ref this))
                {
                    if(parent->forward != null)
                        return parent->forward;
                }
                else
                    UnityEngine.Assertions.Assert.AreEqual((long)parent->forward, (long)UnsafeUtility.AddressOf(ref this));

                UnsafeKDTreeNode* parentSibling = parent->siblingForward;
                while (parentSibling != null)
                {
                    if (parentSibling->backward != null)
                        return parentSibling->backward;

                    if (parentSibling->forward != null)
                        return parentSibling->forward;

                    parentSibling = parentSibling->siblingForward;
                }

                return null;
            }
        }

        public int dimension => __dimension;


        public bool isCreated => __target.isCreated;

        public unsafe bool isLeaf => backward == null && forward == null;

        public unsafe int GetDepth()
        {
            int depth = 0;

            var parent = this.parent;
            while(parent != null)
            {
                ++depth;

                parent = parent->parent;
            }

            return depth;
        }

        public unsafe int CountOfChildren()
        {
            int count = 1;

            var node = (UnsafeKDTreeNode*)UnsafeUtility.AddressOf(ref this);
            if (node->backward != null)
                count += node->backward->CountOfChildren();

            if (node->forward != null)
                count += node->forward->CountOfChildren();

            return count;
        }

        public unsafe UnsafeKDTreeNode* GetBackwardLeaf(ref int maxDepth)
        {
            UnsafeKDTreeNode* node = (UnsafeKDTreeNode*)UnsafeUtility.AddressOf(ref this), child;
            int i;
            for (i = 0; i < maxDepth; ++i)
            {
                UnityEngine.Assertions.Assert.AreEqual(i, node->GetDepth());

                while (true)
                {
                    child = node->backward;
                    if (child == null)
                    {
                        child = node->forward;

                        if (child == null)
                        {
                            child = node->siblingForward;
                            if (child == null)
                                break;

                            UnityEngine.Assertions.Assert.AreEqual(node->GetDepth(), child->GetDepth());

                            node = child;

                            continue;
                        }
                    }

                    UnityEngine.Assertions.Assert.AreEqual(i + 1, child->GetDepth());

                    node = child;

                    break;
                }

                if (child == null)
                {
                    maxDepth = i;

                    break;
                }
            }

            UnityEngine.Assertions.Assert.AreEqual(maxDepth, node->GetDepth());

            return node;
        }

        public unsafe UnsafeKDTreeNode* GetForwardLeaf(ref int maxDepth)
        {
            UnsafeKDTreeNode* node = (UnsafeKDTreeNode*)UnsafeUtility.AddressOf(ref this), child;
            int i;
            for (i = 0; i < maxDepth; ++i)
            {
                while (true)
                {
                    child = node->forward;
                    if (child == null)
                    {
                        child = node->backward;

                        if (child == null)
                        {
                            child = node->siblingBackward;
                            if (child == null)
                                break;

                            node = child;

                            continue;
                        }
                    }

                    node = child;

                    break;
                }

                if (child == null)
                {
                    maxDepth = i;

                    break;
                }
            }

            return node;
        }

        public T As<T>() where T : struct  => __target.As<UnsafeKDTreeObject<T>>().value;

        public unsafe bool Query<TValue, TCollector>(
            int dimensions, 
            in TValue min, 
            in TValue max, 
            ref TCollector collector) 
            where TValue : unmanaged, IKDTreeValue
            where TCollector : IKDTreeCollector<TValue>
        {
            var value = As<TValue>();

            if (!collector.Test(value))
                return false;

            bool result = false;
            float coordinate = value.Get(__dimension);
            if (min.Get(__dimension).CompareTo(coordinate) < 0)
            {
                if (backward != null)
                    result = backward->Query(dimensions, min, max, ref collector);
            }
            else if(max.Get(__dimension).CompareTo(coordinate) > 0)
            {
                if(forward != null)
                    result = forward->Query(dimensions, min, max, ref collector);
            }
            else
            {
                bool isContains = true;
                for(int i = 0; i < dimensions; ++i)
                {
                    if (i == __dimension)
                        continue;

                    coordinate = value.Get(i);
                    if (min.Get(i).CompareTo(coordinate) < 0 || max.Get(i).CompareTo(coordinate) > 0)
                    {
                        isContains = false;

                        break;
                    }
                }

                if (isContains)
                    result = collector.Add(value);

                if (backward != null)
                    result = backward->Query(dimensions, min, max, ref collector) || result;

                if (forward != null)
                    result = forward->Query(dimensions, min, max, ref collector) || result;
            }

            return result;
        }

        public unsafe void Dispose() => __target.Dispose();

        public static unsafe UnsafeKDTreeNode* Create<T>(
            ref UnsafeFactory factory,
            in T value,
            int dimension,
            UnsafeKDTreeNode* parent,
            UnsafeKDTreeNode* forward = null, 
            UnsafeKDTreeNode* backward = null)
            where T : struct
        {
            var result = factory.Create<UnsafeKDTreeObject<T>>();
            ref var target = ref result.As<UnsafeKDTreeObject<T>>();
            target.node.__dimension = dimension;
            target.node.__target = result;
            target.node.parent = parent;
            target.node.forward = forward;
            target.node.backward = backward;

            target.value = value;

            return (UnsafeKDTreeNode*)UnsafeUtility.AddressOf(ref target.node);
        }
    }

    internal struct UnsafeKDTreeData
    {
        public int count;

        public int depth;

        public unsafe UnsafeKDTreeNode* root;

        public unsafe void Clear()
        {
            count = 0;

            depth = 0;

            root = null;
        }
    }

    public struct UnsafeKDTreeNode<T> : IEquatable<UnsafeKDTreeNode<T>>, IEnumerable<UnsafeKDTreeNode<T>> where T : unmanaged, IKDTreeValue
    {
        public struct Enumerator : IEnumerator<UnsafeKDTreeNode<T>>
        {
            private unsafe UnsafeKDTreeNode* __source;
            private unsafe UnsafeKDTreeNode* __destination;

            private UnsafeKDTree __tree;

            public unsafe UnsafeKDTreeNode<T> Current
            {
                get
                {
                    UnsafeKDTreeNode<T> node;
                    node._value = __destination;
                    node._tree = __tree;

                    return node;
                }
            }

            public unsafe Enumerator(UnsafeKDTreeNode<T> node)
            {
                __source = node._value;
                __destination = null;

                __tree = node._tree;
            }

            public unsafe bool MoveNext()
            {
                if (__destination == null)
                {
                    if (__source == null)
                        return false;

                    __destination = __source;
                }
                else
                {
                    if (__destination->backward == null)
                    {
                        if (__destination->forward == null)
                        {
                            if (__destination != __source)
                            {
                                UnsafeKDTreeNode* child = __destination, parent = child->parent;
                                while (parent != null)
                                {
                                    if (parent->forward != child && parent->forward != null)
                                    {
                                        __destination = parent->forward;

                                        return true;
                                    }
                                    else if (parent == __source)
                                        break;

                                    child = parent;

                                    parent = child->parent;
                                }
                            }

                            return false;
                        }
                        else
                            __destination = __destination->forward;
                    }
                    else
                        __destination = __destination->backward;
                }

                return true;
            }

            public unsafe void Reset()
            {
                __destination = null;
            }

            public unsafe void Dispose()
            {
                __source = null;
                __destination = null;
            }

            object IEnumerator.Current => Current;
        }

        private struct ValueComparer : IComparer<T>
        {
            public int dimension;

            public int Compare(T x, T y)
            {
                return x.Get(dimension).CompareTo(y.Get(dimension));
            }
        }

        private struct CoordinateComparer : IComparer<float>
        {
            public int Compare(float x, float y)
            {
                return x.CompareTo(y);
            }
        }

        private struct CoordinateListWrapper : IReadOnlyListWrapper<float, NativeArray<T>>
        {
            public int dimension;

            public int GetCount(NativeArray<T> list) => list.Length;

            public float Get(NativeArray<T> list, int index) => list[index].Get(dimension);
        }

        internal unsafe UnsafeKDTreeNode* _value;
        internal UnsafeKDTree _tree;

        public unsafe bool isCreated => _value != null && _value->isCreated;

        public unsafe bool isLeaf => _value->isLeaf;

        public unsafe int dimension => _value->dimension;

        public unsafe float coordinate => value.Get(_value->dimension);

        public unsafe T value => _value->As<T>();

        public unsafe UnsafeKDTreeNode<T> parent
        {
            get
            {
                UnsafeKDTreeNode<T> node;
                node._value = _value->parent;
                node._tree = _tree;

                return node;
            }
        }

        public unsafe UnsafeKDTreeNode<T> backward
        {
            get
            {
                UnsafeKDTreeNode<T> node;
                node._value = _value->backward;
                node._tree = _tree;

                return node;
            }
        }

        public unsafe UnsafeKDTreeNode<T> forward
        {
            get
            {
                UnsafeKDTreeNode<T> node;
                node._value = _value->forward;
                node._tree = _tree;

                return node;
            }
        }

        public unsafe UnsafeKDTreeNode<T> siblingBackward
        {
            get
            {
                UnsafeKDTreeNode<T> result;
                result._value = _value->siblingBackward;
                result._tree = _tree;

                return result;
            }
        }

        public unsafe UnsafeKDTreeNode<T> siblingForward
        {
            get
            {
                UnsafeKDTreeNode<T> result;
                result._value = _value->siblingForward;
                result._tree = _tree;

                return result;
            }
        }

        public unsafe override int GetHashCode()
        {
            return (int)_value;
        }

        public unsafe void Dispose()
        {
            if (_value->forward != null)
                forward.Dispose();

            if (_value->backward != null)
                backward.Dispose();

            if (_tree._data->root == _value)
                _tree._data->root = null;

            --_tree._data->count;

            _value->Dispose();

            _value = null;
        }

        public unsafe int GetDepth() => _value->GetDepth();

        public unsafe int CountOfChildren() => _value->CountOfChildren();

        public unsafe UnsafeKDTreeNode<T> GetBackwardLeaf(ref int maxDepth)
        {
            UnsafeKDTreeNode<T> node;
            node._value = _value->GetBackwardLeaf(ref maxDepth);
            node._tree = _tree;

            return node;
        }

        public unsafe UnsafeKDTreeNode<T> GetForwardLeaf(ref int maxDepth)
        {
            UnsafeKDTreeNode<T> node;
            node._value = _value->GetForwardLeaf(ref maxDepth);
            node._tree = _tree;

            return node;
        }

        public unsafe bool Query<U>(
            in T min,
            in T max,
            ref U collector)
            where U : IKDTreeCollector<T>
        {
            return _value->Query(_tree.Dimensions, min, max, ref collector);
        }

        public unsafe void Insert(ref NativeArray<T> values, KDTreeInserMethod method = KDTreeInserMethod.Fast)
        {
            ValueComparer valueComparer;
            valueComparer.dimension = _value->dimension;
            values.Sort(valueComparer);

            CoordinateComparer coordinateComparer;
            CoordinateListWrapper coordinateListWrapper;
            coordinateListWrapper.dimension = _value->dimension;
            int maxBackIndex = values.BinarySearch(coordinate, coordinateComparer, coordinateListWrapper);
            if(maxBackIndex >= 0)
            {
                var subValues = values.GetSubArray(0, maxBackIndex + 1);

                if (_value->backward == null)
                    _value->backward = _tree._CreateNode(_value, ref subValues, method);
                else
                    backward.Insert(ref subValues, method);
            }

            int numValues = values.Length, minFrontIndex = maxBackIndex + 1;
            if(minFrontIndex < numValues)
            {
                var subValues = values.GetSubArray(minFrontIndex, numValues - minFrontIndex);

                if (_value->forward == null)
                    _value->forward = _tree._CreateNode(_value, ref subValues, method);
                else
                    forward.Insert(ref subValues, method);
            }
        }

        public unsafe bool Equals(UnsafeKDTreeNode<T> other)
        {
            return _value == other._value;
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        IEnumerator<UnsafeKDTreeNode<T>> IEnumerable<UnsafeKDTreeNode<T>>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal struct UnsafeKDTree
    {
        public readonly int Dimensions;

        internal unsafe UnsafeKDTreeData* _data;

        private UnsafeFactory __factroy;

        public unsafe bool isCreated => _data != null;

        public unsafe int count => _data->count;

        public unsafe int depth => _data->depth;

        public unsafe UnsafeKDTree(int dimensions, Allocator allocator)
        {
            Dimensions = dimensions;

            _data = AllocatorManager.Allocate<UnsafeKDTreeData>(allocator);
            _data->Clear();

            __factroy = new UnsafeFactory(allocator);
        }

        public unsafe void Dispose()
        {
            AllocatorManager.Free(__factroy.allocator, _data);

            _data = null;

            __factroy.Dispose();
        }

        public unsafe void Clear()
        {
            _data->Clear();

            __factroy.Clear();
        }

        public unsafe UnsafeKDTreeNode<T> GetRoot<T>()  
            where T : unmanaged, IKDTreeValue
        {
            UnsafeKDTreeNode<T> node;
            node._value = _data->root;
            node._tree = this;

            return node;
        }

        public unsafe void Insert<T>(ref NativeArray<T> values, KDTreeInserMethod method = KDTreeInserMethod.Fast) where T : unmanaged, IKDTreeValue
        {
            if (_data->root == null)
                _data->root = _CreateNode(null, ref values, method);
            else
                GetRoot<T>().Insert(ref values, method);
        }

        internal unsafe UnsafeKDTreeNode* _CreateNode<T>(
            UnsafeKDTreeNode* parent, 
            ref NativeArray<T> values, 
            KDTreeInserMethod method) where T : unmanaged, IKDTreeValue
        {
            int numValues = values.Length;
            if (numValues < 1)
                return null;

            int dimensionToSplit = -1;

            switch (method)
            {
                case KDTreeInserMethod.Fast:
                    dimensionToSplit = parent == null ? 0 : (parent->dimension + 1) % Dimensions;
                    break;
                case KDTreeInserMethod.Variance:
                    dimensionToSplit = NativeKDTreeUtility<T>.GetDimensionForMaxVariance(Dimensions, values);
                    break;
            }

            if (dimensionToSplit == -1)
                return null;

            ++_data->count;

            if (parent != null)
                _data->depth = math.max(_data->depth, parent->GetDepth() + 1);

            int valueIndexToSplit = NativeKDTreeUtility<T>.GetMidIndexOf(ref values, dimensionToSplit);

            T value = values[valueIndexToSplit];

            UnsafeKDTreeNode<T> result;
            result._value = UnsafeKDTreeNode.Create(ref __factroy, value, dimensionToSplit, parent);

            int lastValueIndex = numValues - 1;
            if (lastValueIndex > 0)
            {
                result._tree = this;

                values[valueIndexToSplit] = values[0];
                values[0] = value;

                var subValues = values.GetSubArray(1, lastValueIndex);

                result.Insert(ref subValues, method);
            }

            return result._value;
        }
    }

    public struct UnsafeKDTree<T> where T : unmanaged, IKDTreeValue
    {
        public readonly int Dimensions => __value.Dimensions;

        private UnsafeKDTree __value;

        public bool isCreated => __value.isCreated;

        public int count => __value.count;

        public int depth => __value.depth;

        public UnsafeKDTreeNode<T> root => __value.GetRoot<T>();

        public UnsafeKDTree(int dimensions, Allocator allocator)
        {
            __value = new UnsafeKDTree(dimensions, allocator);
        }

        public void Dispose() => __value.Dispose();

        public void Clear() => __value.Clear();

        public unsafe void Insert(ref NativeArray<T> values, KDTreeInserMethod method = KDTreeInserMethod.Fast) => __value.Insert(ref values, method);
    }

    public struct NativeKDTreeNode<T> : IEquatable<NativeKDTreeNode<T>>, IEnumerable<NativeKDTreeNode<T>> where T : unmanaged, IKDTreeValue
    {
        public struct Enumerator : IEnumerator<NativeKDTreeNode<T>>
        {
            private UnsafeKDTreeNode<T>.Enumerator __value;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            public unsafe NativeKDTreeNode<T> Current
            {
                get
                {
                    NativeKDTreeNode<T> node;
                    node._value = __value.Current;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    node.m_Safety = m_Safety;
#endif

                    return node;
                }
            }

            public unsafe Enumerator(NativeKDTreeNode<T> node)
            {
                __value = node._value.GetEnumerator();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = node.m_Safety;
#endif
            }

            public unsafe bool MoveNext()
            {
                __CheckRead();

                return __value.MoveNext();
            }

            public unsafe void Reset()
            {
                __value.Reset();
            }

            public unsafe void Dispose()
            {
                __value.Dispose();
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private void __CheckRead()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            }

            object IEnumerator.Current => Current;
        }

        internal UnsafeKDTreeNode<T> _value;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif

        public bool isCreated => _value.isCreated;

        public bool isLeaf => _value.isLeaf;

        public T value => _value.value;

        public unsafe NativeKDTreeNode<T> parent
        {
            get
            {
                __CheckRead();

                NativeKDTreeNode<T> node;
                node._value = _value.parent;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                node.m_Safety = m_Safety;
#endif

                return node;
            }
        }

        public unsafe NativeKDTreeNode<T> backward
        {
            get
            {
                __CheckRead();

                NativeKDTreeNode<T> node;
                node._value = _value.backward;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                node.m_Safety = m_Safety;
#endif

                return node;
            }
        }

        public unsafe NativeKDTreeNode<T> forward
        {
            get
            {
                __CheckRead();

                NativeKDTreeNode<T> node;
                node._value = _value.forward;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                node.m_Safety = m_Safety;
#endif

                return node;
            }
        }

        public unsafe NativeKDTreeNode<T> siblingBackward
        {
            get
            {
                __CheckRead();

                NativeKDTreeNode<T> node;
                node._value = _value.siblingBackward;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                node.m_Safety = m_Safety;
#endif

                return node;
            }
        }

        public unsafe NativeKDTreeNode<T> siblingForward
        {
            get
            {
                __CheckRead();

                NativeKDTreeNode<T> node;
                node._value = _value.siblingForward;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                node.m_Safety = m_Safety;
#endif

                return node;
            }
        }

        public unsafe override int GetHashCode() => _value.GetHashCode();

        public void Dispsoe()
        {
            __CheckWrite();

            _value.Dispose();
        }

        public unsafe NativeKDTreeNode<T> GetBackwardLeaf(ref int maxDepth)
        {
            __CheckRead();

            NativeKDTreeNode<T> node;
            node._value = _value.GetBackwardLeaf(ref maxDepth);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            node.m_Safety = m_Safety;
#endif

            return node;
        }

        public unsafe NativeKDTreeNode<T> GetForwardLeaf(int maxDepth)
        {
            __CheckRead();

            NativeKDTreeNode<T> node;
            node._value = _value.GetForwardLeaf(ref maxDepth);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            node.m_Safety = m_Safety;
#endif

            return node;
        }

        public int GetDepth()
        {
            __CheckRead();

            return _value.GetDepth();
        }

        public int CountOfChildren()
        {
            __CheckRead();

            return _value.CountOfChildren();
        }

        public bool Query<U>(
            in T min,
            in T max,
            ref U collector)
            where U : IKDTreeCollector<T>
        {
            __CheckRead();

            return _value.Query(min, max, ref collector);
        }

        public void Insert(ref NativeArray<T> values, KDTreeInserMethod method = KDTreeInserMethod.Fast)
        {
            __CheckWrite();

            _value.Insert(ref values, method);
        }

        public bool Equals(NativeKDTreeNode<T> other)
        {
            return _value.Equals(other._value);
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        IEnumerator<NativeKDTreeNode<T>> IEnumerable<NativeKDTreeNode<T>>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

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
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
        }
    }

    public struct NativeKDTree<T> where T : unmanaged, IKDTreeValue
    {
        private UnsafeKDTree<T> __value;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;

        private static readonly SharedStatic<int> StaticSafetyId = SharedStatic<int>.GetOrCreate<NativeKDTree<T>>();

        [BurstDiscard]
        static void CreateStaticSafetyId()
        {
            StaticSafetyId.Data = AtomicSafetyHandle.NewStaticSafetyId<NativeKDTree<T>>();
        }

        /*[NativeSetClassTypeToNullOnSchedule]
        DisposeSentinel m_DisposeSentinel;*/
#endif

        public bool isCreated => __value.isCreated;

        public int count
        {
            get
            {
                __CheckRead();

                return __value.count;
            }
        }

        public int depth
        {
            get
            {
                __CheckRead();

                return __value.depth;
            }
        }

        public NativeKDTreeNode<T> root
        {
            get
            {
                __CheckRead();

                NativeKDTreeNode<T> node;
                node._value = __value.root;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                node.m_Safety = m_Safety;
#endif

                return node;
            }
        }

        public NativeKDTree(int dimensions, Allocator allocator)
        {
            __value = new UnsafeKDTree<T>(dimensions, allocator);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            //DisposeSentinel.Create(out m_Safety, out m_DisposeSentinel, 0, allocator);
            m_Safety = AtomicSafetyHandle.Create();

            if (StaticSafetyId.Data == 0)
                CreateStaticSafetyId();

            AtomicSafetyHandle.SetStaticSafetyId(ref m_Safety, StaticSafetyId.Data);
#endif
        }

        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckDeallocateAndThrow(m_Safety);

            AtomicSafetyHandle.Release(m_Safety);
#endif

            __value.Dispose();
        }

        public void Clear()
        {
            __CheckWrite();

            __value.Clear();
        }

        public void Insert(ref NativeArray<T> values, KDTreeInserMethod method = KDTreeInserMethod.Fast)
        {
            __CheckWrite();

            __value.Insert(ref values, method);
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
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
        }
    }

    public static class NativeKDTreeUtility<T> where T : struct, IKDTreeValue
    {
        public static int GetDimensionForMaxVariance(int dimensions, in NativeArray<T> values)
        {
            float maxVariance = float.MinValue, variance, sqr, avg, coordinate;
            int i, j, dimension = -1, numValues = values.Length;
            for (i = 0; i < dimensions; ++i)
            {
                sqr = 0.0f;
                avg = 0.0f;
                for (j = 0; j < numValues; ++j)
                {
                    coordinate = values[j].Get(i);

                    sqr += coordinate * coordinate;
                    avg += coordinate;
                }

                sqr /= numValues;
                avg /= numValues;

                variance = math.abs(sqr - avg * avg);
                if (variance > maxVariance)
                {
                    maxVariance = variance;

                    dimension = i;
                }
            }

            return dimension;
        }

        public static int GetMidIndexOf(ref NativeArray<T> values, int dimension)
        {
            int startIndex = 0, endIndex = values.Length - 1, midIndex = endIndex >> 1, 
                divIndex = __PartSort(ref values, dimension, startIndex, endIndex);

            while(divIndex != midIndex)
                divIndex = midIndex < divIndex ? 
                    __PartSort(ref values, dimension, startIndex, divIndex - 1) : 
                    __PartSort(ref values, dimension, divIndex + 1, endIndex);

            return midIndex;
        }

        public static int __PartSort(ref NativeArray<T> values, int dimension, int startIndex, int endIndex)
        {
            int left = startIndex, right = endIndex;
            float key = values[endIndex].Get(dimension);

            while(true)
            {
                while (left < right && values[left].Get(dimension) <= key)
                    ++left;

                while (left < right && values[right].Get(dimension) >= key)
                    --right;

                if (left < right)
                    Math.Swap(ref values.ElementAt(left), ref values.ElementAt(right));
                else
                    break;
            }

            Math.Swap(ref values.ElementAt(right), ref values.ElementAt(endIndex));

            return left;
        }
    }

    public static class NativeKDTreeUtility
    {
        private struct ArrayCollector<T> : IKDTreeCollector<T> where T : struct
        {
            public int count;
            public NativeArray<T> values;

            public bool Test(in T value) => count < values.Length;

            public bool Add(in T value)
            {
                values[count++] = value;

                return true;
            }
        }

        private struct ListCollector<T> : IKDTreeCollector<T> where T : unmanaged
        {
            public NativeList<T> values;

            public bool Test(in T value) => true;

            public bool Add(in T value)
            {
                values.Add(value);

                return true;
            }
        }

        public static int Query<T>(
            this in NativeKDTreeNode<T> node,
            in T min,
            in T max,
            ref NativeArray<T> values)
            where T : unmanaged, IKDTreeValue
        {
            ArrayCollector<T> collector;
            collector.count = 0;
            collector.values = values;

            node.Query(min, max, ref collector);

            return collector.count;
        }

        public static bool Query<T>(
            this in NativeKDTreeNode<T> node, 
            in T min,
            in T max,
            ref NativeList<T> values)
            where T : unmanaged, IKDTreeValue
        {
            ListCollector<T> collector;
            collector.values = values;

            return node.Query(min, max, ref collector);
        }
    }
}