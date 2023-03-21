using Unity.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Jobs
{
    public static class JobParallelForDeferUtility
    {
        public static unsafe JobHandle ScheduleByRef<T, U>(
            this ref T jobData, 
            ref UnsafeList<U> list,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            in AtomicSafetyHandle safety,
#endif
            int innerloopBatchCount,
            JobHandle dependsOn = new JobHandle())
                where T : struct, IJobParallelForDefer
                where U : unmanaged
        {
            NativeList<U> result = default;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            result.m_Safety = safety;
#endif

            result.m_ListData = (UnsafeList<U>*)UnsafeUtility.AddressOf(ref list);
            return jobData.ScheduleByRef(result, innerloopBatchCount, dependsOn);
        }
    }
}