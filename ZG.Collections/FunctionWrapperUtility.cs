using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using UnityEngine;

namespace ZG
{
    public static class FunctionWrapperUtility
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        public static void Initialize()
        {
            ManagedFunctionWrapper.Initialize();
        }

        [BurstDiscard]
        public static FunctionPointer<T> CompileManagedFunctionPointer<T>(T containerDelegate, out GCHandle gcHandle) where T : Delegate
        {
            gcHandle = GCHandle.Alloc(containerDelegate);
            return new FunctionPointer<T>(Marshal.GetFunctionPointerForDelegate(containerDelegate));
        }

        [BurstDiscard]
        public static FunctionPointer<T> CompileManagedFunctionPointer<T>(T containerDelegate) where T : Delegate
        {
            return CompileManagedFunctionPointer(containerDelegate, out _);
        }

        public static unsafe void Invoke<T>(ref this ManagedFunctionWrapper managedFunctionWrapper, in T buffer) where T : IUnsafeBuffer
        {
            managedFunctionWrapper.Invoke(buffer.GetRangePtr(out int size), size);
        }
    }
}