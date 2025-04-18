using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Vapor
{
    //TODO: Add this into custom player loop update.
    public class CallbackTimer : MonoBehaviour
    {
        public static long CurrentTick { get; protected set; }
        public delegate void DoneHandler(bool isSuccessful);

        private static CallbackTimer _instance;
        public static CallbackTimer Instance
        {
            get
            {
                if (_instance != null || !Application.isPlaying) return _instance;
                
                var go = new GameObject("CallbackTimer");
                _instance = go.AddComponent<CallbackTimer>();
                return _instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            _instance = null;
        }

        private List<Action> _mainThreadActions;

        /// <summary>
        /// Event, which is invoked every second
        /// </summary>
        public event Action<long> OnTick;

        private readonly object _mainThreadLock = new();

        private readonly WaitForSeconds _wfs = new(1);

        private void Awake()
        {
            Application.runInBackground = true;

            lock (_mainThreadLock)
            {
                _mainThreadActions = new List<Action>();
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            StartCoroutine(StartTicker());
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
        }

        private void Update()
        {
            if (_mainThreadActions.Count <= 0) return;
            
            lock (_mainThreadLock)
            {
                foreach (var actions in _mainThreadActions)
                {
                    actions.Invoke();
                }

                _mainThreadActions.Clear();
            }
        }

        /// <summary>
        ///     Waits while condition is false
        ///     If timed out, callback will be invoked with false
        /// </summary>
        /// <param name="condition"></param>
        /// <param name="doneCallback"></param>
        /// <param name="timeoutSeconds"></param>
        public static void WaitUntil(Func<bool> condition, DoneHandler doneCallback, float timeoutSeconds)
        {
            Instance.StartCoroutine(WaitWhileTrueCoroutine(condition, doneCallback, timeoutSeconds, true));
        }

        /// <summary>
        ///     Waits while condition is true
        ///     If timed out, callback will be invoked with false
        /// </summary>
        /// <param name="condition"></param>
        /// <param name="doneCallback"></param>
        /// <param name="timeoutSeconds"></param>
        public static void WaitWhile(Func<bool> condition, DoneHandler doneCallback, float timeoutSeconds)
        {
            Instance.StartCoroutine(WaitWhileTrueCoroutine(condition, doneCallback, timeoutSeconds));
        }

        private static IEnumerator WaitWhileTrueCoroutine(Func<bool> condition, DoneHandler callback, float timeoutSeconds, bool reverseCondition = false)
        {
            while ((timeoutSeconds > 0) && (condition.Invoke() == !reverseCondition))
            {
                timeoutSeconds -= Time.deltaTime;
                yield return null;
            }

            callback.Invoke(timeoutSeconds > 0);
        }

        /// <summary>
        /// Invokes callback after waiting set number seconds.
        /// </summary>
        /// <param name="time"></param>
        /// <param name="callback"></param>
        public static void AfterSeconds(float time, Action callback)
        {
            Instance.StartCoroutine(StartWaitingSeconds(time, callback));
        }

        /// <summary>
        ///     Executes once on the update call of the main thread.
        /// </summary>
        /// <param name="action"></param>
        public static void ExecuteOnMainThread(Action action)
        {
            Instance.OnMainThread(action);
        }

        public void OnMainThread(Action action)
        {
            lock (_mainThreadLock)
            {
                _mainThreadActions.Add(action);
            }
        }

        private static IEnumerator StartWaitingSeconds(float time, Action callback)
        {
            yield return new WaitForSeconds(time);
            callback?.Invoke();
        }

        private IEnumerator StartTicker()
        {
            CurrentTick = 0;
            while (true)
            {
                yield return _wfs;
                CurrentTick++;
                OnTick?.Invoke(CurrentTick);
            }
            // ReSharper disable once IteratorNeverReturns
        }
    }
}