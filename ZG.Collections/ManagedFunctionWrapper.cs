using AOT;
using System;
using System.Runtime.InteropServices;

namespace ZG
{
    public unsafe struct ManagedFunctionWrapper
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MethodDelegate(void* functionPtr, void* arguments, int argumentsSize);

        [MonoPInvokeCallback(typeof(MethodDelegate))]
        private static void Method(void* functionPtr, void* arguments, int argumentsSize)
            => ((delegate*<void*, int, void>)functionPtr)(arguments, argumentsSize);

        private static IntPtr __wrapperMethodPtr;

        public static void Initialize()
        {
            if (__wrapperMethodPtr != default)
                return;

            var methodDelegate = new MethodDelegate(Method);
            GCHandle.Alloc(methodDelegate);
            __wrapperMethodPtr = Marshal.GetFunctionPointerForDelegate(methodDelegate);
        }

        private IntPtr __functionPtr;
        private IntPtr __wrapperPtr;

        public bool isCreated => __functionPtr != default;

        /// <summary>
        /// Creates a wrapper for a managed function pointer that can be called from unmanged context
        /// </summary>
        /// <param name="functionPtr">
        /// The function pointer of a method that receives a void* containing
        /// the arguments and an int containing the size in bytes of those arguments.
        /// </param>
        public ManagedFunctionWrapper(delegate*<void*, int, void> functionPtr)
        {
            __wrapperPtr = __wrapperMethodPtr;
            __functionPtr = new IntPtr(functionPtr);
        }

        public void Invoke(void* arguments, int argumentsSize)
        {
            if (__functionPtr == default)
                throw new NullReferenceException("Trying to invoke a null function pointer");

            ((delegate* unmanaged[Cdecl]<void*, void*, int, void>)__wrapperPtr)(((void*)__functionPtr), arguments, argumentsSize);
        }
    }
}