using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using ZG;

namespace ZG
{
    internal struct UnsafeRBTreeNode
    {
        public bool isRed;
        public NativeFactoryObject target;

        public unsafe UnsafeRBTreeNode* parent;
        public unsafe UnsafeRBTreeNode* leftChild;
        public unsafe UnsafeRBTreeNode* rightChild;

        public unsafe UnsafeRBTreeNode* previous
        {
            get
            {
                UnsafeRBTreeNode* node = (UnsafeRBTreeNode*)UnsafeUtility.AddressOf(ref this);
                if (node->leftChild == null)
                {
                    while (node->parent != null)
                    {
                        if (node->parent->rightChild == node)
                            return node->parent;

                        node = node->parent;
                    }

                    return null;
                }

                node = node->leftChild;
                while (node->rightChild != null)
                    node = node->rightChild;

                return node;
            }
        }

        public unsafe UnsafeRBTreeNode* next
        {
            get
            {
                UnsafeRBTreeNode* node = (UnsafeRBTreeNode*)UnsafeUtility.AddressOf(ref this);
                if (node->rightChild == null)
                {
                    while (node->parent != null)
                    {
                        if (node->parent->leftChild == node)
                            return node->parent;

                        node = node->parent;
                    }

                    return null;
                }

                node = node->rightChild;
                while (node->leftChild != null)
                    node = node->leftChild;

                return node;
            }
        }

        public unsafe UnsafeRBTreeNode* root
        {
            get
            {
                if (parent == null)
                    return (UnsafeRBTreeNode*)UnsafeUtility.AddressOf(ref this);

                return parent->root;
            }
        }

        public ref T As<T>()
        {
            return ref target.As<UnsafeRBTreeObject<T>>().value;
        }
    }

    internal struct UnsafeRBTreeObject<T>
    {
        public UnsafeRBTreeNode node;
        public T value;
    }

    /*internal struct UnsafeRBTreeNodePool : IDisposable
    {
        public struct Concurrent<T>
            where T : struct
        {
            private static bool __isInit;
            private static UnsafeRBTreeNodePool __instance;

            [NativeDisableUnsafePtrRestriction]
            private unsafe UnsafeRBTreeNodePool* __pool;

            private int __capacity;

            public unsafe Concurrent(int capacity)
            {
                if (!__isInit)
                {
                    __isInit = true;
    #if !NET_DOTS
                    AppDomain.CurrentDomain.DomainUnload += __OnDomainUnload;
    #endif
                }

                __pool = (UnsafeRBTreeNodePool*)UnsafeUtility.AddressOf(ref __instance);
                __capacity = capacity;
            }

            public unsafe UnsafeRBTreeNode* Allocate(T value)
            {
                if (__pool == null)
                    return null;

                UnsafeRBTreeNode* result, node;
                while (true)
                {
                    node = (UnsafeRBTreeNode*)(void*)__pool->__nodes;
                    if (node == null)
                    {
                        Interlocked.Increment(ref __pool->__count);

                        result = (UnsafeRBTreeNode*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<UnsafeRBTreeNode>(), UnsafeUtility.AlignOf<UnsafeRBTreeNode>(), Allocator.Persistent);

                        result->version = 0;

                        result->value = UnsafeUtility.Malloc(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>(), Allocator.Persistent);

                        break;
                    }

                    if (Interlocked.CompareExchange(ref __pool->__nodes, (IntPtr)(void*)node->parent, (IntPtr)(void*)node) != (IntPtr)(void*)node)
                        continue;

                    result = node;

                    break;
                }

                result->isRed = true;

                UnsafeUtility.WriteArrayElement(result->value, 0, value);

                return result;
            }

            public unsafe void Free(UnsafeRBTreeNode* node)
            {
                if (__pool == null)
                {
                    if (node != null)
                    {
                        UnsafeUtility.Free(node->value, Allocator.Persistent);
                        UnsafeUtility.Free(node, Allocator.Persistent);
                    }
                }
                else
                    __pool->Free(node, __capacity);
            }

    #if !NET_DOTS
            private static void __OnDomainUnload(object sender, EventArgs e)
            {
                __instance.Dispose();
            }
    #endif
        }

        private IntPtr __nodes;
        private int __count;

        public unsafe void Free(UnsafeRBTreeNode* node, int capacity)
        {
            if (__count > capacity)
            {
                if (Interlocked.Decrement(ref __count) + 1 > capacity)
                {
                    UnsafeUtility.Free(node->value, Allocator.Persistent);
                    UnsafeUtility.Free(node, Allocator.Persistent);

                    return;
                }

                Interlocked.Increment(ref __count);
            }

            ++node->version;
            do
            {
                node->parent = (UnsafeRBTreeNode*)(void*)__nodes;
            }
            while (Interlocked.CompareExchange(ref __nodes, (IntPtr)(void*)node, (IntPtr)(void*)(node->parent)) != (IntPtr)(void*)(node->parent));
        }

        public unsafe void Dispose()
        {
            while (__nodes != IntPtr.Zero)
            {
                UnsafeRBTreeNode* node = (UnsafeRBTreeNode*)(void*)__nodes;
                __nodes = (IntPtr)(void*)node->parent;
                UnsafeUtility.Free(node->value, Allocator.Persistent);
                UnsafeUtility.Free(node, Allocator.Persistent);
            }
        }
    }*/

