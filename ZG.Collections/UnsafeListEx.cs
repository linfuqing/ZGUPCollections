using System;
using System.Diagnostics;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ZG
{
    [DebuggerDisplay("Length = {length}, Capacity = {capacity}, IsCreated = {isCreated}, IsEmpty = {isEmpty}")]
    [DebuggerTypeProxy(typeof(UnsafeListExDebugView<>))]
    public unsafe struct UnsafeListEx<T> : IDisposable where T : unmanaged
    {
        [NativeDisableUnsafePtrRestriction]
        private UnsafeList<T>* __value;

        public bool isCreated => __value != null && __value->IsCreated;

        public bool isEmpty => __value->IsEmpty;

        public int length => __value->Length;

        public int capacity => __value->Capacity;

        public AllocatorManager.AllocatorHandle allocator => __value->Allocator;

        public unsafe T this[int index]
        {
            get
            {
                __CheckBounds(index);

                return UnsafeUtility.ReadArrayElement<T>(__value->Ptr, index);
            }

            set
            {
                __CheckBounds(index);

                UnsafeUtility.WriteArrayElement(__value->Ptr, index, value);
            }
        }

        public UnsafeListEx(int capacity, Allocator allocator)
        {
            __value = UnsafeList<T>.Create(capacity, allocator);
        }

        public UnsafeListEx(Allocator allocator) : this(1, allocator)
        {

        }

        public void Dispose()
        {
            UnsafeList<T>.Destroy(__value);

            __value = null;
        }

        public ref T ElementAt(int index) => ref UnsafeUtility.ArrayElementAsRef<T>(__value->Ptr, index);

        public void Resize(int length, NativeArrayOptions options)
        {
            __value->Resize(length, options);
        }

        public void ResizeUninitialized(int length) => Resize(length, NativeArrayOptions.UninitializedMemory);

        public void RemoveAt(int index) => __value->RemoveAt(index);

        public void Add(in T value) => __value->Add(value);

        public void AddNoResize(in T value) => __value->AddNoResize(value);

        public void AddRange(in UnsafeListEx<T> values) => __value->AddRange(*values.__value);

        public void AddRange(in NativeArray<T> values) => __value->AddRange(values.GetUnsafeReadOnlyPtr(), values.Length);

        public void Clear() => __value->Clear();

        public NativeArray<T> AsArray()
        {
            var result = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(__value->Ptr, __value->Length, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref result, AtomicSafetyHandle.GetTempMemoryHandle());
#endif

            return result;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void __CheckBounds(int index)
        {
            if (index < 0 || index >= length)
                throw new IndexOutOfRangeException();
        }
    }


    internal sealed class UnsafeListExDebugView<T> where T : unmanaged
    {
        UnsafeListEx<T> data;

        public UnsafeListExDebugView(UnsafeListEx<T> data)
        {
            this.data = data;
        }

        public T[] Items
        {
            get
            {
                return data.AsArray().ToArray();
            }
        }
    }
}