using System;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ZG
{
    public struct SharedMultiHashMap<TKey, TValue> 
        where TKey : unmanaged, IEquatable<TKey>
        where TValue : unmanaged
    {
        [NativeContainer]
        public struct Enumerator : IEnumerator<TValue>
        {
            internal UnsafeParallelMultiHashMap<TKey, TValue>.Enumerator _enumerator;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            public TValue Current
            {
                get
                {
                    CheckRead();

                    return _enumerator.Current;
                }
            }

            public void Dispose()
            {
                _enumerator.Dispose();
            }

            public bool MoveNext()
            {
                CheckRead();

                return _enumerator.MoveNext();
            }

            public void Reset()
            {
                _enumerator.Reset();
            }

            object IEnumerator.Current => Current;

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckRead()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            }
        }

        [NativeContainer]
        public struct KeyValueEnumerator : IEnumerator<KeyValue<TKey, TValue>>
        {
            private UnsafeParallelMultiHashMap<TKey, TValue>.KeyValueEnumerator __enumerator;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            public KeyValue<TKey, TValue> Current
            {
                get
                {
                    CheckRead();

                    return __enumerator.Current;
                }
            }

            internal KeyValueEnumerator(ref SharedMultiHashMap<TKey, TValue> instance)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = instance.m_Safety;
#endif

                __enumerator = instance.__values.GetEnumerator();
            }

            public void Dispose()
            {
                __enumerator.Dispose();
            }

            public bool MoveNext()
            {
                CheckRead();

                return __enumerator.MoveNext();
            }

            public void Reset()
            {
                __enumerator.Reset();
            }

            object IEnumerator.Current => Current;

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckRead()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            }
        }

        [NativeContainer]
        public struct Reader
        {
            private UnsafeParallelMultiHashMap<TKey, TValue> __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            public Reader(ref SharedMultiHashMap<TKey, TValue> instance)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = instance.m_Safety;
#endif

                __values = instance.__values;
            }

            public int Count()
            {
                CheckRead();

                return __values.Count();
            }

            public bool ContainsKey(in TKey key)
            {
                CheckRead();

                return __values.ContainsKey(key);
            }

            public Enumerator GetValuesForKey(in TKey key)
            {
                CheckRead();

                Enumerator enumerator;
                enumerator._enumerator = __values.GetValuesForKey(key);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                enumerator.m_Safety = m_Safety;
#endif

                return enumerator;
            }

            public NativeArray<TKey> GetKeys(Allocator allocator)
            {
                CheckRead();

                return __values.GetKeyArray(allocator);
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckRead()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            }
        }

        [NativeContainer]
        public struct Writer
        {
            private UnsafeParallelMultiHashMap<TKey, TValue> __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            public bool isEmpty
            {
                get
                {
                    CheckRead();

                    return __values.IsEmpty;
                }
            }

            public int capacity
            {
                get
                {
                    CheckRead();

                    return __values.Capacity;
                }

                set
                {
                    CheckWrite();

                    __values.Capacity = value;
                }
            }

            public Writer(ref SharedMultiHashMap<TKey, TValue> instance)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = instance.m_Safety;
#endif

                __values = instance.__values;
            }

            public bool TryGetFirstValue(in TKey key, out TValue item, out NativeParallelMultiHashMapIterator<TKey> it)
            {
                CheckRead();

                return __values.TryGetFirstValue(key, out item, out it);
            }

            public bool TryGetNextValue(out TValue item, ref NativeParallelMultiHashMapIterator<TKey> it)
            {
                CheckRead();

                return __values.TryGetNextValue(out item, ref it);
            }

            public Enumerator GetValuesForKey(in TKey key)
            {
                CheckRead();

                Enumerator enumerator;
                enumerator._enumerator = __values.GetValuesForKey(key);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                enumerator.m_Safety = m_Safety;
#endif

                return enumerator;
            }

            public NativeArray<TKey> GetKeyArray(Allocator allocator)
            {
                CheckRead();

                return __values.GetKeyArray(allocator);
            }

            public int CountValuesForKey(in TKey key)
            {
                CheckRead();

                return __values.CountValuesForKey(key);
            }

            public void SetValue(in TValue value, in NativeParallelMultiHashMapIterator<TKey> iterator)
            {
                CheckWrite();

                __values.SetValue(value, iterator);
            }

            public void Add(in TKey key, in TValue value)
            {
                CheckWrite();

                __values.Add(key, value);
            }

            public int Remove(in TKey key)
            {
                CheckWrite();

                /*if (!__values.TryGetValue(handle, out var value))
                    return false;

                value.Dispose();*/

                return __values.Remove(key);
            }

            public void Remove(in NativeParallelMultiHashMapIterator<TKey> it)
            {
                CheckWrite();

                /*if (!__values.TryGetValue(handle, out var value))
                    return false;

                value.Dispose();*/

                __values.Remove(it);
            }

            public void Clear()
            {
                CheckWrite();

                __values.Clear();
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckRead()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckWrite()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            }
        }

        [NativeContainer]
        [NativeContainerIsAtomicWriteOnly]
        public struct ParallelWriter
        {
            private UnsafeParallelMultiHashMap<TKey, TValue>.ParallelWriter __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            public ParallelWriter(ref SharedMultiHashMap<TKey, TValue> instance)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = instance.m_Safety;
#endif

                __values = instance.__values.AsParallelWriter();
            }

            public void Add(in TKey key, TValue value)
            {
                CheckWrite();

                /*if (!__values.TryGetValue(handle, out var value))
                    return false;

                value.Dispose();*/

                __values.Add(key, value);
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckWrite()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            }
        }

        public readonly Allocator Allocator;

        private unsafe LookupJobManager* __lookupJobManager;

        private UnsafeParallelMultiHashMap<TKey, TValue> __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif

        public unsafe ref LookupJobManager lookupJobManager => ref *__lookupJobManager;

        public int capacity
        {
            get
            {
                CheckRead();

                return __values.Capacity;
            }

            set
            {
                CheckWrite();

                __values.Capacity = value;
            }
        }

        public Reader reader => new Reader(ref this);

        public Writer writer => new Writer(ref this);

        public ParallelWriter parallelWriter => new ParallelWriter(ref this);

        public SharedMultiHashMap(Allocator allocator)
        {
            Allocator = allocator;

            unsafe
            {
                __lookupJobManager = (LookupJobManager*)UnsafeUtility.Malloc(
                    UnsafeUtility.SizeOf<LookupJobManager>(),
                    UnsafeUtility.AlignOf<LookupJobManager>(),
                    allocator);

                *__lookupJobManager = default;
            }

            __values = new UnsafeParallelMultiHashMap<TKey, TValue>(1, allocator);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = AtomicSafetyHandle.Create();
#endif
        }

        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(m_Safety);
#endif

            unsafe
            {
                UnsafeUtility.Free(__lookupJobManager, Allocator);

                __lookupJobManager = null;
            }

            __values.Dispose();
        }

        public int Count()
        {
            CheckRead();

            return __values.Count();
        }

        public KeyValueEnumerator GetEnumerator()
        {
            CheckRead();

            return new KeyValueEnumerator(ref this);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckRead()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckWrite()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
        }
    }
}