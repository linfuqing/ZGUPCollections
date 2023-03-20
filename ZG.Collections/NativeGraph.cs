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

namespace ZG
{
    internal struct UnsafeGraphObject<T> where T : struct
    {
        public UnsafeGraphNode node;
        public T value;
    }

    internal struct UnsafeGraphNode
    {
        internal struct AdjacentNode
        {
            public float distance;
            public unsafe UnsafeGraphNode* value;
        }

        internal UnsafeList<AdjacentNode> _adjacentNodes;

        private NativeFactoryObject __target;

        public bool isCreated => __target.isCreated;

        public static unsafe UnsafeGraphNode* Create<T>(
            ref UnsafeFactory factory,
            in T value)
            where T : struct
        {
            var result = factory.Create<UnsafeGraphObject<T>>();
            ref var target = ref result.As<UnsafeGraphObject<T>>();
            target.node.__target = result;
            target.node._adjacentNodes = new UnsafeList<AdjacentNode>(0, factory.allocator);

            target.value = value;

            return (UnsafeGraphNode*)UnsafeUtility.AddressOf(ref target.node);
        }

        public ref T As<T>() where T : struct => ref __target.As<UnsafeGraphObject<T>>().value;

        public void Dispose()
        {
            _adjacentNodes.Dispose();

            __target.Dispose();
        }

        public unsafe void Link(float distance, UnsafeGraphNode* value)
        {
            __CheckFactory(value);

            AdjacentNode adjacentNode;
            adjacentNode.distance = distance;
            adjacentNode.value = value;

            _adjacentNodes.Add(adjacentNode);
        }

        public unsafe bool Unlink(UnsafeGraphNode* value)
        {
            int numAdjacentNodes = _adjacentNodes.Length;
            for(int i = 0; i < numAdjacentNodes; ++i)
            {
                if (_adjacentNodes.ElementAt(i).value == value)
                {
                    _adjacentNodes.RemoveAtSwapBack(i);

                    return true;
                }
            }

            return false;
        }

        public unsafe void Visit<T>(
            float distance,
            float maxDistance,
            ref NativeHashMap<T, float> nodeDistances) where T : unmanaged, IEquatable<T>
        {
            if (!nodeDistances.TryAdd(As<T>(), distance))
                return;

            float adjacentNodeDistance;
            int numAdjacentNodes = _adjacentNodes.Length;
            for (int i = 0; i < numAdjacentNodes; ++i)
            {
                ref var adjacentNode = ref _adjacentNodes.ElementAt(i);
                if (adjacentNode.value == null || !adjacentNode.value->isCreated)
                    continue;

                adjacentNodeDistance = distance + adjacentNode.distance;
                if (adjacentNodeDistance <= maxDistance)
                    adjacentNode.value->Visit(adjacentNodeDistance, maxDistance, ref nodeDistances);
            }
        }

