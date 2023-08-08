using System;
using System.IO;
using System.Threading;
using System.Diagnostics;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections.LowLevel.Unsafe.NotBurstCompatible;
using Unity.Burst;

namespace ZG
{
    public interface IUnsafeBuffer
    {
        unsafe void* GetRangePtr(out int size);

        NativeArray<T> AsArray<T>(int byteOffset, int length) where T : struct;
    }

    public interface IUnsafeReader
    {
        unsafe void* Read(int length);

        T Read<T>() where T : struct;

        NativeArray<T> ReadArray<T>(int length) where T : struct;
    }

    public interface INativeReader : IUnsafeReader
    {
        int position { get; }

        UnsafeBlock ReadBlock(int length);
    }

    public interface IUnsafeWriter
    {
        unsafe void Write(void* ptr, int length);
    }

    public interface INativeWriter : IUnsafeWriter
    {
        UnsafeBlock WriteBlock(int length, bool isClear);
    }

    public struct UnsafeBlock : IUnsafeBuffer
    {
        public struct Reader : INativeReader
        {
            public int __offset;
            public UnsafeBlock __block;

            public bool isVail => position < __block.__length;

            public int position => __block.__position + __offset;

            public Reader(in UnsafeBlock block)
            {
                __offset = 0;
                __block = block;
            }

            public unsafe void* Read(int length)
            {
                var reader = new UnsafeAppendBuffer.Reader(__block.__buffer->Ptr, math.min(__block.__length, __block.__buffer->Length));
                reader.Offset = __offset + __block.__position;
                void* ptr = reader.ReadNext(length);
                __offset = reader.Offset - __block.__position;
                return ptr;
            }

            public unsafe T Read<T>() where T : struct
            {
                UnsafeUtility.CopyPtrToStructure(Read(UnsafeUtility.SizeOf<T>()), out T value);

                return value;
            }

            public unsafe string ReadString()
            {
                var reader = new UnsafeAppendBuffer.Reader(__block.__buffer->Ptr, math.min(__block.__length, __block.__buffer->Length));
                reader.Offset = __offset + __block.__position;
                reader.ReadNextNBC(out var result);
                __offset = reader.Offset - __block.__position;
                return result;
            }

            public unsafe NativeArray<T> ReadArray<T>(int length) where T : struct
            {
                var result = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(Read(length * UnsafeUtility.SizeOf<T>()), length, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref result, AtomicSafetyHandle.GetTempMemoryHandle());
#endif

                return result;
            }

            public unsafe UnsafeBlock ReadBlock(int length)
            {
                int position = __block.__position + __offset;

                Read(length);

                return new UnsafeBlock(__block.__buffer, position, position + length);
            }

            /*public unsafe string ReadString()
            {
                var reader = new UnsafeAppendBuffer.Reader(__block.__buffer->Ptr, math.min(__block.__length, __block.__buffer->Length));
                reader.Offset = __offset + __block.__position;

                reader.ReadNext(out string value);

                __offset = reader.Offset - __block.__position;

                return value;
            }*/
        }

        public struct Writer : INativeWriter
        {
            public int __offset;
            public UnsafeBlock __block;

            public Writer(in UnsafeBlock block)
            {
                __offset = 0;
                __block = block;
            }

            public unsafe void Write(void* ptr, int length)
            {
                int offset = __offset + __block.__position, position = offset + length;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (position > math.min(__block.__length, __block.__buffer->Length))
                    throw new EndOfStreamException();
#endif

                UnsafeUtility.MemCpy(__block.__buffer->Ptr + offset, ptr, length);

                __offset = position - __block.__position;
            }

            public unsafe UnsafeBlock WriteBlock(int length, bool isClear)
            {
                int offset = __offset + __block.__position, position = offset + length;
                if (position > math.min(__block.__length, __block.__buffer->Length))
                    throw new EndOfStreamException();

                if(isClear)
                    UnsafeUtility.MemClear(__block.__buffer->Ptr + offset, length);

                __offset = position - __block.__position;

                return new UnsafeBlock(__block.__buffer, offset, position);
            }
        }

        public static readonly UnsafeBlock Empty = default;

        [NativeDisableUnsafePtrRestriction]
        private readonly unsafe UnsafeAppendBuffer* __buffer;
        private readonly int __position;
        private readonly int __length;

        public unsafe bool isCreated => __buffer != null;