    internal unsafe struct UnsafeRBTreeInfo
    {
        [NativeDisableUnsafePtrRestriction]
        private UnsafeRBTreeNode* __root;

        public UnsafeRBTreeNode* root
        {
            get
            {
                return __root;
            }
        }

        public static UnsafeRBTreeNode* Get<T>(UnsafeRBTreeNode* node, T value) where T : struct, IComparable<T>
        {
            if (node == null)
                return null;

            int comparsionValue;
            UnsafeRBTreeNode* result = null;
            while (true)
            {
                comparsionValue = value.CompareTo(node->As<T>());
                if (comparsionValue < 0)
                {
                    if (result != null)
                        break;

                    if (node->leftChild == null)
                        break;
                    else
                        node = node->leftChild;
                }
                else
                {
                    if (comparsionValue == 0)
                        result = node;

                    if (node->rightChild == null)
                        break;
                    else
                        node = node->rightChild;
                }
            }

            return result;
        }

        public UnsafeRBTreeNode* Get<T>(T value) where T : struct, IComparable<T>
        {
            return Get(__root, value);
        }

        public bool Add<T>(UnsafeRBTreeNode* node, bool isAllowDuplicate) where T : struct, IComparable<T>
        {
            if (node == null)
                return false;

            node->parent = null;
            node->leftChild = null;
            node->rightChild = null;

            UnsafeRBTreeNode* parent = __root;
            if (parent == null)
            {
                node->isRed = false;

                __root = node;

                return true;
            }

            while (true)
            {
                int comparsionValue = node->As<T>().CompareTo(parent->As<T>());
                if (comparsionValue < 0)
                {
                    if (parent->leftChild == null)
                    {
                        node->parent = parent;

                        parent->leftChild = node;

                        break;
                    }
                    else
                        parent = parent->leftChild;
                }
                else
                {
                    if (comparsionValue == 0 && !isAllowDuplicate)
                        return false;

                    if (parent->rightChild == null)
                    {
                        node->parent = parent;

                        parent->rightChild = node;

                        break;
                    }
                    else
                        parent = parent->rightChild;
                }
            }

            return __InsertFiexup(node);
        }

        public bool Remove(UnsafeRBTreeNode* node)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (node == null || node->root != __root)
                throw new NullReferenceException();
#endif

            UnsafeRBTreeNode* temp;
            if (node->leftChild == null || node->rightChild == null)
                temp = node;
            else
            {
                temp = node->rightChild;
                while (temp->leftChild != null)
                    temp = temp->leftChild;
            }

            if (temp->leftChild == null && temp->rightChild == null)
            {
                __RemoveFiexup(temp);

                if (temp->parent == null)
                    __root = null;
                else
                {
                    if (temp->parent->leftChild == temp)
                        temp->parent->leftChild = null;

                    if (temp->parent->rightChild == temp)
                        temp->parent->rightChild = null;
                }
            }
            else
            {
                UnsafeRBTreeNode* child = null;
                if (temp->leftChild != null)
                {
                    child = temp->leftChild;
                    temp->leftChild = null;
                }

                if (temp->rightChild != null)
                {
                    child = temp->rightChild;
                    temp->rightChild = null;
                }

                if (__Replace(child, temp))
                {
                    if (__root == temp)
                        __root = child;
                }
                else
                    return false;
            }

            if (__Replace(temp, node))
            {
                if (__root == node)
                    __root = temp;
            }
            else
                return false;

            return true;
        }

        private bool __LeftRotate(UnsafeRBTreeNode* node)
        {
            if (node == null || node->rightChild == null)
                return false;

            UnsafeRBTreeNode* rightChild = node->rightChild;
            node->rightChild = rightChild->leftChild;

            if (node->rightChild != null)
                node->rightChild->parent = node;

            rightChild->parent = node->parent;

            if (node->parent == null)
                __root = rightChild;
            else
            {
                if (node->parent->leftChild == node)
                    node->parent->leftChild = rightChild;

                if (node->parent->rightChild == node)
                    node->parent->rightChild = rightChild;
            }

            rightChild->leftChild = node;

            node->parent = rightChild;

            return true;
        }

        private bool __RightRotate(UnsafeRBTreeNode* node)
        {
            if (node->leftChild == null)
                return false;

            UnsafeRBTreeNode* leftChild = node->leftChild;
            node->leftChild = leftChild->rightChild;

            if (node->leftChild != null)
                node->leftChild->parent = node;

            leftChild->parent = node->parent;

            if (node->parent == null)
                __root = leftChild;
            else
            {
                if (node->parent->leftChild == node)
                    node->parent->leftChild = leftChild;

                if (node->parent->rightChild == node)
                    node->parent->rightChild = leftChild;
            }

            leftChild->rightChild = node;

            node->parent = leftChild;

            return true;
        }

