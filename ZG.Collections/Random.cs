using System;
using Unity.Collections;
using Random = Unity.Mathematics.Random;

namespace ZG
{
    public enum RandomResult
    {
        Fail,
        Success, 
        Pass
    }

    [Flags]
    public enum RandomFlag
    {
        /// <summary>
        /// 从列表里选一个
        /// </summary>
        Selection = 0x01, 
        /// <summary>
        /// 每个都单独判断概率
        /// </summary>
        Single = 0x02
    }

    [Serializable]
    public struct RandomGroup
    {
        [Mask]
        public RandomFlag flag;

        public int startIndex;
        public int count;

        public float chance;
    }

    public interface IRandomItemHandler
    {
        RandomResult Set(int startIndex, int count);
    }
    
    public static class RandomUtility
    {
        public static RandomResult Next<T>(ref this Random random, ref T itemHandler, in NativeSlice<RandomGroup> groups) where T : struct, IRandomItemHandler
        {
            var result = RandomResult.Fail;

            bool isSingle, isReset = true, isOverload = false;
            int length = groups.Length;
            float value = 0.0f, chance = 0.0f;
            RandomGroup group;
            for (int i = 0; i < length; ++i)
            {
                group = groups[i];
                isSingle = (group.flag & RandomFlag.Single) == RandomFlag.Single;
                if (isSingle)
                {
                    chance = 0.0f;

                    isReset = true;
                }

                chance += group.chance;
                if (chance > 1.0f)
                {
                    chance -= 1.0f;

                    isReset = true;
                }

                if (isReset)
                {
                    isReset = false;

                    isOverload = false;

                    value = random.NextFloat();
                }

                if (isOverload || chance < value)
                    continue;

                switch ((group.flag & RandomFlag.Selection) == RandomFlag.Selection ?
                    itemHandler.Set(group.startIndex + random.NextInt(0, group.count), 1) :
                    itemHandler.Set(group.startIndex, group.count))
                {
                    case RandomResult.Success:
                        return RandomResult.Success;
                    case RandomResult.Pass:
                        isOverload = !isSingle;

                        result = RandomResult.Pass;
                        break;
                    default:
                        break;
                }
            }

            return result;
        }
    }
}