        public unsafe int size => math.min(__buffer->Length, __length) - __position;

        public Reader reader => new Reader(this);

        public Writer writer => new Writer(this);

        internal unsafe UnsafeBlock(UnsafeAppendBuffer* buffer, int posiiton, int length)
        {
            __buffer = buffer;
            __position = posiiton;
            __length = length;
        }

        public unsafe ref T As<T>() where T : struct
        {
            void* ptr = GetRangePtr(out int size);

            _CheckSize<T>(size);

            return ref UnsafeUtility.AsRef<T>(ptr);
        }

        public unsafe NativeArray<T> AsArray<T>(int byteOffset = 0, int length = -1) where T : struct
        {
            void* ptr = GetRangePtr(out int size);

            _CheckRange<T>(byteOffset, length, size);

            if (length < 0)
                length = (size - byteOffset) / UnsafeUtility.SizeOf<T>();

            var result = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(
                (byte*)ptr + byteOffset, 
                length, 
                Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref result, AtomicSafetyHandle.GetTempMemoryHandle());
#endif

            return result;
        }

        public unsafe void* GetRangePtr(out int size)
        {
            size = this.size;

            return __buffer->Ptr + __position;
        }

        public unsafe void CopyFrom(void* src)
        {
            void* dst = GetRangePtr(out int size);
            UnsafeUtility.MemCpy(dst, src, size);
        }

