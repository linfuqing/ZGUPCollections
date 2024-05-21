using System;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ZG
{
    internal struct UnsafePoolItem<T> where T : unmanaged
    {
        public T data;
        public int index;
    }

    public struct UnsafePool<T> where T : unmanaged
    {
        public struct Enumerator
        {
            private int __index;
            private unsafe UnsafePool<T>* __values;

            public unsafe KeyValuePair<int, T> Current => new KeyValuePair<int,T>(__index, (*__values)[__index]);

            internal unsafe Enumerator(ref UnsafePool<T> pool)
            {
                __index = -1;
                __values = (UnsafePool<T>*)UnsafeUtility.AddressOf(ref pool);
            }

            public unsafe bool MoveNext()
            {
                while (__index + 1 < __values->length)
                {
                    if (__values->ContainsKey(++__index))
                        return true;
                }

                return false;
            }
        }

        internal UnsafeList<UnsafePoolItem<T>> _items;
        private UnsafeList<int> __indices;

        public bool isCreated => _items.IsCreated && __indices.IsCreated;

        public AllocatorManager.AllocatorHandle allocator => _items.Allocator;

        /// <summary>
        /// Gets the number of elements actually contained in the <see cref="Pool{T}"/>.
        /// </summary>
        /// <returns>
        /// The number of elements actually contained in the  <see cref="Pool{T}"/>.
        /// </returns>
        public int count
        {
            get
            {
                return _items.Length - __indices.Length;
            }
        }

        /// <summary>
        /// Gets or sets the total number of elements the internal data structure can hold without resizing.
        /// </summary>
        /// <value>
        /// The number of elements that the <see cref="Pool{T}"/> can contain before resizing is required.
        /// </value>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Capacity is set to a value that is less than <see cref="count"/>. 
        /// </exception>
        public int length
        {
            get
            {
                return _items.Length;
            }

            set
            {
                int length = _items.Length;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (value < length)
                    throw new ArgumentOutOfRangeException();
#endif

                capacity = math.max(capacity, value);

                UnsafePoolItem<T> item;
                item.data = default(T);
                for (int i = length; i < value; ++i)
                {
                    item.index = __indices.Length;

                    __indices.Add(_items.Length);

                    _items.Add(item);
                }
            }
        }

        public int capacity
        {
            get
            {
                return _items.Capacity;
            }

            set
            {
                __CheckCapacityInRange(value, _items.Length);

                int capacity = value - _items.Capacity;
                if (capacity > __indices.Capacity)
                    __indices.SetCapacity(capacity);

                _items.SetCapacity(value);
            }
        }

        /// <summary>
        /// Get next index of the element be added.
        /// </summary>
        /// <returns>
        /// The index of elements in the <see cref="Pool{T}"/>.
        /// </returns>
        public int nextIndex
        {
            get
            {
                int numItems = _items.Length;
                if (numItems <= 0)
                    return numItems;

                int numIndices = __indices.Length;
                if (numIndices > 0)
                {
                    int index = __indices[numIndices - 1];
                    if (index >= 0 && index < _items.Length)
                        return index;
                }

                return numItems;
            }
        }

        public T this[int index]
        {
            get
            {
                return ElementAt(index);
            }

            set
            {
                ElementAt(index) = value;
            }
        }

        public UnsafePool(int capacity, AllocatorManager.AllocatorHandle allocator)
        {
            __CheckInit(capacity);

            _items = new UnsafeList<UnsafePoolItem<T>>(capacity, allocator);

            __indices = new UnsafeList<int>(capacity, allocator, NativeArrayOptions.UninitializedMemory);
        }

        internal UnsafePool(SerializationInfo info, StreamingContext context)
        {
            this = new UnsafePool<T>(info.MemberCount, Allocator.Persistent);

            SerializationInfoEnumerator enumerator = info == null ? null : info.GetEnumerator();
            if (enumerator != null)
            {
                while (enumerator.MoveNext())
                {
                    __Insert(int.Parse(enumerator.Name), (T)enumerator.Value);
                }
            }
        }

        public void Dispose()
        {
            _items.Dispose();
            __indices.Dispose();
        }

        /// <summary>
        /// Removes all elements from the <see cref="Pool{T}"/>.
        /// </summary>
        public void Clear()
        {
            _items.Clear();
            __indices.Clear();
        }

        public ref T ElementAt(int index)
        {
            ref var item = ref _ElementAt(index);

            __CheckIndexInRange(item);

            return ref item.data;
        }

        /// <summary>
        /// Determines whether the <see cref="Pool{T}"/> contains an element with the index.
        /// </summary>
        /// <param name="index">
        /// The index to locate in the <see cref="Pool{T}"/>.
        /// </param>
        /// <returns>
        /// <code>true</code> if the <see cref="Pool{T}"/> contains an element with the index; otherwise, <code>false</code>.
        /// </returns>
        public unsafe bool ContainsKey(int index)
        {
            if (index < 0 || index >= _items.Length)
                return false;

            ref var item = ref _ElementAt(index);
            if (item.index != -1)
                return false;

            return true;
        }

        /// <summary>
        /// Gets the value associated with the index.
        /// </summary>
        /// <param name="index">
        /// The index of the value to get.
        /// </param>
        /// <param name="data">
        /// The value associated with the index, if the index is found; 
        /// the old value with the index, if the index has been used; 
        /// otherwise, the default value for the type of the value parameter. 
        /// </param>
        /// <returns>
        /// <code>true</code> if the <see cref="Pool{T}"/> contains an element with the index; otherwise, <code>false</code>.
        /// </returns>
        public bool TryGetValue(int index, out T data)
        {
            if (index < 0 || index >= _items.Length)
            {
                data = default(T);

                return false;
            }

            ref var item = ref _ElementAt(index);
            data = item.data;

            return item.index == -1;
        }

        public bool TrySetValue(int index, in T data)
        {
            if (index < 0 || index >= _items.Length)
                return false;

            ref var item = ref _ElementAt(index);
            if (item.index != -1)
                return false;

            item.data = data;

            return true;
        }

        /// <summary>
        /// Inserts an element into the <see cref="Pool{T}"/> at the specified index.
        /// </summary>
        /// <param name="index">
        /// The zero-based index at which item should be inserted.
        /// If the index is greater than <see cref="capacity"/>, the <see cref="capacity"/> auto to be set as <code>index + 1</code>.
        /// </param>
        /// <param name="data">
        /// The object to insert. The value can be <code>null</code> for reference types.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="index"/> is less than <code>0</code>.
        /// </exception>
        public void Insert(int index, in T data)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (index < 0)
                throw new IndexOutOfRangeException();
#endif

            __Insert(index, data);
        }

        /// <summary>
        /// Adds an element with the value into the <see cref="Pool{T}"/>.
        /// </summary>
        /// <param name="data">
        /// The value of the element to be added. The value can be <code>null</code> for reference types.
        /// 
        /// The value will be set to the index of the last element to be removed in the <see cref="Pool{T}"/>, if there is not a continous index of element in <see cref="Pool{T}"/>;
        /// otherwise, the value will be added to end of <see cref="Pool{T}"/>.
        /// </param>
        /// <returns>
        /// The index of element which after be added to the <see cref="Pool{T}"/>.
        /// </returns>
        public int Add(in T data)
        {
            UnsafePoolItem<T> item;
            item.data = data;
            item.index = -1;

            int numIndices = __indices.Length;
            if (numIndices > 0)
            {
                int index = __indices[--numIndices];
                __indices.RemoveAtSwapBack(numIndices);
                if (index >= 0 && index < _items.Length)
                {
                    _SetElementAt(index, item);

                    return index;
                }
            }

            _items.Add(item);

            return _items.Length - 1;
        }

        /// <summary>
        /// Removes an element at the index.
        /// </summary>
        /// <param name="index">
        /// The index to locate in the <see cref="Pool{T}"/>.
        /// </param>
        /// <returns>
        /// <code>true</code> if the <see cref="Pool{T}"/> contains an element with the index; otherwise, <code>false</code>.
        /// </returns>
        public bool RemoveAt(int index)
        {
            ref var item = ref _ElementAt(index);
            int numIndices = __indices.Length;
            if (item.index >= 0 && item.index < numIndices)
                return false;

            __indices.Add(index);

            item.index = numIndices;
            //_SetElementAt(index, item);

            return true;
        }

        public Enumerator GetEnumerator() => new Enumerator(ref this);

        /// <summary>
        /// Copies the entire <see cref="Pool{T}"/> to a compatible one-dimensional array, starting at the specified index of the target array.
        /// </summary>
        /// <param name="array">
        /// The one-dimensional Array that is the destination of the elements copied from <see cref="Pool{T}"/>. The Array must have zero-based indexing.
        /// </param>
        /// <param name="arrayIndex">
        /// The zero-based index in array at which copying begins.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="array"/> must not be <code>null</code> or empty.
        /// </exception>
        public void CopyTo(T[] array, int arrayIndex)
        {
            int numItems = array == null ? 0 : array.Length;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (arrayIndex < 0 || arrayIndex >= numItems)
                throw new ArgumentNullException();
#endif

            int length = _items.Length;
            for (int i = 0; i < length; ++i)
            {
                ref var item = ref _ElementAt(i);

                if (item.index == -1)
                    array[arrayIndex++] = item.data;

                if (arrayIndex >= numItems)
                    break;
            }
        }

        public void CopyTo(KeyValuePair<int, T>[] array, int arrayIndex)
        {
            int numItems = array == null ? 0 : array.Length;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (arrayIndex < 0 || arrayIndex >= numItems)
                throw new ArgumentNullException();
#endif

            int length = _items.Length;
            for (int i = 0; i < length; ++i)
            {
                ref var item = ref _ElementAt(i);

                if (item.index == -1)
                    array[arrayIndex++] = new KeyValuePair<int, T>(i, item.data);

                if (arrayIndex >= numItems)
                    break;
            }
        }

        /// <summary>
        /// Copies the elements of the <see cref="Pool{T}"/> to a new array.
        /// </summary>
        /// <returns>
        /// An array containing copies of the elements of the <see cref="Pool{T}"/>.
        /// </returns>
        public T[] ToArray()
        {
            int count = this.count;
            if (count <= 0)
                return null;

            T[] array = new T[count];
            CopyTo(array, 0);

            return array;
        }

        internal unsafe ref UnsafePoolItem<T> _ElementAt(int index)
        {
            return ref _items.ElementAt(index);
        }

        internal unsafe void _SetElementAt(int index, in UnsafePoolItem<T> item)
        {
            _items[index] = item;
        }

        private void __Insert(int index, T data)
        {
            if (index >= length)
                length = index + 1;

            ref var item = ref _ElementAt(index);
            item.data = data;
            if (item.index >= 0)
            {
                int numIndices = __indices.Length;
                if (item.index < numIndices)
                {
                    int currentIndex = __indices[--numIndices];
                    __indices.RemoveAtSwapBack(numIndices);
                    if (currentIndex >= 0 && currentIndex < _items.Length)
                    {
                        ref var currentItem = ref _ElementAt(currentIndex);

                        currentItem.index = item.index;

                        //__items[currentIndex] = currentItem;
                    }

                    if (numIndices > item.index)
                        __indices[item.index] = currentIndex;
                }

                item.index = -1;
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void __CheckInit(int capacity)
        {
            if (capacity < 0)
                throw new ArgumentOutOfRangeException("length", "Length must be >= 0");

            /*if (!UnsafeUtility.IsBlittable<T>())
                throw new ArgumentException(string.Format("{0} used in NativeCustomArray<{0}> must be blittable", typeof(T)));*/
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void __CheckIndexInRange(in UnsafePoolItem<T> item)
        {
            if (item.index != -1)
                throw new IndexOutOfRangeException();
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void __CheckCapacityInRange(int value, int length)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException($"Value {value} must be positive.");

            if ((uint)value < (uint)length)
                throw new ArgumentOutOfRangeException($"Value {value} is out of range in Pool of '{length}' Length.");
        }
    }

    /// <summary>
    /// A list that can be added/deleted faster by recycling index.
    /// </summary>
    /// <typeparam name="T">
    /// The type of elements in the pool.
    /// </typeparam>
    [NativeContainer]
    public struct NativePool<T> : IPool<T>, ISerializable, IDisposable where T : unmanaged
    {
        internal struct DataItem// : IEquatable<DataItem>
        {
            public T data;
            public int index;

            /*public bool Equals(DataItem other)
            {
                return index == other.index && data.Equals(other.data);
            }*/
        }

        public interface ISlice
        {
            int length { get; }

            T this[int index] { get; }

            bool ContainsKey(int index);

            bool TryGetValue(int index, out T data);
        }

        [NativeContainer]
        public struct ReadOnlySlice : ISlice, IEnumerable<T>
        {
            [NativeDisableUnsafePtrRestriction]
            internal unsafe UnsafePool<T>* __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            public unsafe int length
            {
                get
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                    return __values->length;
                }
            }


            public static unsafe implicit operator ReadOnlySlice(NativePool<T> pool)
            {
                ReadOnlySlice slice;
                slice.__values = pool._values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                slice.m_Safety = pool.m_Safety;
#endif

                return slice;
            }

            public unsafe T this[int index]
            {
                get
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                    return (*__values)[index];
                }
            }
            
            public unsafe bool ContainsKey(int index)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                return __values->ContainsKey(index);
            }

            public unsafe bool TryGetValue(int index, out T data)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                return __values->TryGetValue(index, out data);
            }

            public SliceEnumerator<ReadOnlySlice> GetEnumerator()
            {
                return new SliceEnumerator<ReadOnlySlice>(ref this);
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

        [NativeContainer]
        public struct Slice : ISlice, IEnumerable<T>
        {
            [NativeDisableUnsafePtrRestriction]
            internal unsafe UnsafePool<T>* __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            public unsafe int length
            {
                get
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                    return __values->length;
                }
            }


            public static unsafe implicit operator Slice(NativePool<T> pool)
            {
                Slice slice;
                slice.__values = pool._values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                slice.m_Safety = pool.m_Safety;

                //AtomicSafetyHandle.CheckGetSecondaryDataPointerAndThrow(slice.m_Safety);
                //AtomicSafetyHandle.UseSecondaryVersion(ref slice.m_Safety);
#endif

                return slice;
            }

            public unsafe T this[int index]
            {
                get
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                    return (*__values)[index];
                }

                [WriteAccessRequired]
                set
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif

                    (*__values)[index] = value;
                }
            }

            public unsafe bool ContainsKey(int index)
            {
                return __values->ContainsKey(index);
            }

            public unsafe bool TryGetValue(int index, out T data)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                return __values->TryGetValue(index, out data);
            }

            public unsafe bool TrySetValue(int index, in T data)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
                return __values->TrySetValue(index, data);
            }

            public SliceEnumerator<Slice> GetEnumerator()
            {
                return new SliceEnumerator<Slice>(ref this);
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

        public struct SliceEnumerator<U> : IEnumerator<T> where U : ISlice
        {
            private U __slice;

            private int __index;

            public T Current => __slice[__index];

            public SliceEnumerator(ref U slice)
            {
                __slice = slice;
                __index = -1;
            }

            public bool MoveNext()
            {
                while (__index++ < __slice.length)
                {
                    if (__slice.ContainsKey(__index))
                        return true;
                }

                return false;
            }

            public void Reset()
            {
                __index = -1;
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

        public struct Enumerator : IEnumerator<T>, IEnumerator<KeyValuePair<int, T>>
        {
            private int __index;
            private T __data;
            private NativePool<T> __pool;

            public int index
            {
                get
                {
                    return __index;
                }
            }

            public T value
            {
                get
                {
                    return __data;
                }
            }

            public KeyValuePair<int, T> Current
            {
                get
                {
                    return new KeyValuePair<int, T>(index, value);
                }
            }

            internal Enumerator(ref NativePool<T> pool)
            {
                __index = -1;
                __data = default;
                __pool = pool;
            }

            public bool MoveNext()
            {
                int length = __pool.length;
                for (int i = __index + 1; i < length; ++i)
                {
                    if (__pool.TryGetValue(i, out __data))
                    {
                        __index = i;

                        return true;
                    }
                }

                return false;
            }

            public void Reset()
            {
                __index = -1;
            }

            void IDisposable.Dispose()
            {

            }

            T IEnumerator<T>.Current => value;

            object IEnumerator.Current => value;
        }

        [NativeDisableUnsafePtrRestriction]
        internal unsafe UnsafePool<T>* _values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;

        //internal int m_SafetyIndexHint;
        internal static readonly SharedStatic<int> s_staticSafetyId = SharedStatic<int>.GetOrCreate<NativeList<T>>();
#endif

        public unsafe bool isCreated
        {
            get
            {
                return _values != null && _values->isCreated;
            }
        }
        
        /// <summary>
        /// Gets a value indicating whether the <see cref="Pool{T}"/> is read-only.
        /// </summary>
        /// <return>
        /// Always <code>false</code>.
        /// </return>
        public bool IsReadOnly
        {
            get
            {
                return false;
            }
        }

        public unsafe AllocatorManager.AllocatorHandle allocator => _values->allocator;

        /// <summary>
        /// Gets the number of elements actually contained in the <see cref="Pool{T}"/>.
        /// </summary>
        /// <returns>
        /// The number of elements actually contained in the <see cref="Pool{T}"/>.
        /// </returns>
        public int Count
        {
            get
            {
                return count;
            }
        }

        /// <summary>
        /// Gets the number of elements actually contained in the <see cref="Pool{T}"/>.
        /// </summary>
        /// <returns>
        /// The number of elements actually contained in the  <see cref="Pool{T}"/>.
        /// </returns>
        public unsafe int count
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                return _values->count;
            }
        }

        /// <summary>
        /// Gets or sets the total number of elements the internal data structure can hold without resizing.
        /// </summary>
        /// <value>
        /// The number of elements that the <see cref="Pool{T}"/> can contain before resizing is required.
        /// </value>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Capacity is set to a value that is less than <see cref="count"/>. 
        /// </exception>
        public unsafe int length
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                return _values->length;
            }

            set
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
                _values->length = value;
            }
        }

        public unsafe int capacity
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                return _values->capacity;
            }

            set
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif

                _values->capacity = value;
            }
        }

        /// <summary>
        /// Get next index of the element be added.
        /// </summary>
        /// <returns>
        /// The index of elements in the <see cref="Pool{T}"/>.
        /// </returns>
        public unsafe int nextIndex
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                return _values->nextIndex;
            }
        }
        
        /// <summary>
        /// Gets or sets the element at the specified index.
        /// </summary>
        /// <param name="index">
        /// The zero-based index of the element to get or set.
        /// </param>
        /// <returns>
        /// The element at the specified index.
        /// </returns>
        public unsafe T this[int index]
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                return (*_values)[index];
            }

            [WriteAccessRequired]
            set
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif

                (*_values)[index] = value;
            }
        }

        public NativePool(AllocatorManager.AllocatorHandle allocator) : this(1, allocator)
        {
        }

        private unsafe NativePool(int capacity, AllocatorManager.AllocatorHandle allocator)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = CollectionHelper.CreateSafetyHandle(allocator.Handle);

            if (UnsafeUtility.IsNativeContainerType<T>())
                AtomicSafetyHandle.SetNestedContainer(m_Safety, true);

            CollectionHelper.SetStaticSafetyId<NativePool<T>>(ref m_Safety, ref s_staticSafetyId.Data);

            //m_SafetyIndexHint = (allocator.Handle).AddSafetyHandle(m_Safety);

            AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, true);