        private bool __InsertFiexup(UnsafeRBTreeNode* node)
        {
            if (node == null || !node->isRed)
                return false;

            if (node->parent == null)
            {
                node->isRed = false;

                __root = node;

                return true;
            }

            if (!node->parent->isRed)
                return true;

            if (node->parent->parent->leftChild == node->parent)
            {
                UnsafeRBTreeNode* pSibling = node->parent->parent->rightChild;
                if (pSibling != null && pSibling->isRed)
                {
                    node->parent->isRed = false;
                    pSibling->isRed = false;

                    node->parent->parent->isRed = true;

                    return __InsertFiexup(node->parent->parent);
                }

                UnsafeRBTreeNode* temp;
                if (node->parent->rightChild == node)
                {
                    temp = node->parent;
                    if (!__LeftRotate(temp))
                        return false;
                }
                else
                    temp = node;

                temp->parent->isRed = false;
                temp->parent->parent->isRed = true;

                if (__RightRotate(temp->parent->parent))
                    return __InsertFiexup(temp);
            }

            if (node->parent->parent->rightChild == node->parent)
            {
                UnsafeRBTreeNode* pSibling = node->parent->parent->leftChild;
                if (pSibling != null && pSibling->isRed)
                {
                    node->parent->isRed = false;
                    pSibling->isRed = false;

                    node->parent->parent->isRed = true;

                    return __InsertFiexup(node->parent->parent);
                }

                UnsafeRBTreeNode* temp;
                if (node->parent->leftChild == node)
                {
                    temp = node->parent;
                    if (!__RightRotate(temp))
                        return false;
                }
                else
                    temp = node;

                temp->parent->isRed = false;
                temp->parent->parent->isRed = true;

                if (__LeftRotate(temp->parent->parent))
                    return __InsertFiexup(temp);
            }

            return false;
        }

        private bool __RemoveFiexup(UnsafeRBTreeNode* node)
        {
            if (node->isRed)
            {
                node->isRed = false;

                return true;
            }

            if (node->parent == null)
            {
                __root = node;

                return true;
            }

            if (node->parent->leftChild == node)
            {
                UnsafeRBTreeNode* sibling = node->parent->rightChild;
                if (sibling->isRed)
                {
                    sibling->isRed = false;
                    node->parent->isRed = true;

                    if (!__LeftRotate(node->parent))
                        return false;

                    sibling = node->parent->rightChild;
                }

                if ((sibling->leftChild == null || !sibling->leftChild->isRed) &&
                    (sibling->rightChild == null || !sibling->rightChild->isRed))
                {
                    sibling->isRed = true;

                    if (__RemoveFiexup(node->parent))
                        return true;
                }
                else if (sibling->rightChild == null || !sibling->rightChild->isRed)
                {
                    sibling->leftChild->isRed = false;
                    sibling->isRed = true;

                    if (!__RightRotate(sibling))
                        return false;

                    sibling = node->parent->rightChild;
                }

                sibling->isRed = node->parent->isRed;
                node->parent->isRed = false;
                sibling->rightChild->isRed = false;

                if (!__LeftRotate(node->parent))
                    return false;
            }

            if (node->parent->rightChild == node)
            {
                UnsafeRBTreeNode* sibling = node->parent->leftChild;
                if (sibling->isRed)
                {
                    sibling->isRed = false;
                    node->parent->isRed = true;

                    if (!__RightRotate(node->parent))
                        return false;

                    sibling = node->parent->leftChild;
                }

                if ((sibling->leftChild == null || !sibling->leftChild->isRed) &&
                    (sibling->rightChild == null || !sibling->rightChild->isRed))
                {
                    sibling->isRed = true;

                    if (__RemoveFiexup(node->parent))
                        return true;
                }
                else if (sibling->leftChild == null || !sibling->leftChild->isRed)
                {
                    sibling->rightChild->isRed = false;
                    sibling->isRed = true;

                    if (!__LeftRotate(sibling))
                        return false;

                    sibling = node->parent->leftChild;
                }

                sibling->isRed = node->parent->isRed;
                node->parent->isRed = false;
                sibling->leftChild->isRed = false;

                if (!__RightRotate(node->parent))
                    return false;
            }

            return true;
        }

