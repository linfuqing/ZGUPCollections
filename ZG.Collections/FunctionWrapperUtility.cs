using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using UnityEngine;

namespace ZG
{
    public static class FunctionWrapperUtility
    {
        private static List<GCHandle> __gcHandles;
        
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
            var result = CompileManagedFunctionPointer(containerDelegate, out var gcHandle);

            if (__gcHandles == null)
                __gcHandles = new List<GCHandle>();
            
            __gcHandles.Add(gcHandle);

            return result;
        }

        public static unsafe void Invoke<T>(ref this ManagedFunctionWrapper managedFunctionWrapper, in T buffer) where T : IUnsafeBuffer
        {
            managedFunctionWrapper.Invoke(buffer.GetRangePtr(out int size), size);
        }
    }
}