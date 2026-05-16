using System.Collections.Generic;
using UnityEngine;

namespace Vapor
{
    public class RootControllerRunner : MonoBehaviour    
    {
        private readonly List<IUIUpdateListener> _updateListeners = new();
        private readonly List<IUILateUpdateListener> _lateUpdateListeners = new();
        private readonly List<IUIFixedUpdateListener> _fixedUpdateListeners = new();

        public void RegisterUpdateListener(IUIUpdateListener listener)
        {
            if (!_updateListeners.Contains(listener))
            {
                _updateListeners.Add(listener);
            }
        }
        public void DeregisterUpdateListener(IUIUpdateListener listener)
        {
            _updateListeners.Remove(listener);
        }

        public void RegisterLateUpdateListener(IUILateUpdateListener listener)
        {
            if (!_lateUpdateListeners.Contains(listener))
            {
                _lateUpdateListeners.Add(listener);
            }
        }
        public void DeregisterLateUpdateListener(IUILateUpdateListener listener)
        {
            _lateUpdateListeners.Remove(listener);
        }

        public void RegisterFixedUpdateListener(IUIFixedUpdateListener listener)
        {
            if (!_fixedUpdateListeners.Contains(listener))
            {
                _fixedUpdateListeners.Add(listener);
            }
        }
        public void DeregisterFixedUpdateListener(IUIFixedUpdateListener listener)
        {
            _fixedUpdateListeners.Remove(listener);
        }

        private void Update()
        {
            foreach (var listener in _updateListeners)
            {
                listener.OnUpdate();
            }
        }

        private void LateUpdate()
        {
            foreach (var listener in _lateUpdateListeners)
            {
                listener.OnLateUpdate();
            }
        }

        private void FixedUpdate()
        {
            foreach (var listener in _fixedUpdateListeners)
            {
                listener.OnFixedUpdate();
            }
        }
    }
}