using System;
using System.Diagnostics;
using Unity.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Burst;
using UnityEngine;

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

        public override string ToString()
        {
            return $"({instanceID} : {index})";
        }
    }

    public struct SingletonAssetContainer<T> where T : unmanaged
    {
        internal struct Data
        {
            private int __count;
            private UnsafeHashMap<int, JobHandle> __jobHandles;
            internal UnsafeHashMap<SingletonAssetContainerHandle, T> _values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            public T this[in SingletonAssetContainerHandle handle]
            {
                get
                {
                    __CheckRead();

                    __CheckHandle(handle);

                    return _values[handle];
                }

                set
                {
                    CompleteDependency();

                    __CheckWrite();

/*#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    if (_values.TryGetValue(handle, out var oldValue))
                        throw new InvalidOperationException($"{handle} : {oldValue} To {value}");
#endif*/

                    _values[handle] = value;

                    //UnityEngine.Debug.LogError($"Write {handle} : {value} To {this}");
                }
            }

            public Data(in AllocatorManager.AllocatorHandle allocator)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = CollectionHelper.CreateSafetyHandle(allocator);
#endif

                __count = 0;
                __jobHandles = new UnsafeHashMap<int, JobHandle>(1, allocator);
                _values = new UnsafeHashMap<SingletonAssetContainerHandle, T>(1, allocator);
            }

            public void Dispose()
            {
                CompleteDependency();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                CollectionHelper.DisposeSafetyHandle(ref m_Safety);
#endif

                __jobHandles.Dispose();
                _values.Dispose();
            }

            public bool Release()
            {
                if (--__count < 1)
                {
                    Dispose();

                    return true;
                }

                return false;
            }

            public void Retain()
            {
                ++__count;
            }

            public bool Delete(in SingletonAssetContainerHandle handle)
            {
                //UnityEngine.Debug.LogError($"Delete {handle} From {this}");

                CompleteDependency();

                __CheckWrite();

                /*if (!__values.TryGetValue(handle, out var value))
                    return false;

                value.Dispose();*/

                return _values.Remove(handle);
            }

            public bool CompleteDependency()
            {
                if (__jobHandles.IsEmpty)
                    return false;

                foreach (var jobHandle in __jobHandles)
                    jobHandle.Value.Complete();

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
                if (!_values.ContainsKey(handle))
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

        [NativeContainer]
        [NativeContainerIsReadOnly]
        public struct Reader
        {
            private UnsafeHashMap<SingletonAssetContainerHandle, T> __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;

            internal static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<Reader>();
#endif

            public T this[in SingletonAssetContainerHandle handle]
            {
                get
                {
                    //UnityEngine.Debug.LogError($"Read {handle} : {__values[handle]} From {this}");

                    __CheckRead();

                    __CheckHandle(handle);

                    return __values[handle];
                }
            }

            public unsafe Reader(ref SingletonAssetContainer<T> container)
            {
                __values = container.__data->_values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = container.__data->m_Safety;

                CollectionHelper.SetStaticSafetyId<Reader>(ref m_Safety, ref StaticSafetyID.Data);
#endif
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

        }

        private unsafe Data* __data;

        private static readonly SharedStatic<IntPtr> Instance = SharedStatic<IntPtr>.GetOrCreate<SingletonAssetContainer<T>>();

        public unsafe bool isCreated => __data != null;

        public unsafe T this[in SingletonAssetContainerHandle handle]
        {
            get
            {
                return (*__data)[handle];
            }

            set
            {
                (*__data)[handle] = value;
            }
        }

        public Reader reader => new Reader(ref this);

        public unsafe static SingletonAssetContainer<T> instance
        {
            get
            {
                SingletonAssetContainer<T> instance;
                instance.__data = (Data*)(void*)Instance.Data;
                if (instance.__data == null)
                {
                    instance.__data = AllocatorManager.Allocate<Data>(Allocator.Persistent);
                    *instance.__data = new Data(Allocator.Persistent);

                    Instance.Data = (IntPtr)instance.__data;
                }

                return instance;
            }
        }

        public unsafe static SingletonAssetContainer<T> Retain()
        {
            var instance = SingletonAssetContainer<T>.instance;

            instance.__data->Retain();

            return instance;
        }

        public unsafe void Release()
        {
            if (__data->Release())
            {
                AllocatorManager.Free(Allocator.Persistent, __data);

                __data = null;

                Instance.Data = IntPtr.Zero;
            }
        }

        /*static SingletonAssetContainer()
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
                enumerator.Current.Value.Dispose();

            instance.__values.Dispose();
        }*/

        public unsafe bool Delete(in SingletonAssetContainerHandle handle)
        {
            return __data->Delete(handle);
        }

        public unsafe bool CompleteDependency()
        {
            return __data->CompleteDependency();
        }

        public unsafe void AddDependency(int id, in JobHandle jobHandle)
        {
            __data->AddDependency(id, jobHandle);
        }

    }
}