        public unsafe void Clear()
        {
            void* ptr = GetRangePtr(out int size);
            UnsafeUtility.MemClear(ptr, size);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void _CheckRange<T>(int byteOffset, int length, int size) where T : struct
        {
            if (byteOffset < 0 || byteOffset + length * UnsafeUtility.SizeOf<T>() > size)
                throw new InvalidCastException();
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void _CheckSize<T>(int size) where T : struct
        {
            if (UnsafeUtility.SizeOf<T>() != size)
                throw new InvalidCastException();
        }
    }

    public struct UnsafeBlock<T> where T : struct
    {
        private UnsafeBlock __value;

        public unsafe T value
        {
            get
            {
                void* ptr = __value.GetRangePtr(out int size);

                if (UnsafeUtility.SizeOf<T>() != size)
                    throw new InvalidCastException();

                UnsafeUtility.CopyPtrToStructure(ptr, out T value);

                return value;
            }

            set
            {
                void* ptr = __value.GetRangePtr(out int size);

                __CheckSize(size);

                UnsafeUtility.CopyStructureToPtr(ref value, ptr);
            }
        }

        public UnsafeBlock(in UnsafeBlock value)
        {
            __value = value;

            __CheckSize(value.size);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void __CheckSize(int size)
        {
            if (UnsafeUtility.SizeOf<T>() != size)
                throw new InvalidCastException();
        }
    }

    public struct UnsafeBuffer : IDisposable, IUnsafeBuffer
    {
        public struct Reader : INativeReader
        {
            [NativeDisableUnsafePtrRestriction]
            private unsafe UnsafeBuffer* __buffer;

            public unsafe Reader(ref UnsafeBuffer buffer)
            {
                __buffer = (UnsafeBuffer*)UnsafeUtility.AddressOf(ref buffer);
            }

            public unsafe bool isCreated => __buffer != null;

            public unsafe bool isVail => __buffer->__value.Length < __buffer->__length;

            public unsafe int position => __buffer->__value.Length;

            public unsafe int length => __buffer->length;

            public unsafe void* Read(int length)
            {
                var reader = __AsReader();

                void* ptr = reader.ReadNext(length);

                __buffer->__value.Length = reader.Offset;

                return ptr;
            }

            public unsafe T Read<T>() where T : struct
            {
                UnsafeUtility.CopyPtrToStructure(Read(UnsafeUtility.SizeOf<T>()), out T value);

                return value;
            }

            public unsafe NativeArray<T> ReadArray<T>(int length) where T : struct
            {
                var result = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(Read(length * UnsafeUtility.SizeOf<T>()), length, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref result, AtomicSafetyHandle.GetTempMemoryHandle());
#endif

                return result;
            }

            public unsafe UnsafeBlock ReadBlock(int length)
            {
                var reader = __AsReader();

                int position = reader.Offset;

                reader.ReadNext(length);

                __buffer->__value.Length = reader.Offset;

                return new UnsafeBlock((UnsafeAppendBuffer*)UnsafeUtility.AddressOf(ref __buffer->__value), position, reader.Offset);
            }

            public unsafe string ReadString()
            {
                var reader = __AsReader();

                reader.ReadNextNBC(out string value);

                __buffer->__value.Length = reader.Offset;

                return value;
            }

            private unsafe UnsafeAppendBuffer.Reader __AsReader()
            {
                var buffer = new UnsafeAppendBuffer(__buffer->__value.Ptr, __buffer->__length);
                buffer.Length = __buffer->__length;
                var reader = buffer.AsReader();
                reader.Offset = __buffer->__value.Length;

                return reader;
            }
        }

        public struct Writer : INativeWriter
        {
            [NativeDisableUnsafePtrRestriction]
            private unsafe UnsafeBuffer* __buffer;

            public unsafe bool isCreated => __buffer != null;

            public unsafe int position
            {
                get
                {
                    return __buffer->__value.Length;
                }
            }

            public unsafe Writer(ref UnsafeBuffer buffer)
            {
                __buffer = (UnsafeBuffer*)UnsafeUtility.AddressOf(ref buffer);
            }

            public unsafe void Write(string value)
            {
                __buffer->__value.AddNBC(value);

                __buffer->__length = math.max(__buffer->__length, __buffer->__value.Length);
            }

            public unsafe void Write(void* ptr, int length)
            {
                __buffer->__value.Add(ptr, length);

                __buffer->__length = math.max(__buffer->__length, __buffer->__value.Length);
            }

            public unsafe UnsafeBlock WriteBlock(int length, bool isClear)
            {
                int offset = __buffer->__value.Length, position = offset + length;
                __buffer->__value.ResizeUninitialized(position);

                if (isClear)
                    UnsafeUtility.MemClear(__buffer->__value.Ptr + offset, length);

                __buffer->__length = math.max(__buffer->__length, __buffer->__value.Length);

                return new UnsafeBlock((UnsafeAppendBuffer*)UnsafeUtility.AddressOf(ref __buffer->__value), offset, position);
            }
        }

        public struct ParallelWriter : INativeWriter
        {
            [NativeDisableUnsafePtrRestriction]
            private unsafe UnsafeBuffer* __buffer;

            public unsafe bool isCreated => __buffer != null;

            public unsafe ParallelWriter(ref UnsafeBuffer buffer)
            {
                __buffer = (UnsafeBuffer*)UnsafeUtility.AddressOf(ref buffer);
            }

            public unsafe void Write(void* ptr, int length)
            {
                int position = Interlocked.Add(ref __buffer->__value.Length, length);

                __CheckCapacity(position, length);

                UnsafeUtility.MemCpy(__buffer->__value.Ptr + (position - length), ptr, length);
            }

            public unsafe UnsafeBlock WriteBlock(int length, bool isClear)
            {
                int position = Interlocked.Add(ref __buffer->__value.Length, length);

                __CheckCapacity(position, length);

                int offset = position - length;
                if (isClear)
                    UnsafeUtility.MemClear(__buffer->__value.Ptr + offset, length);

                return new UnsafeBlock((UnsafeAppendBuffer*)UnsafeUtility.AddressOf(ref __buffer->__value), offset, position);
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            unsafe void __CheckCapacity(int bufferLength, int length)
            {
                if (bufferLength > __buffer->__value.Capacity)
                {
                    Interlocked.Add(ref __buffer->__value.Length, -length);

                    throw new OutOfMemoryException();
                }
            }
        }

        private int __length;
        private UnsafeAppendBuffer __value;

        public bool isCreated => __value.IsCreated;

        public AllocatorManager.AllocatorHandle allocator => __value.Allocator;

        public Reader reader => new Reader(ref this);

        public Writer writer => new Writer(ref this);

        public ParallelWriter parallelWriter => new ParallelWriter(ref this);

        public int capacity
        {
            get => __value.Capacity;

            set => __value.SetCapacity(value);
        }

        public unsafe int length
        {
            get
            {
                return __length;
            }

            set
            {
                __CheckCapacity(value);

                if (__value.Length > value)
                    __value.Length = value;

                __length = value;
            }
        }

        public unsafe int position
        {
            get
            {

                return __value.Length;
            }

            set
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (value > __length)
                    throw new InvalidOperationException();
#endif

                __value.Length = value;
            }
        }

        public UnsafeBuffer(int initialCapacity, int alignment, in AllocatorManager.AllocatorHandle allocator)
        {
            __length = 0;
            __value = new UnsafeAppendBuffer(initialCapacity, alignment, allocator);
        }

        public unsafe void Dispose()
        {
            __length = 0;
            __value.Dispose();
        }

        public unsafe void* GetRangePtr(out int size)
        {
            size = __length;

            return __value.Ptr;
        }

        public void Reset()
        {
            __length = 0;
            __value.Reset();
        }

        public unsafe byte[] ToBytes()
        {
            var value = __value;
            value.Length = __length;
            var bytes = value.ToBytesNBC();

            return bytes;
        }

        public unsafe NativeArray<T> AsArray<T>(int byteOffset, int length) where T : struct
        {
            void* ptr = GetRangePtr(out int size);

            UnsafeBlock._CheckRange<T>(byteOffset, length, size);

            if (length < 0)
                length = (size - byteOffset) / UnsafeUtility.SizeOf<T>();

            var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>((byte*)ptr + byteOffset, length, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, AtomicSafetyHandle.GetTempMemoryHandle());
#endif

            return array;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        unsafe void __CheckCapacity(int length)
        {
            if (length > __value.Capacity)
                throw new InvalidOperationException();

        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void __CheckRange<T>(int position, int length, int size) where T : struct
        {
            if (position + UnsafeUtility.SizeOf<T>() * length > size)
                throw new InvalidCastException();
        }

    }

    public struct UnsafeBufferEx : IDisposable, IUnsafeBuffer
    {
        public struct Reader : INativeReader
        {
            private UnsafeBuffer.Reader __value;

            public bool isCreated => __value.isCreated;

            public bool isVail
            {
                get
                {
                    return __value.isVail;
                }
            }

            public int position
            {
                get
                {
                    return __value.position;
                }
            }

            public unsafe Reader(ref UnsafeBufferEx buffer)
            {
                __value = new UnsafeBuffer.Reader(ref UnsafeUtility.AsRef<UnsafeBuffer>(buffer.__value));
            }

            public unsafe void* Read(int length)
            {
                return __value.Read(length);
            }

            public T Read<T>() where T : struct
            {
                return __value.Read<T>();
            }

            public NativeArray<T> ReadArray<T>(int length) where T : struct
            {
                return __value.ReadArray<T>(length);
            }

            public UnsafeBlock ReadBlock(int length)
            {
                return __value.ReadBlock(length);
            }

            public string ReadString()
            {
                return __value.ReadString();
            }
        }

        public struct Writer : INativeWriter
        {
            private UnsafeBuffer.Writer __value;

            public bool isCreated => __value.isCreated;

            public int position
            {
                get
                {
                    return __value.position;
                }
            }

            public unsafe Writer(ref UnsafeBufferEx buffer)
            {
                __value = new UnsafeBuffer.Writer(ref UnsafeUtility.AsRef<UnsafeBuffer>(buffer.__value));
            }

            public void Write(string value)
            {
                __value.Write(value);
            }

            public unsafe void Write(void* ptr, int length)
            {
                __value.Write(ptr, length);
            }

            public UnsafeBlock WriteBlock(int length, bool isClear)
            {
                return __value.WriteBlock(length, isClear);
            }
        }

        public struct ParallelWriter : INativeWriter
        {
            private UnsafeBuffer.ParallelWriter __value;

            public bool isCreated => __value.isCreated;

            public unsafe ParallelWriter(ref UnsafeBufferEx buffer)
            {
                __value = new UnsafeBuffer.ParallelWriter(ref UnsafeUtility.AsRef<UnsafeBuffer>(buffer.__value));
            }

            public unsafe void Write(void* ptr, int length)
            {
                __value.Write(ptr, length);
            }

            public UnsafeBlock WriteBlock(int length, bool isClear)
            {
                return __value.WriteBlock(length, isClear);
            }
        }

        [NativeDisableUnsafePtrRestriction]
        private unsafe UnsafeBuffer* __value;

        public unsafe bool isCreated => __value != null;

        public unsafe AllocatorManager.AllocatorHandle allocator => __value->allocator;

        public unsafe int capacity
        {
            get => __value->capacity;

            set => __value->capacity = value;
        }

        public unsafe int length
        {
            get
            {
                return __value->length;
            }

            set
            {
                __value->length = value;
            }
        }

        public unsafe int position
        {
            get
            {
                return __value->position;
            }

            set
            {
                __value->position = value;
            }
        }

        public Reader reader
        {
            get
            {
                return new Reader(ref this);
            }
        }

        public Writer writer
        {
            get
            {
                return new Writer(ref this);
            }
        }

        public ParallelWriter parallelWriter
        {
            get
            {
                return new ParallelWriter(ref this);
            }
        }

        public unsafe UnsafeBufferEx(in AllocatorManager.AllocatorHandle allocator, int alignment, int initialCapacity = 0)
        {
            __value = AllocatorManager.Allocate<UnsafeBuffer>(allocator);

            *__value = new UnsafeBuffer(initialCapacity, alignment, allocator);
        }

        public void Reset()
        {
            position = 0;
        }

        public unsafe void Dispose()
        {
            var allocator = __value->allocator;

            __value->Dispose();

            AllocatorManager.Free(allocator, __value);

            __value = null;
        }

        public unsafe NativeArray<T> AsArray<T>(int byteOffset, int length) where T : struct
        {
            void* ptr = GetRangePtr(out int size);

            UnsafeBlock._CheckRange<T>(byteOffset, length, size);

            if (length < 0)
                length = (size - byteOffset) / UnsafeUtility.SizeOf<T>();

            var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>((byte*)ptr + byteOffset, length, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, AtomicSafetyHandle.GetTempMemoryHandle());
#endif

            return array;
        }

        public unsafe void* GetRangePtr(out int size)
        {
            return __value->GetRangePtr(out size);
        }

        public unsafe byte[] ToBytes()
        {
            return __value->ToBytes();
        }
    }

    [NativeContainer]
    public struct NativeBuffer : IDisposable, IUnsafeBuffer
    {
        [NativeContainer]
        public struct Reader : INativeReader
        {
            private UnsafeBuffer.Reader __value;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            public bool isCreated => __value.isCreated;

            public bool isVail
            {
                get
                {
                    __CheckRead();

                    return __value.isVail;
                }
            }

            public int position
            {
                get
                {
                    __CheckRead();

                    return __value.position;
                }
            }

            public int length
            {
                get
                {
                    __CheckRead();

                    return __value.length;
                }
            }

            public unsafe Reader(ref NativeBuffer buffer)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                //AtomicSafetyHandle.CheckReadAndThrow(buffer.m_Safety);
                m_Safety = buffer.m_Safety;
                //AtomicSafetyHandle.UseSecondaryVersion(ref m_Safety);
#endif

                __value = new UnsafeBuffer.Reader(ref UnsafeUtility.AsRef<UnsafeBuffer>(buffer.__value));
            }

            public unsafe void* Read(int length)
            {
                __CheckWrite();

                return __value.Read(length);
            }

            public T Read<T>() where T : struct
            {
                __CheckWrite();

                return __value.Read<T>();
            }

            public NativeArray<T> ReadArray<T>(int length) where T : struct
            {
                __CheckWrite();

                var array = __value.ReadArray<T>(length);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, m_Safety);
#endif

                return array;
            }

            public UnsafeBlock ReadBlock(int length)
            {
                __CheckWrite();

                return __value.ReadBlock(length);
            }

            public string ReadString()
            {
                __CheckWrite();

                return __value.ReadString();
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void __CheckRead()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void __CheckWrite()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif

            }
        }

        [NativeContainer]
        public struct Writer : INativeWriter
        {
            private UnsafeBuffer.Writer __value;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;

            internal static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<Writer>();
#endif

            public bool isCreated => __value.isCreated;

            public int position
            {
                get
                {
                    __CheckRead();

                    return __value.position;
                }
            }

            public unsafe Writer(ref NativeBuffer buffer)
            {

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = buffer.m_Safety;

                CollectionHelper.SetStaticSafetyId<Writer>(ref m_Safety, ref StaticSafetyID.Data);
#endif

                __value = new UnsafeBuffer.Writer(ref UnsafeUtility.AsRef<UnsafeBuffer>(buffer.__value));
            }

            public void Write(string value)
            {
                __CheckWrite();

                __value.Write(value);
            }

            public unsafe void Write(void* ptr, int length)
            {

                __CheckWrite();

                __value.Write(ptr, length);
            }

            public UnsafeBlock WriteBlock(int length, bool isClear)
            {
                __CheckWrite();

                return __value.WriteBlock(length, isClear);
            }

            public NativeArray<T> WriteArray<T>(int length, NativeArrayOptions options) where T : struct
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckGetSecondaryDataPointerAndThrow(m_Safety);
#endif

                var array = WriteBlock(UnsafeUtility.SizeOf<T>() * length, options == NativeArrayOptions.ClearMemory).AsArray<T>();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                var arraySafety = m_Safety;
                AtomicSafetyHandle.UseSecondaryVersion(ref arraySafety);

                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, arraySafety);
#endif

                return array;
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void __CheckRead()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void __CheckWrite()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif

            }
        }

        [NativeContainer]
        [NativeContainerIsAtomicWriteOnly]
        public struct ParallelWriter : INativeWriter
        {
            private UnsafeBuffer.ParallelWriter __value;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;

            internal static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<ParallelWriter>();
#endif

            public bool isCreated => __value.isCreated;

            public unsafe ParallelWriter(ref NativeBuffer buffer)
            {

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                //AtomicSafetyHandle.CheckWriteAndThrow(buffer.m_Safety);
                m_Safety = buffer.m_Safety;

                CollectionHelper.SetStaticSafetyId<ParallelWriter>(ref m_Safety, ref StaticSafetyID.Data);
#endif

                __value = new UnsafeBuffer.ParallelWriter(ref UnsafeUtility.AsRef<UnsafeBuffer>(buffer.__value));
            }

            public unsafe void Write(void* ptr, int length)
            {
                __CheckWrite();

                __value.Write(ptr, length);
            }

            public UnsafeBlock WriteBlock(int length, bool isClear)
            {
                __CheckWrite();

                return __value.WriteBlock(length, isClear);
            }

            public NativeArray<T> WriteArray<T>(int length, NativeArrayOptions options) where T : struct
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckGetSecondaryDataPointerAndThrow(m_Safety);
#endif

                var array = WriteBlock(UnsafeUtility.SizeOf<T>() * length, options == NativeArrayOptions.ClearMemory).AsArray<T>();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                var arraySafety = m_Safety;
                AtomicSafetyHandle.UseSecondaryVersion(ref arraySafety);

                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, arraySafety);
#endif

                return array;
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void __CheckWrite()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif

            }
        }