        public unsafe static void Visit<T>(
            UnsafeGraphNode* source,
            UnsafeGraphNode* destination,
            float fromDistance,
            float toDistance,
            ref NativeHashMap<T, float> addSet,
            ref NativeHashMap<T, float> removeSet,
            in NativeHashMap<T, float> originSet = default) where T : unmanaged, IEquatable<T>
        {
            source->Visit(0.0f, toDistance, ref addSet);
            if (originSet.IsCreated)
            {
                T value;
                foreach(var pair in originSet)
                {
                    value = pair.Key;
                    if (!addSet.Remove(value))
                        removeSet.Add(value, pair.Value);
                }
            }
            else
            {
                destination->Visit(0.0f, fromDistance, ref removeSet);

                using (var values = removeSet.GetKeyArray(Allocator.Temp))
                {
                    foreach (T value in values)
                    {
                        if (addSet.Remove(value))
                            removeSet.Remove(value);
                    }
                }
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private unsafe void __CheckFactory(UnsafeGraphNode* value)
        {
            if (value == null)
                throw new NullReferenceException();

            if (UnsafeUtility.AddressOf(ref value->__target.enumerable) != UnsafeUtility.AddressOf(ref __target.enumerable))
                throw new InvalidCastException();
        }
    }

    public struct UnsafeGraphNode<T> where T : unmanaged, IEquatable<T>
    {
        internal unsafe UnsafeGraphNode* _value;

        public unsafe bool isCreated => _value != null && _value->isCreated;

        public unsafe ref T value => ref _value->As<T>();

        public unsafe void Dispose() => _value->Dispose();

        public unsafe void Link(float distance, in UnsafeGraphNode<T> value)
        {
            _value->Link(distance, value._value);
        }

        public unsafe void Unlink(in UnsafeGraphNode<T> value)
        {
            _value->Unlink(value._value);
        }

        public unsafe void Visit(
            float maxDistance,
            ref NativeHashMap<T, float> nodeDistances) => _value->Visit(0.0f, maxDistance, ref nodeDistances);

        public static unsafe void Visit(
            in UnsafeGraphNode<T> source,
            in UnsafeGraphNode<T> destination,
            float fromDistance,
            float toDistance,
            ref NativeHashMap<T, float> addSet,
            ref NativeHashMap<T, float> removeSet,
            in NativeHashMap<T, float> originSet = default)
        {
            UnsafeGraphNode.Visit(
                source._value, 
                destination._value, 
                fromDistance, 
                toDistance, 
                ref addSet, 
                ref removeSet, 
                originSet);
        }
    }

    public struct UnsafeGraph<T> where T : unmanaged, IEquatable<T>
    {
        private UnsafeFactory __factroy;

        public bool isCreated => __factroy.isCreated;

        public UnsafeGraph(in AllocatorManager.AllocatorHandle allocator, bool isParallelWritable = false)
        {
            __factroy = new UnsafeFactory(allocator, isParallelWritable);
        }

        public void Dispose()
        {
            foreach(var target in __factroy.enumerable)
                target.As<UnsafeGraphObject<T>>().node._adjacentNodes.Dispose();

            __factroy.Dispose();
        }

        public void Clear()
        {
            foreach (var target in __factroy.enumerable)
                target.As<UnsafeGraphObject<T>>().node._adjacentNodes.Dispose();

            __factroy.Clear();
        }

        public unsafe UnsafeGraphNode<T> Create(in T value)
        {
            UnsafeGraphNode<T> node;
            node._value = UnsafeGraphNode.Create<T>(ref __factroy, value);

            return node;
        }
    }

    public struct UnsafeGraphEx<T> where T : unmanaged, IEquatable<T>
    {
        private UnsafeGraph<T> __value;
        private UnsafeParallelHashMap<T, UnsafeGraphNode<T>> __nodes;

        public bool isCreated => __value.isCreated;

        public UnsafeGraphEx(AllocatorManager.AllocatorHandle allocator, bool isParallelWritable = false)
        {
            __value = new UnsafeGraph<T>(allocator, isParallelWritable);

            __nodes = new UnsafeParallelHashMap<T, UnsafeGraphNode<T>>(1, allocator);
        }

        public void Dispose()
        {
            __nodes.Dispose();

            __value.Dispose();
        }

        public void Clear()
        {
            __nodes.Clear();

            __value.Clear();
        }

        public void Add(in T value)
        {
            __CheckKey(value);

            __nodes.Add(value, __value.Create(value));
        }

        public bool Remove(in T value)
        {
            if (__nodes.TryGetValue(value, out var node))
            {
                node.Dispose();

                __nodes.Remove(value);

                return true;
            }

            return false;
        }

        public void Link(in T source, in T destination, float distance)
        {
            __nodes[source].Link(distance, __nodes[destination]);
        }

        public void Unlink(in T source, in T destination)
        {
            __nodes[source].Unlink(__nodes[destination]);
        }

        public void Visit(
            in T value, 
            float maxDistance,
            ref NativeHashMap<T, float> nodeDistances)
        {
            __nodes[value].Visit(maxDistance, ref nodeDistances);
        }

        public void Visit(
            in T source,
            in T destination,
            float fromDistance,
            float toDistance,
            ref NativeHashMap<T, float> addSet,
            ref NativeHashMap<T, float> removeSet,
            in NativeHashMap<T, float> originSet = default)
        {
            UnsafeGraphNode<T>.Visit(
                __nodes[source], 
                __nodes[destination], 
                fromDistance, 
                toDistance,
                ref addSet, 
                ref removeSet,
                originSet);
        }


        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private unsafe void __CheckKey(T key)
        {
            if (__nodes.ContainsKey(key))
                throw new InvalidOperationException();
        }

    }

    public struct NativeGraphNode<T> where T : unmanaged, IEquatable<T>
    {
        internal UnsafeGraphNode<T> _value;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif

        public bool isCreated => _value.isCreated;

        public T value
        {
            get
            {
                __CheckRead();

                return _value.value;
            }
        }

        public void Dispose()
        {
            __CheckWrite();

            _value.Dispose();
        }

        public void Link(float distance, in NativeGraphNode<T> value)
        {
            __CheckWrite();

            _value.Link(distance, value._value);
        }

        public void Unlink(in NativeGraphNode<T> value)
        {
            __CheckWrite();

            _value.Unlink(value._value);
        }

        public unsafe void Visit(
            //float distance,
            float maxDistance,
            ref NativeHashMap<T, float> nodeDistances)
        {
            __CheckRead();

            _value.Visit(maxDistance, ref nodeDistances);
        }

        public unsafe void Visit(
            in UnsafeGraphNode<T> target,
            float fromDistance,
            float toDistance,
            ref NativeHashMap<T, float> addSet,
            ref NativeHashMap<T, float> removeSet,
            in NativeHashMap<T, float> originSet = default)
        {
            __CheckRead();

            UnsafeGraphNode<T>.Visit(_value, target, fromDistance, toDistance, ref addSet, ref removeSet, originSet);
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
    }

    [NativeContainer]
    public struct NativeGraph<T> where T : unmanaged, IEquatable<T>
    {
        private UnsafeGraph<T> __value;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;

#if REMOVE_DISPOSE_SENTINEL
#else
        [NativeSetClassTypeToNullOnSchedule]
        internal DisposeSentinel m_DisposeSentinel;
#endif

        internal static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<NativeGraphEx<T>>();
#endif

        public bool isCreated => __value.isCreated;

        public NativeGraph(in AllocatorManager.AllocatorHandle allocator, bool isParallelWritable = false)
        {
            __value = new UnsafeGraph<T>(allocator, isParallelWritable);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
#if REMOVE_DISPOSE_SENTINEL
            m_Safety = CollectionHelper.CreateSafetyHandle(allocator.Handle);
#else
            if (allocator.IsCustomAllocator)
            {
                m_Safety = AtomicSafetyHandle.Create();
                m_DisposeSentinel = null;
            }
            else
                DisposeSentinel.Create(out m_Safety, out m_DisposeSentinel, 0, allocator.ToAllocator);
#endif
            CollectionHelper.SetStaticSafetyId<NativeGraphEx<T>>(ref m_Safety, ref StaticSafetyID.Data);
#endif
        }

        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckDeallocateAndThrow(m_Safety);

#if REMOVE_DISPOSE_SENTINEL
            AtomicSafetyHandle.Release(m_Safety);
#else
            DisposeSentinel.Dispose(ref m_Safety, ref m_DisposeSentinel);
#endif
#endif

            __value.Dispose();
        }

        public void Clear()
        {
            __CheckWrite();

            __value.Clear();
        }

        public NativeGraphNode<T> Create(in T value)
        {
            __CheckWrite();

            NativeGraphNode<T> node;
            node._value = __value.Create(value);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            node.m_Safety = m_Safety;
#endif

            return node;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private unsafe void __CheckWrite()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
        }
    }


    [NativeContainer]
    public struct NativeGraphEx<T> where T : unmanaged, IEquatable<T>
    {
        private UnsafeGraphEx<T> __value;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;

        internal static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<NativeGraphEx<T>>();
#endif

        public bool isCreated => __value.isCreated;

        public NativeGraphEx(in AllocatorManager.AllocatorHandle allocator, bool isParallelWritable = false)
        {
            __value = new UnsafeGraphEx<T>(allocator, isParallelWritable);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = CollectionHelper.CreateSafetyHandle(allocator.Handle);

            //AtomicSafetyHandle.SetNestedContainer(m_Safety, true);

            CollectionHelper.SetStaticSafetyId<NativeGraphEx<T>>(ref m_Safety, ref StaticSafetyID.Data);
#endif
        }

        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            CollectionHelper.DisposeSafetyHandle(ref m_Safety);
#endif

            __value.Dispose();
        }

        public void Clear()
        {
            __CheckWriteAndBumpSecondaryVersion();

            __value.Clear();
        }

        public void Add(in T value)
        {
            __CheckWriteAndBumpSecondaryVersion();

            __value.Add(value);
        }

        public void Remove(in T value)
        {
            __CheckWriteAndBumpSecondaryVersion();

            __value.Remove(value);
        }
        public void Link(in T source, in T destination, float distance)
        {
            __CheckWrite();

            __value.Link(source, destination, distance);
        }

        public void Unlink(in T source, in T destination)
        {
            __CheckWrite();

            __value.Unlink(source, destination);
        }

        public void Visit(
            in T value,
            float maxDistance,
            ref NativeHashMap<T, float> nodeDistances)
        {
            __CheckRead();

            __value.Visit(value, maxDistance, ref nodeDistances);
        }

        public void Visit(
            in T source,
            in T destination,
            float fromDistance,
            float toDistance,
            ref NativeHashMap<T, float> addSet,
            ref NativeHashMap<T, float> removeSet,
            in NativeHashMap<T, float> originSet = default)
        {
            __CheckRead();

            __value.Visit(
                source, 
                destination, 
                fromDistance, 
                toDistance, 
                ref addSet, 
                ref removeSet, 
                originSet);
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
    }
}