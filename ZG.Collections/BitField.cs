using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Math = ZG.Mathematics.Math;

namespace ZG
{
    /*public unsafe struct BitTable
    {
        public static readonly BitTable Instance;

        //[NativeDisableUnsafePtrRestriction]
        private fixed byte __highestBitTable[byte.MaxValue];

        static unsafe BitTable()
        {
            BitTable bitTable;
            byte highestBit = 0;
            for (byte i = byte.MinValue; i < byte.MaxValue; ++i)
            {
                switch (i)
                {
                    case 0x01:
                    case 0x02:
                    case 0x04:
                    case 0x08:
                    case 0x10:
                    case 0x20:
                    case 0x40:
                    case 0x80:
                        ++highestBit;
                        break;
                }

                bitTable.__highestBitTable[i] = highestBit;
            }

            bitTable.__highestBitTable[byte.MaxValue] = highestBit;

            Instance = bitTable;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetHighestBit(byte value)
        {
            return __highestBitTable[value];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetHighestBit(ushort value)
        {
            int mask = value >> 8;
            return mask == 0 ? __highestBitTable[value] : 8 + __highestBitTable[mask];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetHighestBit(uint value)
        {
            uint mask = value >> 24;
            int result;
            if (mask == 0)
            {
                mask = value >> 16;
                if (mask == 0)
                {
                    mask = value >> 8;

                    result = mask == 0 ? __highestBitTable[value] : 8 + __highestBitTable[mask];
                }
                else
                    result = 16 + __highestBitTable[mask];
            }
            else
                result = 24 + __highestBitTable[mask];

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetLowerstBit(byte value)
        {
            return __highestBitTable[(value - 1) ^ value];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetLowerstBit(ushort value)
        {
            return GetHighestBit((ushort)((value - 1) ^ value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetLowerstBit(uint value)
        {
            return GetHighestBit((value - 1) ^ value);
        }
    }*/

    public struct BitField<T> : System.IEquatable<BitField<T>> where T : struct
    {
        public static readonly BitField<T> Max = CreateMax();

        public T value;

        public unsafe int lowerstBit
        {
            get
            {
                byte temp;
                byte* value = (byte*)UnsafeUtility.AddressOf(ref this.value);
                int size = UnsafeUtility.SizeOf<T>();
                for (int i = 0; i < size; ++i)
                {
                    temp = value[i];
                    if (temp == 0)
                        continue;

                    return Math.GetLowerstBit(temp) + (i << 3);
                }

                return 0;
            }
        }

        public unsafe int highestBit
        {
            get
            {
                byte temp;
                byte* value = (byte*)UnsafeUtility.AddressOf(ref this.value);
                int size = UnsafeUtility.SizeOf<T>();
                for (int i = size - 1; i >= 0; --i)
                {
                    temp = value[i];
                    if (temp == 0)
                        continue;

                    return Math.GetHighestBit(temp) + (i << 3);
                }

                return 0;
            }
        }


        public unsafe int GetHighestBit(int max)
        {
            byte temp;
            byte* value = (byte*)UnsafeUtility.AddressOf(ref this.value);
            int size = math.min(UnsafeUtility.SizeOf<T>(),  (max >> 3) + 1);
            for (int i = size - 1; i >= 0; --i)
            {
                temp = value[i];
                if (temp == 0)
                    continue;

                return math.min(Math.GetHighestBit(temp) + (i << 3), max);
            }

            return 0;
        }

        public static unsafe BitField<T> operator|(BitField<T> x, BitField<T> y)
        {
            BitField<T> result = x;
            byte* xValue = (byte*)UnsafeUtility.AddressOf(ref result.value), 
                yValue = (byte*)UnsafeUtility.AddressOf(ref y.value);
            int size = UnsafeUtility.SizeOf<T>();
            for (int i = 0; i < size; ++i)
                xValue[i] |= yValue[i];

            return result;
        }

