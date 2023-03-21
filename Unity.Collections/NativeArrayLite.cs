using System;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ZG
{
	public struct NativeArrayLite<T> where T : struct
	{
		[NativeDisableUnsafePtrRestriction]
		private unsafe void* __ptr;

		private int __length;

		private Allocator __allocator;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
		internal AtomicSafetyHandle m_Safety;

		private static readonly SharedStatic<int> StaticSafetyId = SharedStatic<int>.GetOrCreate<NativeArrayLite<T>>();
#endif

		public unsafe bool isCreated => __ptr != null;

		public int Length => __length;

		public Allocator allocator => __allocator;

		public unsafe T this[int index]
		{
			get
			{
				__CheckElementReadAccess(index);

				return UnsafeUtility.ReadArrayElement<T>(__ptr, index);
			}

			[WriteAccessRequired]
			set
			{
				__CheckElementWriteAccess(index);

				UnsafeUtility.WriteArrayElement(__ptr, index, value);
			}
		}

		public unsafe NativeArrayLite(int length, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory)
		{
			__Allocate(length, allocator, out this);
			if ((options & NativeArrayOptions.ClearMemory) == NativeArrayOptions.ClearMemory)
				UnsafeUtility.MemClear(__ptr, (long)length * (long)UnsafeUtility.SizeOf<T>());
		}

		[WriteAccessRequired]
		public unsafe void Dispose()
		{
#if ENABLE_UNITY_COLLECTIONS_CHECKS
			if (__ptr == null)
				throw new ObjectDisposedException("The NativeArray is already disposed.");
			if (__allocator == Allocator.Invalid)
				throw new InvalidOperationException("The NativeArray can not be Disposed because it was not allocated with a valid allocator.");
#endif

			if (__allocator > Allocator.None)
			{
#if ENABLE_UNITY_COLLECTIONS_CHECKS
				AtomicSafetyHandle.Release(m_Safety);
#endif

				UnsafeUtility.Free(__ptr, __allocator);

				__allocator = Allocator.Invalid;
			}

			__ptr = null;
			__length = 0;
		}

		public static unsafe implicit operator NativeArray<T>(in NativeArrayLite<T> value)
        {
			var result = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(value.__ptr, value.__length, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
			NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref result, value.m_Safety);
#endif

			return result;
		}

		private static unsafe void __Allocate(int length, Allocator allocator, out NativeArrayLite<T> array)
		{
			long num = (long)UnsafeUtility.SizeOf<T>() * (long)length;
			__CheckAllocateArguments(length, allocator, num);
			array = default(NativeArrayLite<T>);
			array.__allocator = allocator;
			array.__ptr = UnsafeUtility.Malloc(num, UnsafeUtility.AlignOf<T>(), allocator);
			array.__length = length;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
			array.m_Safety = AtomicSafetyHandle.Create();

			__InitStaticSafetyId(ref array.m_Safety);
#endif
		}

#if ENABLE_UNITY_COLLECTIONS_CHECKS
		[BurstDiscard]
		private static void __InitStaticSafetyId(ref AtomicSafetyHandle handle)
		{
			if (StaticSafetyId.Data == 0)
				StaticSafetyId.Data = AtomicSafetyHandle.NewStaticSafetyId<NativeArrayLite<T>>();

			AtomicSafetyHandle.SetStaticSafetyId(ref handle, StaticSafetyId.Data);
		}
#endif

		[Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
		private unsafe void __CheckElementReadAccess(int index)
		{
			if (index < 0 || index >= __length)
				__FailOutOfRangeError(index);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
			int* ptr = (int*)(void*)m_Safety.versionNode;
			if (m_Safety.version != (*ptr & AtomicSafetyHandle.ReadCheck))
				AtomicSafetyHandle.CheckReadAndThrowNoEarlyOut(m_Safety);
#endif
		}

		[Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
		private unsafe void __CheckElementWriteAccess(int index)
		{
			if (index < 0 || index >= __length)
				__FailOutOfRangeError(index);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
			int* ptr = (int*)(void*)m_Safety.versionNode;
			if (m_Safety.version != (*ptr & AtomicSafetyHandle.WriteCheck))
				AtomicSafetyHandle.CheckWriteAndThrowNoEarlyOut(m_Safety);
#endif
		}

		[Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
		private void __FailOutOfRangeError(int index)
		{
			throw new IndexOutOfRangeException($"Index {index} is out of range of '{__length}' Length.");
		}

		[Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
		private static void __CheckAllocateArguments(int length, Allocator allocator, long totalSize)
		{
			if (allocator <= Allocator.None)
				throw new ArgumentException("Allocator must be Temp, TempJob or Persistent", "allocator");

			if (length < 0)
				throw new ArgumentOutOfRangeException("length", "Length must be >= 0");

			__IsUnmanagedAndThrow();
		}

		[BurstDiscard]
		private static void __IsUnmanagedAndThrow()
		{
			if (UnsafeUtility.IsValidNativeContainerElementType<T>())
				return;

			throw new InvalidOperationException($"{typeof(T)} used in NativeArray<{typeof(T)}> must be unmanaged (contain no managed types) and cannot itself be a native container type.");
		}
	}
}