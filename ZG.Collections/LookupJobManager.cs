using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;

namespace ZG
{
    public interface IReadOnlyLookupJobManager
    {
        JobHandle readOnlyJobHandle { get; }

        void AddReadOnlyDependency(in JobHandle inputDeps);
    }

    public interface ILookupJobManager : IReadOnlyLookupJobManager
    {
        JobHandle readWriteJobHandle { get; set; }
    }

    public struct LookupJobManager : ILookupJobManager
    {
        private JobHandle __dependency;
        private JobHandle __jobHandle;

        public JobHandle readOnlyJobHandle
        {
            get => __jobHandle;
        }

        public JobHandle readWriteJobHandle
        {
            get => JobHandle.CombineDependencies(__dependency, __jobHandle);

            set
            {
                __dependency = default;
                __jobHandle = value;
            }
        }

        public void CompleteReadOnlyDependency()
        {
            __jobHandle.Complete();
            __jobHandle = default;
        }

        public void CompleteReadWriteDependency()
        {
            JobHandle.CompleteAll(ref __dependency, ref __jobHandle);
        }

        public void AddReadOnlyDependency(in JobHandle inputDeps)
        {
            __dependency = JobHandle.CombineDependencies(__dependency, inputDeps);
        }
    }
}