        private bool __Replace(UnsafeRBTreeNode* destination, UnsafeRBTreeNode* source)
        {
            if (destination == null || source == null)
                return false;

            if (destination == source)
                return true;

            destination->isRed = source->isRed;
            destination->parent = source->parent;
            destination->leftChild = source->leftChild;
            destination->rightChild = source->rightChild;

            if (source->parent != null)
            {
                if (source->parent->leftChild == source)
                    source->parent->leftChild = destination;

                if (source->parent->rightChild == source)
                    source->parent->rightChild = destination;
            }

            if (source->leftChild != null)
                source->leftChild->parent = destination;

            if (source->rightChild != null)
                source->rightChild->parent = destination;

            return true;
        }
    }

    internal struct UnsafeRBTreeData
    {
        public int count;

        public UnsafeRBTreeInfo info;

        public unsafe UnsafeRBTreeNode* head;
        public unsafe UnsafeRBTreeNode* tail;
    }

    public struct NativeRBTreeNode : IEquatable<NativeRBTreeNode>
    {
        internal int _version;

        [NativeDisableUnsafePtrRestriction]
        internal unsafe UnsafeRBTreeNode* _instance;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif

        public unsafe bool isNull
        {
            get
            {
                return _instance == null;
            }
        }

        public unsafe bool isVail
        {
            get
            {
                if (_instance == null)
                    return false;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckExistsAndThrow(m_Safety);
#endif
                return _instance->target.isVail && _instance->target.version == _version;
            }
        }

        public unsafe NativeRBTreeNode previous
        {
            get
            {
                NativeRBTreeNode node;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (!isVail)
                    throw new InvalidOperationException();

                node.m_Safety = m_Safety;

                AtomicSafetyHandle.CheckReadAndThrow(node.m_Safety);
#endif

                node._instance = _instance == null ? null : _instance->previous;
                node._version = node._instance == null ? 0 : node._instance->target.version;
                return node;
            }
        }

        public unsafe NativeRBTreeNode next
        {
            get
            {
                NativeRBTreeNode node;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (!isVail)
                    throw new InvalidOperationException();

                node.m_Safety = m_Safety;

                AtomicSafetyHandle.CheckReadAndThrow(node.m_Safety);
#endif

                node._instance = _instance == null ? null : _instance->next;
                node._version = node._instance == null ? 0 : node._instance->target.version;
                return node;
            }
        }

        public unsafe NativeRBTreeNode leftChild
        {
            get
            {
                NativeRBTreeNode node;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (!isVail)
                    throw new InvalidOperationException();

                node.m_Safety = m_Safety;

                AtomicSafetyHandle.CheckReadAndThrow(node.m_Safety);
#endif

                node._instance = _instance == null ? null : _instance->leftChild;
                node._version = node._instance == null ? 0 : node._instance->target.version;
                return node;
            }
        }

        public unsafe NativeRBTreeNode rightChild
        {
            get
            {
                NativeRBTreeNode node;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (!isVail)
                    throw new InvalidOperationException();

                node.m_Safety = m_Safety;

                AtomicSafetyHandle.CheckReadAndThrow(node.m_Safety);
#endif

                node._instance = _instance == null ? null : _instance->rightChild;
                node._version = node._instance == null ? 0 : node._instance->target.version;
                return node;
            }
        }

        public unsafe ref T As<T>()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);

            if (_instance == null)
                throw new InvalidOperationException();
#endif

            return ref _instance->As<T>();
        }

        public unsafe T AsReadOnly<T>()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);

            if (_instance == null)
                throw new InvalidOperationException();
#endif

            return _instance->As<T>();
        }

        public unsafe bool Equals(NativeRBTreeNode other)
        {
            return _version == other._version && _instance == other._instance;
        }
    }

    public struct NativeRBTreeNode<T> : IEquatable<NativeRBTreeNode<T>>
    {
        private NativeRBTreeNode __instance;

        public bool isNull => __instance.isNull;

        public bool isVail => __instance.isVail;

        public NativeRBTreeNode<T> previous => (NativeRBTreeNode<T>)__instance.previous;

        public NativeRBTreeNode<T> next => (NativeRBTreeNode<T>)__instance.next;

        public NativeRBTreeNode<T> leftChild => (NativeRBTreeNode<T>)__instance.leftChild;

        public NativeRBTreeNode<T> rightChild => (NativeRBTreeNode<T>)__instance.rightChild;

        public ref T value => ref __instance.As<T>();

        public T valueReadOnly => __instance.AsReadOnly<T>();

        public static explicit operator NativeRBTreeNode<T>(NativeRBTreeNode instance)
        {
            NativeRBTreeNode<T> result;
            result.__instance = instance;
            return result;
        }

        public static implicit operator NativeRBTreeNode(NativeRBTreeNode<T> instance)
        {
            return instance.__instance;
        }

        public unsafe bool Equals(NativeRBTreeNode<T> other)
        {
            return __instance.Equals(other.__instance);
        }
    }

    public struct NativeRBTreeEnumerator
    {
        private enum Status
        {
            None,
            Left,
            Right
        }

        private Status __status;

        [NativeDisableUnsafePtrRestriction]
        internal unsafe UnsafeRBTreeNode* _source;

        [NativeDisableUnsafePtrRestriction]
        internal unsafe UnsafeRBTreeNode* _destination;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif

        public unsafe NativeRBTreeEnumerator(in NativeRBTreeNode node)
        {
            __status = Status.None;
            _source = node._instance;
            _destination = null;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = node.m_Safety;
#endif
        }

        public unsafe ref T As<T>() where T : struct
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);

            if (_destination == null)
                throw new InvalidOperationException();
