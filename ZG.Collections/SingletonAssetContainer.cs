using System;
using System.Diagnostics;
using Unity.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ZG
{
    public struct SingletonAssetContainerHandle : IEquatable<SingletonAssetContainerHandle>
    {
        public int instanceID;
        public int index;

        public SingletonAssetContainerHandle(int instanceID, int index)
        {
            this.instanceID = instanceID;
            this.index = index;
        }

        public bool Equals(SingletonAssetContainerHandle other)
        {
            return instanceID == other.instanceID && index == other.index;
        }

        public override int GetHashCode()
        {
            return instanceID ^ index;
        }
    }

    public struct SingletonAssetContainer<T> where T : unmanaged
    {
        [NativeContainer]
        [NativeContainerIsReadOnly]
        public struct Reader
        {
            private UnsafeParallelHashMap<SingletonAssetContainerHandle, T> __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            public T this[in SingletonAssetContainerHandle handle]
            {
                get
                {
                    CheckRead();

                    __CheckHandle(handle);

                    return __values[handle];
                }
            }

            public Reader(ref SingletonAssetContainer<T> container)
            {
                __values = container.__values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = container.m_Safety;
#endif
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private void __CheckHandle(in SingletonAssetContainerHandle handle)
            {
                if (!__values.ContainsKey(handle))
                    throw new IndexOutOfRangeException();
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckRead()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            }
        }

        private UnsafeParallelHashMap<int, JobHandle> __jobHandles;
        private UnsafeParallelHashMap<SingletonAssetContainerHandle, T> __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif

        public T this[in SingletonAssetContainerHandle handle]
        {
            get
            {
                __CheckRead();

                __CheckHandle(handle);

                return __values[handle];
            }

            set
            {
                CompleteDependency();

                __CheckWrite();

                __values[handle] = value;
            }
        }

        public Reader reader => new Reader(ref this);

        public static SingletonAssetContainer<T> instance
        {
            get;
        }

        static SingletonAssetContainer()
        {
            SingletonAssetContainer<T> container;
            container.__jobHandles = new UnsafeParallelHashMap<int, JobHandle>(1, Allocator.Persistent);
            container.__values = new UnsafeParallelHashMap<SingletonAssetContainerHandle, T>(1, Allocator.Persistent);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            container.m_Safety = AtomicSafetyHandle.Create();
#endif

            instance = container;

            AppDomain.CurrentDomain.DomainUnload += __OnDomainUnload;
        }

        private static void __OnDomainUnload(object sender, EventArgs e)
        {
            var instance = SingletonAssetContainer<T>.instance;
            instance.CompleteDependency();

            instance.__jobHandles.Dispose();

            /*var enumerator = instance.__values.GetEnumerator();
            while (enumerator.MoveNext())
                enumerator.Current.Value.Dispose();*/

            instance.__values.Dispose();
        }

        public bool Delete(in SingletonAssetContainerHandle handle)
        {
            CompleteDependency();

            __CheckWrite();

            /*if (!__values.TryGetValue(handle, out var value))
                return false;

            value.Dispose();*/

            return __values.Remove(handle);
        }

        public bool CompleteDependency()
        {
            if (__jobHandles.IsEmpty)
                return false;

            foreach(var jobHandle in __jobHandles)
            {
                jobHandle.Value.Complete();
            }

            /*using(var jobHandles = __jobHandles.GetValueArray(Allocator.Temp))
                JobHandle.CombineDependencies(jobHandles);*/

            __jobHandles.Clear();

            return true;
        }

        public void AddDependency(int id, in JobHandle jobHandle)
        {
            if (__jobHandles.TryGetValue(id, out var temp))
                temp.Complete();

            __jobHandles[id] = jobHandle;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void __CheckHandle(in SingletonAssetContainerHandle handle)
        {
            if (!__values.ContainsKey(handle))
                throw new IndexOutOfRangeException();
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void __CheckRead()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void __CheckWrite()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
        }
    }
}
