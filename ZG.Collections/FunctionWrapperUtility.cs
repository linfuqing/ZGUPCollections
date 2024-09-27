using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace ZG
{
    
    [JobProducerType(typeof(FunctionWrapperUtility.ManagedWrapper<>))]
    public interface IFunctionWrapper
    {
        void Invoke();
    }

    public struct FunctionFactory
    {
        public readonly struct Function
        {
            public readonly int Version;
            public readonly int Offset;
            public readonly int Size;

            public readonly ManagedFunctionWrapper ManagedFunctionWrapper;

            public Function(int version, int offset, int size, ManagedFunctionWrapper managedFunctionWrapper)
            {
                Version = version;
                Offset = offset;
                Size = size;
                ManagedFunctionWrapper = managedFunctionWrapper;
            }

            public unsafe void Invoke(int version, in NativeArray<byte> bytes)
            {
                if (version != Version)
                    return;
                
                ManagedFunctionWrapper.Invoke((byte*)bytes.GetUnsafePtr() + Offset, Size);
            }

            public unsafe ref T ReadWrite<T>(ref NativeArray<byte> bytes) where T : unmanaged, IFunctionWrapper
            {
                return ref FunctionWrapperUtility.ManagedWrapper<T>.ArgumentsFromPtr((byte*)bytes.GetUnsafePtr() + Offset, Size);
            }
            
            public unsafe T ReadOnly<T>(in NativeArray<byte> bytes) where T : unmanaged, IFunctionWrapper
            {
                return FunctionWrapperUtility.ManagedWrapper<T>.ArgumentsFromPtr((byte*)bytes.GetUnsafeReadOnlyPtr() + Offset, Size);
            }
        }

        public struct ParallelWriter
        {
            public readonly int Version;
            
            private UnsafeListEx<byte>.ParallelWriter __bytes;

            public ParallelWriter(int version, ref NativeList<byte> bytes)
            {
                Version = version;
                __bytes = new UnsafeListEx<byte>.ParallelWriter(ref bytes);
            }
            
            public unsafe Function Create<T>(ref T value) where T : unmanaged, IFunctionWrapper
            {
                int size = UnsafeUtility.SizeOf<T>();
                int offset = __bytes.AddRangeNoResize(UnsafeUtility.AddressOf(ref value), size);

                var function = new Function(
                    Version, 
                    offset, 
                    size, 
                    FunctionWrapperUtility.ManagedWrapper<T>.managedFunctionWrapper);
            
                return function;
            }
        }

        private NativeList<byte> __bytes;

        public int capacity
        {
            get => __bytes.Capacity;

            set => __bytes.Capacity = value;
        }

        public int length => __bytes.Length;
        
        public unsafe int version => *(int*)((UnsafeList<byte>*)NativeListUnsafeUtility.GetInternalListDataPtrUnchecked(ref __bytes))->Ptr;

        public ParallelWriter parallelWriter => new ParallelWriter(version, ref __bytes);

        public FunctionFactory(in AllocatorManager.AllocatorHandle allocator)
        {
            __bytes = new NativeList<byte>(allocator);
            
            __bytes.Resize(UnsafeUtility.SizeOf<int>(), NativeArrayOptions.ClearMemory);
        }

        public void Dispose()
        {
            __bytes.Dispose();
        }
        
        public void Clear()
        {
            __bytes.ResizeUninitialized(UnsafeUtility.SizeOf<int>());

            ++__GetVersion();
        }

        public unsafe Function Create<T>(ref T value) where T : unmanaged, IFunctionWrapper
        {
            var function = new Function(
                version, 
                __bytes.Length, 
                UnsafeUtility.SizeOf<T>(), 
                FunctionWrapperUtility.ManagedWrapper<T>.managedFunctionWrapper);
            
            __bytes.AddRange(UnsafeUtility.AddressOf(ref value), UnsafeUtility.SizeOf<T>());

            return function;
        }
        
        public void Invoke(in Function function)
        {
            function.Invoke(version, __bytes.AsArray());
        }
        
        private unsafe ref int __GetVersion()
        {
            return ref *(int*)__bytes.GetUnsafePtr();
        }
    }

    public struct SharedFunctionFactory
    {
        [BurstCompile]
        private struct Resize : IJob
        {
            public NativeArray<int> functionCountAndSize;
            
            public FunctionFactory instance;

            public SharedList<FunctionFactory.Function>.Writer functions;

            public void Execute()
            {
                functions.capacity = math.max(functions.capacity, functions.length + functionCountAndSize[0]);

                instance.capacity = math.max(instance.capacity, instance.length + functionCountAndSize[1]);
            }
        }
        
        public struct Writer
        {
            private FunctionFactory __factory;
            private SharedList<FunctionFactory.Function>.Writer __functions;

            public Writer(ref SharedFunctionFactory factory)
            {
                __factory = factory.__instance;
                __functions = factory.__functions.writer;
            }
            
            public void Invoke<T>(ref T value) where T : unmanaged, IFunctionWrapper
            {
                __functions.Add(__factory.Create(ref value));
            }
        }

        public struct ParallelWriter
        {
            private FunctionFactory.ParallelWriter __factory;
            private SharedList<FunctionFactory.Function>.ParallelWriter __functions;

            public ParallelWriter(ref SharedFunctionFactory factory)
            {
                __factory = factory.__instance.parallelWriter;
                __functions = factory.__functions.parallelWriter;
            }
            
            public void Invoke<T>(ref T value) where T : unmanaged, IFunctionWrapper
            {
                __functions.AddNoResize(__factory.Create(ref value));
            }
        }

        private FunctionFactory __instance;

        private SharedList<FunctionFactory.Function> __functions;

        public ref LookupJobManager lookupJobManager => ref __functions.lookupJobManager;

        public Writer writer => new Writer(ref this);

        public SharedFunctionFactory(in AllocatorManager.AllocatorHandle allocator)
        {
            __instance = new FunctionFactory(allocator);
            __functions = new SharedList<FunctionFactory.Function>(allocator);
        }

        public void Dispose()
        {
            __instance.Dispose();
            __functions.Dispose();
        }

        public ParallelWriter AsParallelWriter(in NativeArray<int> functionCountAndSize, ref JobHandle jobHandle)
        {
            Resize resize;
            resize.functionCountAndSize = functionCountAndSize;
            resize.instance = __instance;
            resize.functions = __functions.writer;
            jobHandle = resize.ScheduleByRef(jobHandle);

            return new ParallelWriter(ref this);
        }

        public void Apply()
        {
            __functions.lookupJobManager.CompleteReadWriteDependency();

            var functions = __functions.reader.AsArray();
            foreach (var function in functions)
                __instance.Invoke(function);
            
            __functions.writer.Clear();
            
            __instance.Clear();
        }
    }

    public static class FunctionWrapperUtility
    {
        public struct ManagedWrapper<T> where T : unmanaged, IFunctionWrapper
        {
            private static readonly SharedStatic<ManagedFunctionWrapper> __managedFunctionWrapper = SharedStatic<ManagedFunctionWrapper>.GetOrCreate<ManagedWrapper<T>>();

            public static ManagedFunctionWrapper managedFunctionWrapper => __managedFunctionWrapper.Data;
            
            public static unsafe void Init()
            {
                __managedFunctionWrapper.Data = new ManagedFunctionWrapper(&__Invoke);
            }

            public static unsafe void Execute(void* argumentsPtr, int size)
            {
                __managedFunctionWrapper.Data.Invoke(argumentsPtr, size);
            }

            public static unsafe void Execute(ref T wrapper)
            {
                Execute(UnsafeUtility.AddressOf(ref wrapper), UnsafeUtility.SizeOf<T>());
            }

            private static unsafe void __Invoke(void* argumentsPtr, int size)
            {
                ref var function = ref ArgumentsFromPtr(argumentsPtr, size);

                try
                {
                    function.Invoke();
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e.InnerException ?? e);
                }
            }
        
            public static unsafe ref T ArgumentsFromPtr(void* argumentsPtr, int size)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (size != UnsafeUtility.SizeOf<T>())
                    throw new InvalidOperationException("The requested argument type size does not match the provided one");
#endif
                return ref *(T*)argumentsPtr;
            }
        }

        private static List<GCHandle> __gcHandles;
        
        public static void EarlyJobInit<T>()
            where T : unmanaged, IFunctionWrapper
        {
            ManagedFunctionWrapper.Initialize();
            
            ManagedWrapper<T>.Init();
        }

        public static void Run<T>(this ref T functionWrapper) where T : unmanaged, IFunctionWrapper
        {
            ManagedWrapper<T>.Execute(ref functionWrapper);
        }

        /*[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        public static void Initialize()
        {
            ManagedFunctionWrapper.Initialize();
        }*/

        [BurstDiscard]
        public static FunctionPointer<T> CompileManagedFunctionPointer<T>(T containerDelegate, out GCHandle gcHandle) where T : Delegate
        {
            gcHandle = GCHandle.Alloc(containerDelegate);
            return new FunctionPointer<T>(Marshal.GetFunctionPointerForDelegate(containerDelegate));
        }

        [BurstDiscard]
        public static FunctionPointer<T> CompileManagedFunctionPointer<T>(T containerDelegate) where T : Delegate
        {
            var result = CompileManagedFunctionPointer(containerDelegate, out var gcHandle);

            if (__gcHandles == null)
                __gcHandles = new List<GCHandle>();
            
            __gcHandles.Add(gcHandle);

            return result;
        }
    }
}