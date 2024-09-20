using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace ZG
{
    public class SharedNativeArray<T> : ScriptableObject, IEnumerable<T>, System.IDisposable where T : struct
    {
        public class Enumerator : Object, IEnumerator<T>
        {
            private int __index;
            private int __length;
            private unsafe void* __values;

            public unsafe T Current
            {
                get
                {
                    return UnsafeUtility.ReadArrayElement<T>(__values, __index);
                }
            }

            public unsafe static Enumerator Create(int length, void* values)
            {
                Enumerator result = Create<Enumerator>();
                result.__index = -1;
                result.__length = length;
                result.__values = values;
                return result;
            }

            public bool MoveNext()
            {
                return ++__index < __length;
            }

            public void Reset()
            {
                __index = -1;
            }

            object IEnumerator.Current
            {
                get
                {
                    return Current;
                }
            }
        }

        private unsafe void* __values;

        public int length
        {
            get;

            private set;
        }

        public unsafe NativeArray<T> values
        {
            get
            {
                var result = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(
                    __values,
                    length,
                    Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref result, AtomicSafetyHandle.GetTempMemoryHandle());
#endif

                return result;
            }
        }

        public unsafe T this[int index]
        {
            get
            {
#if DEBUG
                if (index >= length)
                    throw new System.IndexOutOfRangeException();
#endif
                return UnsafeUtility.ReadArrayElement<T>(__values, index);
            }

            set
            {

#if DEBUG
                if (index >= length)
                    throw new System.IndexOutOfRangeException();
#endif
                UnsafeUtility.WriteArrayElement(__values, index, value);
            }
        }

        public static unsafe U Create<U>(NativeArray<T> values) where U : SharedNativeArray<T>
        {
            U result = CreateInstance<U>();
            result.length = values.Length;

            result.__values = SharedNativeArrayUtility.Clone(values);

            return result;
        }

        ~SharedNativeArray()
        {
            Dispose();
        }

        public unsafe Enumerator GetEnumerator()
        {
            return Enumerator.Create(length, __values);
        }
        
        public unsafe void Dispose()
        {
            if (__values != null)
            {
                UnsafeUtility.Free(__values, Allocator.Persistent);

                __values = null;
            }

            length = 0;
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

    [BurstCompile]
    public static class SharedNativeArrayUtility
    {
        [BurstCompile]
        public static unsafe void* Clone<T>(NativeArray<T> values) where T : struct
        {
            long size = UnsafeUtility.SizeOf<T>() * values.Length;
            var result = UnsafeUtility.Malloc(
                size,
                UnsafeUtility.AlignOf<T>(),
                Allocator.Persistent);

            UnsafeUtility.MemCpy(result, NativeArrayUnsafeUtility.GetUnsafePtr(values), size);

            return result;
        }

    }
}