#endif

            return ref _destination->As<T>();
        }

        public unsafe T AsReadOnly<T>() where T : struct
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);

            if (_destination == null)
                throw new InvalidOperationException();
#endif

            return _destination->As<T>();
        }

        public unsafe bool MoveNext()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

            switch (__status)
            {
                case Status.None:
                    if (_source == null)
                        return false;

                    __status = Status.Left;

                    _destination = _source;

                    return true;
                case Status.Left:
                    _destination = _destination->previous;
                    if (_destination == null)
                    {
                        __status = Status.Right;

                        _destination = _source->next;
                        if (_destination == null)
                            return false;
                    }

                    return true;
                case Status.Right:
                    _destination = _destination == null ? null : _destination->next;
                    if (_destination == null)
                        return false;

                    return true;
            }

            return false;
        }

        public void Reset()
        {
            __status = Status.None;
        }
    }

    public struct NativeRBTreeEnumerator<T> : IEnumerator<T> where T : struct
    {
        private NativeRBTreeEnumerator __instance;

        public T Current => __instance.AsReadOnly<T>();

        public unsafe NativeRBTreeEnumerator(in NativeRBTreeNode node)
        {
            __instance = new NativeRBTreeEnumerator(node);
        }

        public unsafe bool MoveNext() => __instance.MoveNext();

        public void Reset() => __instance.Reset();

        void IDisposable.Dispose()
        {
        }

        object IEnumerator.Current
        {
            get
            {
                return Current;
            }
        }
    }

    [NativeContainer]
    public struct NativeRBTreeLite<T> : IDisposable, IEnumerable<T>
        where T : struct, IComparable<T>
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif

        [NativeDisableUnsafePtrRestriction]
        private unsafe UnsafeRBTreeData* __data;
        //private UnsafeRBTreeNodePool.Concurrent<T> __pool;
        private UnsafeFactory __factory;

        public unsafe bool isCreated
        {
            get
            {
                return __data != null;
            }
        }

        public unsafe int count
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                return __data->count;
            }
        }

        public unsafe NativeRBTreeNode<T> head
        {
            get
            {
                NativeRBTreeNode node;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);

                node.m_Safety = m_Safety;
#endif

                node._instance = __data->head;
                node._version = node._instance == null ? 0 : node._instance->target.version;
                return (NativeRBTreeNode<T>)node;
            }
        }

        public unsafe NativeRBTreeNode<T> tail
        {
            get
            {
                NativeRBTreeNode node;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);

                node.m_Safety = m_Safety;
#endif

                node._instance = __data->tail;
                node._version = node._instance == null ? 0 : node._instance->target.version;
                return (NativeRBTreeNode<T>)node;
            }
        }

        public unsafe NativeRBTreeNode<T> root
        {
            get
            {
                NativeRBTreeNode node;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);

                node.m_Safety = m_Safety;
#endif

                node._instance = __data->info.root;
                node._version = node._instance == null ? 0 : node._instance->target.version;
                return (NativeRBTreeNode<T>)node;
            }
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal
#else
    public
