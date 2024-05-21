using System;
using System.Reflection;
using UnityEngine;

namespace ZG
{
    [AttributeUsage(AttributeTargets.Method)]
    public class RuntimeDisposeAttribute : Attribute
    {
        
    }
    
    public static class RuntimeUtility
    {
        [ExecuteAlways]
        private sealed class Runtime : MonoBehaviour
        {
            public event Action onDispose;

            public bool isActive;

            public void OnEnable()
            {
                if (!isActive)
                    return;

                isActive = false;
                DestroyImmediate(gameObject);
            }

            public void OnDisable()
            {
                if (isActive)
                {
                    if (onDispose != null)
                        onDispose.Invoke();
                }
            }
        }

        private static Runtime __runtime;

        public static event Action onDispose
        {
            add
            {
                if (__runtime == null)
                {
                    var go = new GameObject { hideFlags = HideFlags.HideInHierarchy };
                    if (Application.isPlaying)
                        UnityEngine.Object.DontDestroyOnLoad(go);
                    else
                        go.hideFlags = HideFlags.HideAndDontSave;

                    __runtime = go.AddComponent<Runtime>();
                    __runtime.isActive = true;
                }

                __runtime.onDispose += value;
            }

            remove
            {
                if (__runtime == null)
                    return;

                __runtime.onDispose -= value;
            }
        }
        
#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Init()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in assembly.GetTypes())
                {
                    foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (method.IsDefined(typeof(RuntimeDisposeAttribute)))
                            onDispose += (Action)Delegate.CreateDelegate(typeof(Action), method);
                    }
                }
            }
        }
#endif
    }
}
