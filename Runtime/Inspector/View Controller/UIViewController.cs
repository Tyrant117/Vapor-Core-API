using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.Inspector
{
    public interface ISourceDataAction
    {
        Action<object> ModifyGraphDataAction { get; }
    }

    public interface ISourceDataStore<T> where T : class
    {
        T State { get; }
        event Action<T, ISourceDataAction> Subscribe;

        void Dispatch(ISourceDataAction changeAction);
    }

    public class DataStore<T> : ISourceDataStore<T> where T : class
    {
        public T State { get; private set; }

        public Action<T, ISourceDataAction> m_Reducer;
        public event Action<T, ISourceDataAction> Subscribe;

        public DataStore(Action<T, ISourceDataAction> reducer, T initialState)
        {
            m_Reducer = reducer;
            State = initialState;
        }

        public void Dispatch(ISourceDataAction changeAction)
        {
            m_Reducer(State, changeAction);
            // Note: This would only work with reference types, as value types would require creating a new copy, this works given that we use GraphData which is a heap object
            // Notifies any listeners about change in state
            Subscribe?.Invoke(State, changeAction);
        }
    }

    public interface IUIControlledElement
    {
        public UIController Controller { get; }

        void OnControllerChanged(ref UIControllerChangedEvent e);

        void OnControllerEvent(UIControllerEvent e);
    }

    public interface IUIControlledElement<T> : IUIControlledElement where T : UIController
    {
        // This provides a way to access the controller of a ControlledElement at both the base class UIController level and child class level
        new T Controller { get; }
    }

    public class DummyChangeAction : ISourceDataAction
    {
        void OnDummyChangeAction(object sourceData)
        {
        }

        public Action<object> ModifyGraphDataAction => OnDummyChangeAction;
    }

    public struct UIControllerChangedEvent
    {
        public IUIControlledElement target;
        public UIController controller;
        public ISourceDataAction change;

        private bool m_PropagationStopped;
        void StopPropagation()
        {
            m_PropagationStopped = true;
        }

        public bool isPropagationStopped => m_PropagationStopped;
    }

    public class UIControllerEvent
    {
        IUIControlledElement target = null;

        UIControllerEvent(IUIControlledElement controlledTarget)
        {
            target = controlledTarget;
        }
    }

    public abstract class UIController
    {
        public bool DisableCalled = false;

        protected ISourceDataAction DummyChange = new DummyChangeAction();

        public virtual void OnDisable()
        {
            if (DisableCalled)
            {
                Debug.LogError(GetType().Name + ".Disable called twice");
            }

            DisableCalled = true;
            foreach (var element in AllChildren)
            {
                UnityEngine.Profiling.Profiler.BeginSample(element.GetType().Name + ".OnDisable");
                element.OnDisable();
                UnityEngine.Profiling.Profiler.EndSample();
            }
        }

        public void RegisterHandler(IUIControlledElement handler)
        {
            //Debug.Log("RegisterHandler  of " + handler.GetType().Name + " on " + GetType().Name );

            if (m_EventHandlers.Contains(handler))
            {
                Debug.LogError("Handler registered twice");
            }
            else
            {
                m_EventHandlers.Add(handler);

                NotifyEventHandler(handler, DummyChange);
            }
        }

        public void UnregisterHandler(IUIControlledElement handler)
        {
            m_EventHandlers.Remove(handler);
        }

        protected void NotifyChange(ISourceDataAction changeAction)
        {
            var eventHandlers = m_EventHandlers.ToArray(); // Some notification may trigger Register/Unregister so duplicate the collection.

            foreach (var eventHandler in eventHandlers)
            {
                UnityEngine.Profiling.Profiler.BeginSample("NotifyChange:" + eventHandler.GetType().Name);
                NotifyEventHandler(eventHandler, changeAction);
                UnityEngine.Profiling.Profiler.EndSample();
            }
        }

        void NotifyEventHandler(IUIControlledElement eventHandler, ISourceDataAction changeAction)
        {
            UIControllerChangedEvent e = new()
            {
                controller = this,
                target = eventHandler,
                change = changeAction
            };
            eventHandler.OnControllerChanged(ref e);
            if (e.isPropagationStopped)
            {
                return;
            }

            if (eventHandler is VisualElement)
            {
                var element = eventHandler as VisualElement;
                eventHandler = element.GetFirstOfType<IUIControlledElement>();
                while (eventHandler != null)
                {
                    eventHandler.OnControllerChanged(ref e);
                    if (e.isPropagationStopped)
                    {
                        break;
                    }

                    eventHandler = (eventHandler as VisualElement).GetFirstAncestorOfType<IUIControlledElement>();
                }
            }
        }

        public void SendEvent(UIControllerEvent e)
        {
            var eventHandlers = m_EventHandlers.ToArray(); // Some notification may trigger Register/Unregister so duplicate the collection.
            foreach (var eventHandler in eventHandlers)
            {
                eventHandler.OnControllerEvent(e);
            }
        }

        public abstract void ApplyChanges();

        public virtual IEnumerable<UIController> AllChildren
        {
            get { return Enumerable.Empty<UIController>(); }
        }

        protected List<IUIControlledElement> m_EventHandlers = new();
    }

    public abstract class UIController<T> : UIController where T : class
    {
        public T Model { get; protected set; }

        public ISourceDataStore<T> DataStore { get; protected set; }

        protected UIController(T model, ISourceDataStore<T> dataStore)
        {
            Model = model;
            DataStore = dataStore;
            DataStore.Subscribe += ModelChanged;
        }

        protected abstract void RequestModelChange(ISourceDataAction changeAction);

        protected abstract void ModelChanged(T graphData, ISourceDataAction changeAction);

        // Cleanup delegate association before destruction
        public void Cleanup()
        {
            if (DataStore == null)
                return;
            DataStore.Subscribe -= ModelChanged;
            Model = default;
            DataStore = null;
        }
    }

    public abstract class UIViewController<ModelType, ViewModelType> : UIController<ModelType> where ModelType : class
    {
        // Holds data specific to the views this controller is responsible for
        public ViewModelType ViewModel { get; protected set; }

        protected UIViewController(ModelType model, ViewModelType viewModel, ISourceDataStore<ModelType> graphDataStore) : base(model, graphDataStore)
        {
            ViewModel = viewModel;
            try
            {
                // Need ViewModel to be initialized before we call ModelChanged() [as view model might need to update]
                ModelChanged(DataStore.State, DummyChange);
            }
            catch (Exception e)
            {
                Debug.Log($"Failed to initialize View Controller of type: {GetType()} due to exception: {e}");
            }
        }        

        public override void ApplyChanges()
        {
            foreach (var controller in AllChildren)
            {
                controller.ApplyChanges();
            }
        }

        public virtual void Dispose()
        {
            m_EventHandlers.Clear();
            ViewModel = default;
        }
    }
}