#endif
        unsafe NativeRBTreeLite(Allocator allocator
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        , AtomicSafetyHandle safety
#endif
        )
        {
            if (!UnsafeUtility.IsUnmanaged<T>())
                throw new ArgumentException(string.Format("{0} used in NativeRBTree<{0}> must be Unmanaged", typeof(T)));

            __data = AllocatorManager.Allocate<UnsafeRBTreeData>(allocator);
            UnsafeUtility.MemClear(__data, UnsafeUtility.SizeOf<UnsafeRBTreeData>());

            __factory = new UnsafeFactory(allocator);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = safety;
#endif
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        public unsafe NativeRBTreeLite(Allocator allocator) : this(allocator, AtomicSafetyHandle.Create())
        {
        }
#endif

        public unsafe NativeRBTreeNode<T> Get(in T value)
        {
            NativeRBTreeNode node;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);

            node.m_Safety = m_Safety;
#endif

            node._instance = __data->info.Get(value);
            node._version = node._instance == null ? 0 : node._instance->target.version;
            return (NativeRBTreeNode<T>)node;
        }

        public unsafe NativeRBTreeNode<T> Add(in T value, bool isAllowDuplicate)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif

            var factoryObject = __factory.Create<UnsafeRBTreeObject<T>>();
            ref var target = ref factoryObject.As<UnsafeRBTreeObject<T>>();
            target.value = value;
            target.node.isRed = true;
            target.node.target = factoryObject;

            UnsafeRBTreeNode* node = (UnsafeRBTreeNode*)UnsafeUtility.AddressOf(ref target.node);
            if (__data->info.Add<T>(node, isAllowDuplicate))
            {
                ++__data->count;

                if (__data->head == null || __data->head->As<T>().CompareTo(value) > 0)
                    __data->head = node;

                UnityEngine.Assertions.Assert.IsTrue(__data->head->previous == null);

                if (__data->tail == null || __data->tail->As<T>().CompareTo(value) <= 0)
                    __data->tail = node;

                UnityEngine.Assertions.Assert.IsTrue(__data->tail->next == null);

                /*UnsafeRBTreeNode* temp = __data->tail->next;
                while (temp != null)
                {
                    __data->tail = temp;

                    temp = temp->next;
                }*/

                NativeRBTreeNode result;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                result.m_Safety = m_Safety;
#endif

                result._instance = node;
                result._version = result._instance == null ? 0 : result._instance->target.version;
                return (NativeRBTreeNode<T>)result;
            }
            else
                factoryObject.Dispose();

            return default;
        }

        public unsafe bool Remove(in NativeRBTreeNode node)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            if (!node.isVail)
                return false;

            UnsafeRBTreeNode* head = __data->head;
            if (head == node._instance)
                head = node._instance->next;

            UnsafeRBTreeNode* tail = __data->tail;
            if (tail == node._instance)
                tail = node._instance->previous;

            if (__data->info.Remove(node._instance))
            {
                __data->head = head == null ? __data->info.root : head;

                UnityEngine.Assertions.Assert.IsTrue(__data->head == null || __data->head->previous == null);

                /*UnsafeRBTreeNode* temp;
                if (__data->head != null)
                {
                    temp = __data->head->previous;
                    while (temp != null)
                    {
                        __data->head = temp;

                        temp = temp->previous;
                    }
                }*/

                __data->tail = tail == null ? __data->info.root : tail;

                UnityEngine.Assertions.Assert.IsTrue(__data->tail == null || __data->tail->next == null);

                /*if (__data->tail != null)
                {
                    temp = __data->tail->next;
                    while (temp != null)
                    {
                        __data->tail = temp;

                        temp = temp->next;
                    }
                }*/

                --__data->count;

                node._instance->target.Dispose();

                return true;
            }

            return false;
        }

        public unsafe bool RemoveAt(in T value)
        {
            NativeRBTreeNode node = Get(value);

            if (node._instance == null)
                return false;

            return Remove(node);
        }

        public unsafe void Clear()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif

            UnsafeRBTreeNode* node = __data->info.root;
            while (node != null && __data->info.Remove(node))
            {
                //__pool.Free(node);
                node->target.Dispose();

                node = __data->info.root;
            }

            __data->count = 0;
        }

        public unsafe void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckDeallocateAndThrow(m_Safety);

            AtomicSafetyHandle.Release(m_Safety);