        [NativeDisableUnsafePtrRestriction]
        private unsafe UnsafeBuffer* __value;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif

        public unsafe bool isCreated => __value != null;

        public unsafe int capacity
        {
            get
            {
                __CheckRead();

                return __value->capacity;
            }

            set
            {

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif

                __value->capacity = value;
            }
        }

        public unsafe int length
        {
            get
            {
                __CheckRead();

                return __value->length;
            }

            set
            {
                __CheckWrite();

                __value->length = value;
            }
        }

        public unsafe int position
        {
            get
            {
                __CheckRead();

                return __value->position;
            }

            set
            {
                __CheckWrite();

                __value->position = value;
            }
        }

        public Reader reader
        {
            get
            {
                return new Reader(ref this);
            }
        }

        public Writer writer
        {
            get
            {
                return new Writer(ref this);
            }
        }

        public ParallelWriter parallelWriter
        {
            get
            {
                return new ParallelWriter(ref this);
            }
        }

        public unsafe NativeBuffer(in AllocatorManager.AllocatorHandle allocator, int alignment, int initialCapacity = 0)
        {
            __value = AllocatorManager.Allocate<UnsafeBuffer>(allocator);

            *__value = new UnsafeBuffer(initialCapacity, alignment, allocator);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = CollectionHelper.CreateSafetyHandle(allocator.Handle);
#endif
        }

