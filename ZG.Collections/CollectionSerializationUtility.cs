using System;
using System.IO;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ZG
{
    public static class CollectionSerializationUtility
    {
        public static void Serialize<T>(this BinaryWriter writer, NativeArray<T> value, int length = 0, int startIndex = 0) where T : struct
        {
            var bytes = value.Slice(startIndex, length > 0 ? length : value.Length - startIndex).SliceConvert<byte>().ToArray();
            int numBytes = bytes.Length;
            writer.Write(numBytes);
            writer.Write(bytes, 0, numBytes);
        }

        public static void Serialize<TKey, TValue>(this BinaryWriter writer, NativeParallelMultiHashMap<TKey, TValue> value)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            using (var keys = value.GetKeyArray(Allocator.Temp))
            {
                int length = keys.ConvertToUniqueArray();
                Serialize(writer, keys, length);

                using (var items = new NativeList<TValue>(Allocator.Temp))
                {
                    TValue item;
                    NativeParallelMultiHashMapIterator<TKey> iterator;
                    for(int i = 0; i < length; ++i)
                    {
                        items.Clear();

                        if (value.TryGetFirstValue(keys[i], out item, out iterator))
                        {
                            do
                            {
                                items.Add(item);
                            } while (value.TryGetNextValue(out item, ref iterator));
                        }

                        Serialize(writer, items.AsArray());
                    }
                }
            }
        }

        public static unsafe NativeArray<T> DeserializeNativeArray<T>(this BinaryReader reader, Allocator allocator) where T : struct
        {
            int count = reader.ReadInt32(), length = count * UnsafeUtility.SizeOf<byte>();
            var result = new NativeArray<T>(length / UnsafeUtility.SizeOf<T>(), allocator, NativeArrayOptions.UninitializedMemory);
            fixed (void* bytes = reader.ReadBytes(count))
            {
                UnsafeUtility.MemCpy(NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(result), bytes, length);
            }

            return result;
        }

        public static NativeParallelMultiHashMap<TKey, TValue> DeserializeNativeMultiHashMap<TKey, TValue>(this BinaryReader reader, Allocator allocator)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            using (var keys = DeserializeNativeArray<TKey>(reader, Allocator.Temp))
            {
                NativeParallelMultiHashMap<TKey, TValue> result = new NativeParallelMultiHashMap<TKey, TValue>(keys.Length, allocator);

                foreach (var key in keys)
                {
                    using (var values = DeserializeNativeArray<TValue>(reader, Allocator.Temp))
                    {
                        foreach (var value in values)
                            result.Add(key, value);
                    }
                }

                return result;
            }
        }
    }
}