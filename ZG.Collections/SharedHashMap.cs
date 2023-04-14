using System;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ZG
{
    public struct SharedHashMap<TKey, TValue> 
        where TKey : unmanaged, IEquatable<TKey>
        where TValue : unmanaged
    {
        [NativeContainer]
        public struct Enumerator : IEnumerator<KeyValue<TKey, TValue>>
        {
            internal UnsafeParallelHashMap<TKey, TValue>.Enumerator __enumerator;

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

            internal Enumerator(in UnsafeParallelHashMap<TKey, TValue> values
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                , in AtomicSafetyHandle safety
#endif
                )
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckGetSecondaryDataPointerAndThrow(safety);
                m_Safety = safety;
                AtomicSafetyHandle.UseSecondaryVersion(ref m_Safety);
                AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, true);
#endif

                __enumerator = values.GetEnumerator();
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
        [NativeContainerIsReadOnly]
        public struct Reader
        {
            private UnsafeParallelHashMap<TKey, TValue> __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            public bool isCreated => __values.IsCreated;

            public TValue this[in TKey key]
            {
                get
                {
                    __CheckRead();

                    __CheckKey(key);

                    return __values[key];
                }
            }

            public Reader(ref SharedHashMap<TKey, TValue> instance)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = instance.m_Safety;
#endif

                __values = instance.__values;
            }

            public bool ContainsKey(in TKey key)
            {
                __CheckRead();

                return __values.ContainsKey(key);
            }

            public bool TryGetValue(in TKey key, out TValue value)
            {
                __CheckRead();

                return __values.TryGetValue(key, out value);
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private void __CheckKey(in TKey key)
            {
                if (!__values.ContainsKey(key))
                    throw new IndexOutOfRangeException();
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void __CheckRead()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            }
        }

        [NativeContainer]
        public struct Writer
        {
            private UnsafeParallelHashMap<TKey, TValue> __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
            public AtomicSafetyHandle GetAtomicSafetyHandle() => m_Safety;
#endif

            public bool isEmpty
            {
                get
                {
                    __CheckRead();

                    return __values.IsEmpty;
                }
            }

            public int capacity
            {
                get
                {
                    __CheckRead();

                    return __values.Capacity;
                }

                set
                {
                    __CheckWrite();

                    __values.Capacity = value;
                }
            }

            public TValue this[in TKey key]
            {
                get
                {
                    __CheckRead();

                    __CheckKey(key);

                    return __values[key];
                }

                set
                {
                    __CheckWrite();

                    __values[key] = value;
                }
            }

            public Writer(ref SharedHashMap<TKey, TValue> instance)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = instance.m_Safety;
#endif

                __values = instance.__values;
            }

            public int Count()
            {
                __CheckRead();

                return __values.Count();
            }

            public bool ContainsKey(in TKey key)
            {
                __CheckRead();

                return __values.ContainsKey(key);
            }

            public bool TryGetValue(in TKey key, out TValue value)
            {
                __CheckRead();

                return __values.TryGetValue(key, out value);
            }

            public NativeKeyValueArrays<TKey, TValue> GetKeyValueArrays(Allocator allocator)
            {
                __CheckRead();

                return __values.GetKeyValueArrays(allocator);
            }

            public NativeArray<TKey> GetKeyArray(Allocator allocator)
            {
                __CheckRead();

                return __values.GetKeyArray(allocator);
            }

            public NativeArray<TValue> GetValueArray(Allocator allocator)
            {
                __CheckRead();

                return __values.GetValueArray(allocator);
            }

            public bool TryAdd(in TKey key, in TValue value)
            {
                __CheckWrite();

                return __values.TryAdd(key, value);
            }

            public void Add(in TKey key, in TValue value)
            {
                __CheckWrite();

                __values.Add(key, value);
            }

            public bool Remove(in TKey key)
            {
                __CheckWrite();

                /*if (!__values.TryGetValue(handle, out var value))
                    return false;

                value.Dispose();*/

                return __values.Remove(key);
            }

            public void Clear()
            {
                __CheckWrite();

                __values.Clear();
            }

            public Enumerator GetEnumerator()
            {
                return new Enumerator(__values
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                , m_Safety
#endif
                );
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private void __CheckKey(in TKey key)
            {
                if (!__values.ContainsKey(key))
                    throw new IndexOutOfRangeException();
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
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            }
        }

        [NativeContainer]
        [NativeContainerIsAtomicWriteOnly]
        public struct ParallelWriter
        {
            private UnsafeParallelHashMap<TKey, TValue>.ParallelWriter __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            public ParallelWriter(ref SharedHashMap<TKey, TValue> instance)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = instance.m_Safety;
#endif

                __values = instance.__values.AsParallelWriter();
            }

            public bool TryAdd(in TKey key, TValue value)
            {
                CheckWrite();

                /*if (!__values.TryGetValue(handle, out var value))
                    return false;

                value.Dispose();*/

                return __values.TryAdd(key, value);
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

        private UnsafeParallelHashMap<TKey, TValue> __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;

        public AtomicSafetyHandle GetAtomicSafetyHandle() => m_Safety;
#endif

        public bool isCreated => __values.IsCreated;

        public bool isEmpty => __values.IsEmpty;

        public unsafe ref LookupJobManager lookupJobManager => ref *__lookupJobManager;

        public Reader reader => new Reader(ref this);

        public Writer writer => new Writer(ref this);

        public ParallelWriter parallelWriter => new ParallelWriter(ref this);

        public SharedHashMap(Allocator allocator)
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

            __values = new UnsafeParallelHashMap<TKey, TValue>(1, allocator);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = AtomicSafetyHandle.Create();
#endif
        }

        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckDeallocateAndThrow(m_Safety);

            AtomicSafetyHandle.Release(m_Safety);
#endif

            unsafe
            {
                UnsafeUtility.Free(__lookupJobManager, Allocator);

                __lookupJobManager = null;
            }

            __values.Dispose();
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(__values
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                , m_Safety
#endif
                );
        }
    }
}