        public void Reset()
        {
            position = 0;
        }

        public unsafe void Dispose()
        {
            // Let the dispose sentinel know that the data has been freed so it does not report any memory leaks
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            CollectionHelper.DisposeSafetyHandle(ref m_Safety);
#endif

            var allocator = __value->allocator;

            __value->Dispose();

            AllocatorManager.Free(allocator, __value);

            __value = null;
        }

        public unsafe NativeArray<T> AsArray<T>(int byteOffset, int length) where T : struct
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckGetSecondaryDataPointerAndThrow(m_Safety);
#endif

            void* ptr = GetRangePtr(out int size);

            UnsafeBlock._CheckRange<T>(byteOffset, length, size);

            if (length < 0)
                length = (size - byteOffset) / UnsafeUtility.SizeOf<T>();

            var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>((byte*)ptr + byteOffset, length, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var arraySafety = m_Safety;

            AtomicSafetyHandle.UseSecondaryVersion(ref arraySafety);
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, arraySafety);
#endif

            return array;
        }


        public unsafe void* GetRangePtr(out int size)
        {
            //__CheckWrite();

            return __value->GetRangePtr(out size);
        }

        public unsafe byte[] ToBytes()
        {
            __CheckRead();

            return __value->ToBytes();
        }


        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void __CheckRead()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void __CheckWrite()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif

        }
    }

    public static class NativeBufferUtility
    {
        public unsafe static UnsafeBlock WriteBlock<T>(ref this T writer, in UnsafeBlock block) where T : struct, INativeWriter
        {
            void* ptr = block.GetRangePtr(out int size);

            var result = writer.WriteBlock(size, false);

            result.CopyFrom(ptr);

            return result;
        }

        public static UnsafeBlock<TValue> WriteBlock<TWriter, TValue>(ref this TWriter writer, TValue value)
            where TWriter : struct, INativeWriter
            where TValue : struct
        {
            var result = new UnsafeBlock<TValue>(writer.WriteBlock(UnsafeUtility.SizeOf<TValue>(), false));
            result.value = value;
            return result;
        }

        public static unsafe void WriteBuffer<TWriter, TValue>(ref this TWriter writer, in TValue value)
            where TWriter : struct, IUnsafeWriter
            where TValue : struct, IUnsafeBuffer 
        {
            void* ptr = value.GetRangePtr(out int size);

            writer.Write(ptr, size);
        }

        public static unsafe void Write<TWriter, TValue>(ref this TWriter writer, TValue value)
            where TWriter : struct, IUnsafeWriter
            where TValue : struct
        {
            writer.Write(UnsafeUtility.AddressOf(ref value), UnsafeUtility.SizeOf<TValue>());
        }

        public static unsafe void Write<TWriter, TValue>(ref this TWriter writer, in NativeSlice<TValue> values)
            where TWriter : struct, IUnsafeWriter
            where TValue : struct
        {
            writer.Write(values.GetUnsafeReadOnlyPtr(), values.Length * UnsafeUtility.SizeOf<TValue>());
        }

        public static unsafe void Write<TWriter, TValue>(ref this TWriter writer, in NativeArray<TValue> values)
            where TWriter : struct, IUnsafeWriter
            where TValue : struct => Write(ref writer, values.Slice());

        public static unsafe void Write<TWriter, TValue>(ref this TWriter writer, TValue[] values)
            where TWriter : struct, IUnsafeWriter
            where TValue : unmanaged
        {
            fixed (void* ptr = values)
            {
                writer.Write(ptr, UnsafeUtility.SizeOf<TValue>() * values.Length);
            }
        }
    }

    public static class NativeBufferUtilityEx
    {
        public static void Serialize<TWriter, TKey, TValue>(ref this TWriter writer, in UnsafeHashMap<TKey, TValue> hashMap)
            where TWriter : unmanaged, IUnsafeWriter
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            using (var keyValueArrays = hashMap.GetKeyValueArrays(Allocator.Temp))
            {
                writer.Write(keyValueArrays.Keys.Length);
                writer.Write(keyValueArrays.Keys);
                writer.Write(keyValueArrays.Values);
            }
        }

        public static void Serialize<TWriter, TKey, TValue>(ref this TWriter writer, in NativeParallelHashMap<TKey, TValue> hashMap)
            where TWriter : unmanaged, IUnsafeWriter
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            using (var keyValueArrays = hashMap.GetKeyValueArrays(Allocator.Temp))
            {
                writer.Write(keyValueArrays.Keys.Length);
                writer.Write(keyValueArrays.Keys);
                writer.Write(keyValueArrays.Values);
            }
        }

        public static void Serialize<TWriter, TKey, TValue>(ref this TWriter writer, in UnsafeParallelMultiHashMap<TKey, TValue> hashMap)
            where TWriter : unmanaged, IUnsafeWriter
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            using (var keyValueArrays = hashMap.GetKeyValueArrays(Allocator.Temp))
            {
                writer.Write(keyValueArrays.Keys.Length);
                writer.Write(keyValueArrays.Keys);
                writer.Write(keyValueArrays.Values);
            }
        }

        public static void Serialize<TWriter, TKey, TValue>(ref this TWriter writer, in NativeParallelMultiHashMap<TKey, TValue> hashMap)
            where TWriter : unmanaged, IUnsafeWriter
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            using (var keyValueArrays = hashMap.GetKeyValueArrays(Allocator.Temp))
            {
                writer.Write(keyValueArrays.Keys.Length);
                writer.Write(keyValueArrays.Keys);
                writer.Write(keyValueArrays.Values);
            }
        }

        public static void Serialize<TWriter, TValue>(ref this TWriter writer, in UnsafePool<TValue> pool)
            where TWriter : unmanaged, IUnsafeWriter
            where TValue : unmanaged
        {
            int capacity = pool.length;
            for (int i = 0; i < capacity; ++i)
            {
                if (!pool.TryGetValue(i, out var item))
                    continue;

                writer.Write(i);
                writer.Write(item);
            }

            writer.Write(-1);
        }

        public static void Serialize<TWriter, TValue>(ref this TWriter writer, in NativePool<TValue> pool)
            where TWriter : unmanaged, IUnsafeWriter
            where TValue : unmanaged
        {
            int capacity = pool.length;
            for (int i = 0; i < capacity; ++i)
            {
                if (!pool.TryGetValue(i, out var item))
                    continue;

                writer.Write(i);
                writer.Write(item);
            }

            writer.Write(-1);
        }

        public static void Deserialize<TReader, TKey, TValue>(ref this TReader reader, ref UnsafeHashMap<TKey, TValue> hashMap)
            where TReader : struct, IUnsafeReader
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            hashMap.Clear();

            int length = reader.Read<int>();
            if (length > 0)
            {
                hashMap.Capacity = math.max(hashMap.Capacity, length);

                var keys = reader.ReadArray<TKey>(length);
                var values = reader.ReadArray<TValue>(length);
                for (int i = 0; i < length; ++i)
                    hashMap.Add(keys[i], values[i]);
            }
        }

        public static void Deserialize<TReader, TKey, TValue>(ref this TReader reader, ref NativeParallelHashMap<TKey, TValue> hashMap)
            where TReader : struct, IUnsafeReader
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            hashMap.Clear();

            int length = reader.Read<int>();
            if (length > 0)
            {
                hashMap.Capacity = math.max(hashMap.Capacity, length);

                var keys = reader.ReadArray<TKey>(length);
                var values = reader.ReadArray<TValue>(length);
                for (int i = 0; i < length; ++i)
                    hashMap.Add(keys[i], values[i]);
            }
        }

        public static void Deserialize<TReader, TKey, TValue>(ref this TReader reader, ref UnsafeParallelMultiHashMap<TKey, TValue> hashMap)
            where TReader : struct, IUnsafeReader
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            hashMap.Clear();

            int length = reader.Read<int>();
            if (length > 0)
            {
                hashMap.Capacity = math.max(hashMap.Capacity, length);

                var keys = reader.ReadArray<TKey>(length);
                var values = reader.ReadArray<TValue>(length);
                for (int i = 0; i < length; ++i)
                    hashMap.Add(keys[i], values[i]);
            }
        }

        public static void Deserialize<TReader, TKey, TValue>(ref this TReader reader, ref NativeParallelMultiHashMap<TKey, TValue> hashMap)
            where TReader : struct, IUnsafeReader
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            hashMap.Clear();

            int length = reader.Read<int>();
            if (length > 0)
            {
                hashMap.Capacity = math.max(hashMap.Capacity, length);

                var keys = reader.ReadArray<TKey>(length);
                var values = reader.ReadArray<TValue>(length);
                for (int i = 0; i < length; ++i)
                    hashMap.Add(keys[i], values[i]);
            }
        }

        public static void Deserialize<TReader, TValue>(ref this TReader reader, ref UnsafePool<TValue> pool)
            where TReader : unmanaged, IUnsafeReader
            where TValue : unmanaged
        {
            pool.Clear();

            int index;
            while (true)
            {
                index = reader.Read<int>();
                if (index < 0)
                    break;

                pool.Insert(index, reader.Read<TValue>());
            }
        }
        public static void Deserialize<TReader, TValue>(ref this TReader reader, ref NativePool<TValue> pool)
            where TReader : unmanaged, IUnsafeReader
            where TValue : unmanaged
        {
            pool.Clear();

            int index;
            while (true)
            {
                index = reader.Read<int>();
                if (index < 0)
                    break;

                pool.Insert(index, reader.Read<TValue>());
            }
        }
    }
}