#endif

            /*UnsafeRBTreeNode* node = __data->info.root;
            while (node != null && __data->info.Remove(node))
            {
                __pool.Free(node);

                node = __data->info.root;
            }*/

            AllocatorManager.Free(__factory.allocator, __data);

            __data = null;

            __factory.Dispose();
        }

        public NativeRBTreeEnumerator<T> GetEnumerator()
        {
            return new NativeRBTreeEnumerator<T>(head);
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    //[NativeContainer]
    public unsafe struct NativeRBTree<T> : IDisposable, IEnumerable<T>
        where T : struct, IComparable<T>
    {
        private NativeRBTreeLite<T> __instance;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [NativeSetClassTypeToNullOnSchedule]
        internal DisposeSentinel m_DisposeSentinel;
#endif

        public bool isCreated => __instance.isCreated;

        public int count => __instance.count;

        public NativeRBTreeNode<T> head => __instance.head;

        public NativeRBTreeNode<T> tail => __instance.tail;

        public NativeRBTreeNode<T> root => __instance.root;

        public static implicit operator NativeRBTree<T>(NativeRBTreeLite<T> instance)
        {
            NativeRBTree<T> result = default;
            result.__instance = instance;

            return result;
        }

        public NativeRBTree(Allocator allocator)
        {
            __instance = new NativeRBTreeLite<T>(allocator);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Create(out var safety, out m_DisposeSentinel, 0, allocator);

            __instance = new NativeRBTreeLite<T>(allocator, safety);
#else
            __instance = new NativeRBTreeLite<T>(allocator);
#endif
        }

        public NativeRBTreeNode<T> Get(in T value) => __instance.Get(value);

        public NativeRBTreeNode<T> Add(T value, bool isAllowDuplicate) => __instance.Add(value, isAllowDuplicate);

        public bool Remove(in NativeRBTreeNode node) => __instance.Remove(node);

        public bool RemoveAt(in T value) => __instance.RemoveAt(value);

        public void Clear() => __instance.Clear();

        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Clear(ref m_DisposeSentinel);
#endif

            __instance.Dispose();
        }

        public NativeRBTreeEnumerator<T> GetEnumerator() => __instance.GetEnumerator();

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => __instance.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => __instance.GetEnumerator();
    }

    /*[NativeContainer]
    public unsafe struct NativeRBTree<T> : IDisposable, IEnumerable<T>
        where T : struct, IComparable<T>
    {
        public struct Node : IEquatable<Node>
        {
            internal int _version;

            [NativeDisableUnsafePtrRestriction]
            internal UnsafeRBTreeNode* _instance;

    #if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
    #endif

            public bool isNull
            {
                get
                {
                    return _instance == null;
                }
            }

            public bool isVail
            {
                get
                {
                    if (_instance == null)
                        return false;

    #if ENABLE_UNITY_COLLECTIONS_CHECKS
                    AtomicSafetyHandle.CheckExistsAndThrow(m_Safety);
    #endif
                    return _instance->target.isVail && _instance->target.version == _version;
                }
            }

            public ref T value
            {
                get
                {
    #if ENABLE_UNITY_COLLECTIONS_CHECKS
                    AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);

                    if (_instance == null)
                        throw new InvalidOperationException();
    #endif

                    return ref _instance->As<T>();
                }
            }

            public Node previous
            {
                get
                {
                    Node node;
    #if ENABLE_UNITY_COLLECTIONS_CHECKS
                    if (!isVail)
                        throw new InvalidOperationException();

                    node.m_Safety = m_Safety;

                    AtomicSafetyHandle.CheckReadAndThrow(node.m_Safety);
    #endif

                    node._instance = _instance == null ? null : _instance->previous;
                    node._version = node._instance == null ? 0 : node._instance->target.version;
                    return node;
                }
            }

            public Node next
            {
                get
                {
                    Node node;
    #if ENABLE_UNITY_COLLECTIONS_CHECKS
                    if (!isVail)
                        throw new InvalidOperationException();

                    node.m_Safety = m_Safety;

                    AtomicSafetyHandle.CheckReadAndThrow(node.m_Safety);
    #endif

                    node._instance = _instance == null ? null : _instance->next;
                    node._version = node._instance == null ? 0 : node._instance->target.version;
                    return node;
                }
            }

            public Node leftChild
            {
                get
                {
                    Node node;
    #if ENABLE_UNITY_COLLECTIONS_CHECKS
                    if (!isVail)
                        throw new InvalidOperationException();

                    node.m_Safety = m_Safety;

                    AtomicSafetyHandle.CheckReadAndThrow(node.m_Safety);
    #endif

                    node._instance = _instance == null ? null : _instance->leftChild;
                    node._version = node._instance == null ? 0 : node._instance->target.version;
                    return node;
                }
            }

            public Node rightChild
            {
                get
                {
                    Node node;
    #if ENABLE_UNITY_COLLECTIONS_CHECKS
                    if (!isVail)
                        throw new InvalidOperationException();

                    node.m_Safety = m_Safety;

                    AtomicSafetyHandle.CheckReadAndThrow(node.m_Safety);
    #endif

                    node._instance = _instance == null ? null : _instance->rightChild;
                    node._version = node._instance == null ? 0 : node._instance->target.version;
                    return node;
                }
            }

            public T GetValue()
            {
    #if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);

                if (_instance == null)
                    throw new InvalidOperationException();
    #endif

                return _instance->As<T>();
            }

            public bool Equals(Node other)
            {
                return _version == other._version && _instance == other._instance;
            }
        }

        public struct Enumerator : IEnumerator<T>
        {
            private enum Status
            {
                None, 
                Left, 
                Right
            }

            private Status __status;

            [NativeDisableUnsafePtrRestriction]
            internal UnsafeRBTreeNode* _source;

            [NativeDisableUnsafePtrRestriction]
            internal UnsafeRBTreeNode* _destination;

            public T Current
            {
                get
                {
                    if (_destination == null)
                        throw new InvalidOperationException();

                    return _destination->As<T>();
                }
            }

            internal Enumerator(UnsafeRBTreeNode* node)
            {
                __status = Status.None;
                _source = node;
                _destination = null;
            }

            public Enumerator(Node node) : this(node._instance)
            {

            }

            public bool MoveNext()
            {
                switch(__status)
                {
                    case Status.None:
                        if (_source == null)
                            return false;

                        __status = Status.Left;

                        _destination = _source;

                        return true;
                    case Status.Left:
                        _destination = _destination->previous;
                        if (_destination == null)
                        {
                            __status = Status.Right;

                            _destination = _source->next;
                            if (_destination == null)
                                return false;
                        }

                        return true;
                    case Status.Right:
                        _destination = _destination == null ? null : _destination->next;
                        if (_destination == null)
                            return false;

                        return true;
                }

                return false;
            }

            public void Reset()
            {
                __status = Status.None;
            }

            void IDisposable.Dispose()
            {
            }

            object IEnumerator.Current
            {
                get
                {
                    return Current;
                }
            }
        }

    #if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;

        [NativeSetClassTypeToNullOnSchedule]
        internal DisposeSentinel m_DisposeSentinel;
    #endif

        [NativeDisableUnsafePtrRestriction]
        private NativeRBTreeData* __data;
        //private UnsafeRBTreeNodePool.Concurrent<T> __pool;
        private UnsafeFactory __factory;

        public bool isCreated
        {
            get
            {
                return __data != null;
            }
        }

        public int count
        {
            get
            {
    #if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
    #endif

                return __data->count;
            }
        }

        public Node head
        {
            get
            {
                Node node;

    #if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);

                node.m_Safety = m_Safety;
    #endif

                node._instance = __data->head;
                node._version = node._instance == null ? 0 : node._instance->target.version;
                return node;
            }
        }

        public Node tail
        {
            get
            {
                Node node;

    #if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);

                node.m_Safety = m_Safety;
    #endif

                node._instance = __data->tail;
                node._version = node._instance == null ? 0 : node._instance->target.version;
                return node;
            }
        }

        public Node root
        {
            get
            {
                Node node;
    #if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);

                node.m_Safety = m_Safety;
    #endif

                node._instance = __data->info.root;
                node._version = node._instance == null ? 0 : node._instance->target.version;
                return node;
            }
        }

        public NativeRBTree(Allocator allocator)
        {
            if (!UnsafeUtility.IsUnmanaged<T>())
                throw new ArgumentException(string.Format("{0} used in NativeRBTree<{0}> must be Unmanaged", typeof(T)));

            __data = AllocatorManager.Allocate<NativeRBTreeData>(allocator);
            UnsafeUtility.MemClear(__data, UnsafeUtility.SizeOf<NativeRBTreeData>());

            __factory = new UnsafeFactory(allocator);

    #if ENABLE_UNITY_COLLECTIONS_CHECKS
    #if UNITY_2018_3_OR_NEWER
            DisposeSentinel.Create(out m_Safety, out m_DisposeSentinel, 0, allocator);
    #else
            DisposeSentinel.Create(out m_Safety, out m_DisposeSentinel, 0);
    #endif
    #endif
        }

        public Node Get(T value)
        {
            Node node;
    #if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);

            node.m_Safety = m_Safety;
    #endif

            node._instance = __data->info.Get(value);
            node._version = node._instance == null ? 0 : node._instance->target.version;
            return node;
        }

        public Node Add(T value, bool isAllowDuplicate)
        {
    #if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
    #endif

            var factoryObject = __factory.Create<NativeRBTreeObject<T>>();
            ref var target = ref factoryObject.As<NativeRBTreeObject<T>>();
            target.value = value;
            target.node.isRed = true;
            target.node.target = factoryObject;

            UnsafeRBTreeNode* node = (UnsafeRBTreeNode*)UnsafeUtility.AddressOf(ref target.node);
            if (__data->info.Add<T>(node, isAllowDuplicate))
            {
                ++__data->count;

                if (__data->head == null || __data->head->As<T>().CompareTo(value) > 0)
                    __data->head = node;

                UnityEngine.Assertions.Assert.IsTrue(__data->head->previous == null);

                if (__data->tail == null || __data->tail->As<T>().CompareTo(value) <= 0)
                    __data->tail = node;

                UnityEngine.Assertions.Assert.IsTrue(__data->tail->next == null);

    Node result;

    #if ENABLE_UNITY_COLLECTIONS_CHECKS
                result.m_Safety = m_Safety;
    #endif

                result._instance = node;
                result._version = result._instance == null ? 0 : result._instance->target.version;
                return result;
            }
            else
                factoryObject.Dispose();

            return default;
        }

        public bool Remove(Node node)
        {
    #if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
    #endif
            if (!node.isVail)
                return false;

            UnsafeRBTreeNode* head = __data->head;
            if (head == node._instance)
                head = node._instance->next;

            UnsafeRBTreeNode* tail = __data->tail;
            if (tail == node._instance)
                tail = node._instance->previous;

            if (__data->info.Remove(node._instance))
            {
                __data->head = head == null ? __data->info.root : head;

                UnityEngine.Assertions.Assert.IsTrue(__data->head == null || __data->head->previous == null);

                __data->tail = tail == null ? __data->info.root : tail;

                UnityEngine.Assertions.Assert.IsTrue(__data->tail == null || __data->tail->next == null);

                --__data->count;

                node._instance->target.Dispose();

                return true;
            }

            return false;
        }

        public bool RemoveAt(T value)
        {
            Node node = Get(value);

            if (node._instance == null)
                return false;

            return Remove(node);
        }

        public void Clear()
        {
    #if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
    #endif

            UnsafeRBTreeNode* node = __data->info.root;
            while (node != null && __data->info.Remove(node))
            {
                //__pool.Free(node);
                node->target.Dispose();

                node = __data->info.root;
            }

            __data->count = 0;
        }

        public void Dispose()
        {
    #if ENABLE_UNITY_COLLECTIONS_CHECKS
    #if UNITY_2018_3_OR_NEWER
            DisposeSentinel.Dispose(ref m_Safety, ref m_DisposeSentinel);
    #else
            DisposeSentinel.Dispose(m_Safety, ref m_DisposeSentinel);
    #endif
    #endif

            AllocatorManager.Free(__factory.allocator, __data);

            __data = null;

            __factory.Dispose();
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(__data->head);
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }*/
}