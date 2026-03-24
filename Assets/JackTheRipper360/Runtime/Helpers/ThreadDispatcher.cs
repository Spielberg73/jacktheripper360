#if UNITY_EDITOR || UNITY_STANDALONE
using System;
using System.Collections.Generic;
using UnityEngine;

namespace JackTheRipper360.Runtime.Helpers
{
    /// <summary>
    /// Dispatches actions from background threads to Unity's main thread.
    /// </summary>
    public class ThreadDispatcher : MonoBehaviour
    {
        private static ThreadDispatcher _instance;
        private static readonly Queue<Action> _actionQueue = new Queue<Action>();
        private static readonly object _lock = new object();

        public static ThreadDispatcher Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("ThreadDispatcher");
                    go.hideFlags = HideFlags.HideAndDontSave;
                    _instance = go.AddComponent<ThreadDispatcher>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        /// <summary>
        /// Queue an action to run on the main thread.
        /// </summary>
        public static void RunOnMainThread(Action action)
        {
            lock (_lock)
            {
                _actionQueue.Enqueue(action);
            }
        }

        void Update()
        {
            lock (_lock)
            {
                while (_actionQueue.Count > 0)
                {
                    var action = _actionQueue.Dequeue();
                    try
                    {
                        action?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"ThreadDispatcher error: {ex.Message}");
                    }
                }
            }
        }
    }
}
#endif