#endif

            _values = AllocatorManager.Allocate<UnsafePool<T>>(allocator);

            *_values = new UnsafePool<T>(capacity, allocator);
        }

        private unsafe NativePool(SerializationInfo info, StreamingContext context)
        {
            AllocatorManager.AllocatorHandle allocator = AllocatorManager.Persistent;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = CollectionHelper.CreateSafetyHandle(allocator.Handle);

            if (UnsafeUtility.IsNativeContainerType<T>())
                AtomicSafetyHandle.SetNestedContainer(m_Safety, true);

            CollectionHelper.SetStaticSafetyId<NativePool<T>>(ref m_Safety, ref s_staticSafetyId.Data);

            //m_SafetyIndexHint = (allocator.Handle).AddSafetyHandle(m_Safety);

            AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, true);
#endif

            _values = AllocatorManager.Allocate<UnsafePool<T>>(allocator);

            *_values = new UnsafePool<T>(info, context);
        }

        public unsafe void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            CollectionHelper.DisposeSafetyHandle(ref m_Safety);
#endif

            var allocator = _values->allocator;

            _values->Dispose();

            AllocatorManager.Free(allocator, _values);

            _values = null;
        }

        public unsafe ref T ElementAt(int index)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif

            return ref _values->ElementAt(index);
        }

        /// <summary>
        /// Determines whether the <see cref="Pool{T}"/> contains an element with the index.
        /// </summary>
        /// <param name="index">
        /// The index to locate in the <see cref="Pool{T}"/>.
        /// </param>
        /// <returns>
        /// <code>true</code> if the <see cref="Pool{T}"/> contains an element with the index; otherwise, <code>false</code>.
        /// </returns>
        public unsafe bool ContainsKey(int index)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

            return _values->ContainsKey(index);
        }

        /*/// <summary>
        /// Determines whether the <see cref="Pool{T}"/> contains an element.
        /// </summary>
        /// <param name="data">
        /// The element to locate in the <see cref="Pool{T}"/>.
        /// </param>
        /// <returns>
        /// <code>true</code> if the <see cref="Pool{T}"/> contains an element; otherwise, <code>false</code>.
        /// </returns>
        public bool ContainsValue(T data)
        {
            return IndexOf(data) != -1;
        }*/

        /*/// <summary>
        /// Searches for the specified object and returns the zero-based index of the first occurrence within the entire <see cref="Pool{T}"/>.
        /// </summary>
        /// <param name="data">
        /// The object to locate in the <see cref="Pool{T}"/>. The value can be <code>null</code> for reference types.
        /// </param>
        /// <returns>
        /// The zero-based index of the first occurrence of item within the entire <see cref="Pool{T}"/>, if found; otherwise, <code>-1</code>.
        /// </returns>
        public int IndexOf(T data)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

            DataItem item;
            int length = __items.Length;
            for(int i = 0; i < length; ++i)
            {
                item = __items[i];

                if (item.index == -1 && item.data.Equals(data))
                    return i;
            }

            return -1;
        }*/
        
        /// <summary>
        /// Gets the value associated with the index.
        /// </summary>
        /// <param name="index">
        /// The index of the value to get.
        /// </param>
        /// <param name="data">
        /// The value associated with the index, if the index is found; 
        /// the old value with the index, if the index has been used; 
        /// otherwise, the default value for the type of the value parameter. 
        /// </param>
        /// <returns>
        /// <code>true</code> if the <see cref="Pool{T}"/> contains an element with the index; otherwise, <code>false</code>.
        /// </returns>
        public unsafe bool TryGetValue(int index, out T data)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

            return _values->TryGetValue(index, out data);
        }

        /// <summary>
        /// Inserts an element into the <see cref="Pool{T}"/> at the specified index.
        /// </summary>
        /// <param name="index">
        /// The zero-based index at which item should be inserted.
        /// If the index is greater than <see cref="capacity"/>, the <see cref="capacity"/> auto to be set as <code>index + 1</code>.
        /// </param>
        /// <param name="data">
        /// The object to insert. The value can be <code>null</code> for reference types.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="index"/> is less than <code>0</code>.
        /// </exception>
        [WriteAccessRequired]
        public unsafe void Insert(int index, in T data)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif

            _values->Insert(index, data);
        }

        /// <summary>
        /// Adds an element with the value into the <see cref="Pool{T}"/>.
        /// </summary>
        /// <param name="data">
        /// The value of the element to be added. The value can be <code>null</code> for reference types.
        /// 
        /// The value will be set to the index of the last element to be removed in the <see cref="Pool{T}"/>, if there is not a continous index of element in <see cref="Pool{T}"/>;
        /// otherwise, the value will be added to end of <see cref="Pool{T}"/>.
        /// </param>
        /// <returns>
        /// The index of element which after be added to the <see cref="Pool{T}"/>.
        /// </returns>
        public unsafe int Add(in T data)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif

            return _values->Add(data);
        }
        
        /// <summary>
        /// Removes an element at the index.
        /// </summary>
        /// <param name="index">
        /// The index to locate in the <see cref="Pool{T}"/>.
        /// </param>
        /// <returns>
        /// <code>true</code> if the <see cref="Pool{T}"/> contains an element with the index; otherwise, <code>false</code>.
        /// </returns>
        public unsafe bool RemoveAt(int index)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif

            return _values->RemoveAt(index);
        }

        /// <summary>
        /// Removes the first occurrence of a specific object from the <see cref="Pool{T}"/>.
        /// </summary>
        /// <param name="data">
        /// The object to remove from the <see cref="Pool{T}"/>. The value can be <code>null</code> for reference types.
        /// </param>
        /// <returns>
        /// <code>true</code> if item is successfully removed; otherwise, <code>false</code>. 
        /// This method also returns <code>false</code> if item was not found in the <see cref="Pool{T}"/>.
        /// </returns>
        /*public bool Remove(T data)
        {
            return RemoveAt(IndexOf(data));
        }*/
        
        /// <summary>
        /// Copies the entire <see cref="Pool{T}"/> to a compatible one-dimensional array, starting at the specified index of the target array.
        /// </summary>
        /// <param name="array">
        /// The one-dimensional Array that is the destination of the elements copied from <see cref="Pool{T}"/>. The Array must have zero-based indexing.
        /// </param>
        /// <param name="arrayIndex">
        /// The zero-based index in array at which copying begins.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="array"/> must not be <code>null</code> or empty.
        /// </exception>
        public unsafe void CopyTo(T[] array, int arrayIndex)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

            _values->CopyTo(array, arrayIndex);
        }

        public unsafe void CopyTo(KeyValuePair<int, T>[] array, int arrayIndex)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

            _values->CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// Copies the elements of the <see cref="Pool{T}"/> to a new array.
        /// </summary>
        /// <returns>
        /// An array containing copies of the elements of the <see cref="Pool{T}"/>.
        /// </returns>
        public T[] ToArray()
        {
            int count = this.count;
            if (count <= 0)
                return null;

            T[] array = new T[count];
            CopyTo(array, 0);

            return array;
        }
        
        /// <summary>
        /// Removes all elements from the <see cref="Pool{T}"/>.
        /// </summary>
        public unsafe void Clear()
        {

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif

            _values->Clear();
        }

        /*public unsafe void Write(BinaryWriter writer, Predicate<int> predicate)
        {
            if (writer == null)
                return;
            
            int capacity = __items.IsCreated ? __items.Length : 0, size = UnsafeUtility.SizeOf<T>();
            byte[] bytes = new byte[size / UnsafeUtility.SizeOf<byte>()];
            void* items = capacity > 0 ? NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(__items.AsArray()) : null;
            for(int i = 0; i < capacity; ++i)
            {
                DataItem item = UnsafeUtility.ReadArrayElement<DataItem>(items, i);
                if (item.index != -1)
                    continue;

                if (predicate != null && !predicate(i))
                    continue;

                writer.Write(i);

                fixed (byte* result = bytes)
                {
                    UnsafeUtility.WriteArrayElement(result, 0, item.data);
                }

                writer.Write(bytes);
            }

            writer.Write(-1);
        }
        
        public unsafe void Read(BinaryReader reader)
        {
            if (reader == null)
                return;
            
            int index, size = UnsafeUtility.SizeOf<T>() / UnsafeUtility.SizeOf<byte>();
            byte[] bytes = new byte[size];
            while(true)
            {
                index = reader.ReadInt32();
                if (index < 0)
                    break;

                reader.Read(bytes, 0, size);

                fixed (byte* result = bytes)
                {
                    Insert(index, UnsafeUtility.ReadArrayElement<T>(result, 0));
                }
            }
        }

        public unsafe byte[] Serialize()
        {
            int byteSize = UnsafeUtility.SizeOf<byte>(), 
                itemsSize = __items.Length * UnsafeUtility.SizeOf<DataItem>(), 
                indicesSize = __indices.Length * UnsafeUtility.SizeOf<int>(), 
                size = itemsSize + indicesSize;
            if (size < 1)
                return null;

            byte[] items = BitConverter.GetBytes(itemsSize),
                indices = BitConverter.GetBytes(indicesSize);

            int numItemsBytes = items == null ? 0 : items.Length, numIndicesBytes = indices == null ? 0 : indices.Length, index = 0, i;
            byte[] bytes = new byte[numItemsBytes + numIndicesBytes + size / byteSize];

            for (i = 0; i < numIndicesBytes; ++i)
                bytes[index++] = items[i];

            for (i = 0; i < numIndicesBytes; ++i)
                bytes[index++] = indices[i];

            fixed (byte* result = bytes)
            {
                UnsafeUtility.MemCpy(result + index, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(__items.AsArray()), itemsSize);
                UnsafeUtility.MemCpy(result + (index + itemsSize / byteSize), NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(__indices.AsArray()), indicesSize);
            }

            return bytes;
        }

        public unsafe void Deserialize(byte[] bytes, Allocator allocator)
        {
            if (isCreated)
                Dispose();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            // Native allocation is only valid for Temp, Job and Persistent
            if (allocator <= Allocator.None)
                throw new ArgumentException("Allocator must be Temp, TempJob or Persistent", "allocator");

            if (!UnsafeUtility.IsBlittable<T>())
                throw new ArgumentException(string.Format("{0} used in NativeCustomArray<{0}> must be blittable", typeof(T)));
#endif
            int index = sizeof(int) / sizeof(byte), 
                itemsSize = BitConverter.ToInt32(bytes, 0), 
                indicesSize = BitConverter.ToInt32(bytes, index), 
                itemsLength = itemsSize / UnsafeUtility.SizeOf<DataItem>(), 
                indicesLength = indicesSize / UnsafeUtility.SizeOf<int>();
            NativeArray<DataItem> items;
            NativeArray<int> indices;

            index <<= 1;
            fixed (byte* result = bytes)
            {
                items = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<DataItem>(result + index, itemsLength, Allocator.Invalid);
                
                indices = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int>(result + (index + itemsSize / UnsafeUtility.SizeOf<byte>()), indicesLength, Allocator.Invalid);
            }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref items, AtomicSafetyHandle.GetTempUnsafePtrSliceHandle());
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref indices, AtomicSafetyHandle.GetTempUnsafePtrSliceHandle());
            
            m_Safety = AtomicSafetyHandle.Create();
#endif

            __items = new NativeList<DataItem>(itemsLength, allocator);
            __indices = new NativeList<int>(indicesLength, allocator);

            __items.AddRange(items);
            __indices.AddRange(indices);
        }*/

        public unsafe void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
                return;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            T item;
            int length = this.length;
            for (int i = 0; i < length; ++i)
            {
                if (_values->TryGetValue(i, out item))
                    info.AddValue(i.ToString(), item);
            }
        }

        public unsafe JobHandle ScheduleParallelForDefer<TJob>(
            ref TJob job, 
            int innerloopBatchCount, 
            in JobHandle inputDeps) where TJob : struct, IJobParallelForDefer
        {
            return job.ScheduleByRef(
                ref _values->_items,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety,
#endif
                innerloopBatchCount, 
                inputDeps);
        }
        
        /*public ByteEnumerator GetByteEnumerator()
        {
            return Enumerator.Create<ByteEnumerator>(this);
        }*/

        public Enumerator GetEnumerator()
        {
            return new Enumerator(ref this);
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator<KeyValuePair<int, T>> IEnumerable<KeyValuePair<int, T>>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        bool ICollection<T>.Contains(T item)
        {
            throw new InvalidOperationException();
            //return ContainsValue(item);
        }

        void ICollection<T>.Add(T item)
        {
            Add(item);
        }

        bool ICollection<T>.Remove(T item)
        {
            throw new InvalidOperationException();
        }

        void IList<T>.RemoveAt(int index)
        {
            RemoveAt(index);
        }

        void IList<T>.Insert(int index, T data)
        {
            Insert(index, data);
        }

        int IList<T>.IndexOf(T item)
        {
            throw new InvalidOperationException();
        }

    }

    public unsafe struct NativePoolLite<T> : IDisposable where T : unmanaged
    {
        [NativeDisableUnsafePtrRestriction]
        private UnsafePool<T>* __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif

        public NativePoolLite(int capacity, Allocator allocator)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = AtomicSafetyHandle.Create();
#endif

            __values = AllocatorManager.Allocate<UnsafePool<T>>(allocator);

            *__values = new UnsafePool<T>(capacity, allocator);
        }

        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(m_Safety);
#endif

            var allocator = __values->allocator;

            __values->Dispose();

            AllocatorManager.Free(allocator, __values);

            __values = null;
        }

        public static implicit operator NativePool<T>(NativePoolLite<T> value)
        {
            NativePool<T> result = default;
            result._values = value.__values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            result.m_Safety = value.m_Safety;
#endif

            return result;
        }
    }

    public static class NativePoolUtility
    {
        public static unsafe int IndexOf<T>(this NativePool<T> instance, in T destination) where T : unmanaged, IEquatable<T>
        {

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(instance.m_Safety);
#endif

            T source;
            int length = instance.length;
            for (int i = 0; i < length; ++i)
            {
                if (instance._values->TryGetValue(i, out source) && source.Equals(destination))
                    return i;
            }

            return -1;
        }

        public static bool ContainsValue<T>(this NativePool<T> instance, in T data) where T : unmanaged, IEquatable<T>
        {
            return IndexOf(instance, data) != -1;
        }

        public static bool Remove<T>(this NativePool<T> instance, in T data) where T : unmanaged, IEquatable<T>
        {
            return instance.RemoveAt(instance.IndexOf(data));
        }
    }
}