        public static unsafe BitField<T> operator &(BitField<T> x, BitField<T> y)
        {
            BitField<T> result = x;
            byte* xValue = (byte*)UnsafeUtility.AddressOf(ref result.value),
                yValue = (byte*)UnsafeUtility.AddressOf(ref y.value);
            int size = UnsafeUtility.SizeOf<T>();
            for (int i = 0; i < size; ++i)
                xValue[i] &= yValue[i];

            return result;
        }

        public static unsafe BitField<T> operator ^(BitField<T> x, BitField<T> y)
        {
            BitField<T> result = x;
            byte* xValue = (byte*)UnsafeUtility.AddressOf(ref result.value),
                yValue = (byte*)UnsafeUtility.AddressOf(ref y.value);
            int size = UnsafeUtility.SizeOf<T>();
            for (int i = 0; i < size; ++i)
                xValue[i] ^= yValue[i];

            return result;
        }

        public static unsafe bool operator ==(BitField<T> x, BitField<T> y)
        {
            return UnsafeUtility.MemCmp(
                UnsafeUtility.AddressOf(ref x.value),
                UnsafeUtility.AddressOf(ref y.value),
                UnsafeUtility.SizeOf<T>()) == 0;
        }

        public static unsafe bool operator !=(BitField<T> x, BitField<T> y)
        {
            return UnsafeUtility.MemCmp(
                UnsafeUtility.AddressOf(ref x.value),
                UnsafeUtility.AddressOf(ref y.value),
                UnsafeUtility.SizeOf<T>()) != 0;
        }

        public static unsafe BitField<T> Create<U>(U value) where U : struct
        {
            BitField<T> result = default;
            UnsafeUtility.MemCpy(
                UnsafeUtility.AddressOf(ref result.value),
                UnsafeUtility.AddressOf(ref value), 
                math.min(UnsafeUtility.SizeOf<T>(), UnsafeUtility.SizeOf<U>()));

            return result;
        }

        public static unsafe BitField<T> CreateMax()
        {
            BitField<T> result = default;
            byte* value = (byte*)UnsafeUtility.AddressOf(ref result.value); 
            int size = UnsafeUtility.SizeOf<T>();
            for (int i = 0; i < size; ++i)
                value[i] = byte.MaxValue;

            return result;
        }

        public BitField(in T value)
        {
            this.value = value;
        }

        public void SetMax()
        {
            this = Max;
        }

        public unsafe bool Test(int index)
        {
            ref var temp = ref __Get(ref index);

            return (temp & (byte)(1 << index)) != 0;
        }

        public unsafe void Set(int index)
        {
            ref var temp = ref __Get(ref index);

            temp |= (byte)(1 << index);
        }

        public unsafe bool TrySet(int index)
        {
            ref var temp = ref __Get(ref index);

            bool result = (temp & (1 << index)) == 0;

            if(result)
                temp |= (byte)(1 << index);

            return result;
        }

        public unsafe void Unset(int index)
        {
            ref var temp = ref __Get(ref index);

            temp &= (byte)~(1 << index);
        }

        public unsafe bool TryUnset(int index)
        {
            ref var temp = ref __Get(ref index);

            bool result = (temp & (1 << index)) != 0;

            if(result)
                temp &= (byte)~(1 << index);

            return result;
        }

        public bool Equals(BitField<T> other)
        {
            return this == other;
        }

        public override bool Equals(object obj)
        {
            return Equals((BitField<T>)obj);
        }

        public unsafe override int GetHashCode()
        {
            return (int)math.hash(UnsafeUtility.AddressOf(ref value), UnsafeUtility.SizeOf<T>());
        }

        private unsafe ref byte __Get(ref int index)
        {
            UnityEngine.Assertions.Assert.IsFalse(index < 0);

            int byteIndex = index >> 3;

            UnityEngine.Assertions.Assert.IsTrue(byteIndex < UnsafeUtility.SizeOf<T>());

            ref byte temp = ref ((byte*)UnsafeUtility.AddressOf(ref value))[byteIndex];

            index &= 0x7;

            return ref temp;
        }
    }
}