using System;
using UnityEngine.InputSystem;

namespace Vapor
{
    public static class InputActionExtensions
    {
        public static InputAction WithPerformed(this InputAction action, Action callback, out Action unsubscribeToken)
        {
            // Define the delegate instances
            void OnPerformed(InputAction.CallbackContext context) => callback.Invoke();

            action.performed += OnPerformed;

            unsubscribeToken = () =>
            {
                action.performed -= OnPerformed;
            };
            return action;
        }

        public static InputAction WithPerformedContext(this InputAction action, Action<InputAction.CallbackContext> callback, out Action unsubscribeToken)
        {
            // Define the delegate instances
            void OnPerformed(InputAction.CallbackContext context) => callback.Invoke(context);

            action.performed += OnPerformed;

            unsubscribeToken = () =>
            {
                action.performed -= OnPerformed;
            };
            return action;
        }

        public static InputAction WithPerformed<T>(this InputAction action, Action<T> callback, T value, out Action unsubscribeToken)
        {
            // Define the delegate instances
            void OnPerformed(InputAction.CallbackContext context) => callback.Invoke(value);

            action.performed += OnPerformed;

            unsubscribeToken = () =>
            {
                action.performed -= OnPerformed;
            };
            return action;
        }

        public static InputAction WithPerformedRead<T>(this InputAction action, Action<T> callback, out Action unsubscribeToken) where T : struct
        {
            // Define the delegate instances
            void OnPerformed(InputAction.CallbackContext context) => callback.Invoke(context.ReadValue<T>());

            action.performed += OnPerformed;

            unsubscribeToken = () =>
            {
                action.performed -= OnPerformed;
            };
            return action;
        }

        public static InputAction WithCanceled(this InputAction action, Action callback, out Action unsubscribeToken)
        {
            void OnCanceled(InputAction.CallbackContext context) => callback.Invoke();

            action.canceled += OnCanceled;

            unsubscribeToken = () =>
            {
                action.canceled -= OnCanceled;
            };
            return action;
        }

        public static InputAction WithCanceledContext(this InputAction action, Action<InputAction.CallbackContext> callback, out Action unsubscribeToken)
        {
            void OnCanceled(InputAction.CallbackContext context) => callback.Invoke(context);

            action.canceled += OnCanceled;

            unsubscribeToken = () =>
            {
                action.canceled -= OnCanceled;
            };
            return action;
        }

        public static InputAction WithCanceled<T>(this InputAction action, Action<T> callback, T value, out Action unsubscribeToken)
        {
            void OnCanceled(InputAction.CallbackContext context) => callback.Invoke(value);

            action.canceled += OnCanceled;

            unsubscribeToken = () =>
            {
                action.canceled -= OnCanceled;
            };
            return action;
        }

        public static InputAction WithCanceledRead<T>(this InputAction action, Action<T> callback, out Action unsubscribeToken) where T : struct
        {
            void OnCanceled(InputAction.CallbackContext context) => callback.Invoke(context.ReadValue<T>());

            action.canceled += OnCanceled;

            unsubscribeToken = () =>
            {
                action.canceled -= OnCanceled;
            };
            return action;
        }

        public static InputAction WithPerformedAndCanceled(this InputAction action, Action callback, out Action unsubscribeToken)
        {
            // Define the delegate instances
            void OnPerformed(InputAction.CallbackContext context) => callback.Invoke();
            void OnCanceled(InputAction.CallbackContext context) => callback.Invoke();

            action.performed += OnPerformed;
            action.canceled += OnCanceled;

            unsubscribeToken = () =>
            {
                action.performed -= OnPerformed;
                action.canceled -= OnCanceled;
            };
            return action;
        }

        public static InputAction WithPerformedAndCanceledContext(this InputAction action, Action<InputAction.CallbackContext> callback, out Action unsubscribeToken)
        {
            // Define the delegate instances
            void OnPerformed(InputAction.CallbackContext context) => callback.Invoke(context);
            void OnCanceled(InputAction.CallbackContext context) => callback.Invoke(context);

            action.performed += OnPerformed;
            action.canceled += OnCanceled;

            unsubscribeToken = () =>
            {
                action.performed -= OnPerformed;
                action.canceled -= OnCanceled;
            };
            return action;
        }

        public static InputAction WithPerformedAndCanceled<T>(this InputAction action, Action<T> callback, T performed, T canceled, out Action unsubscribeToken)
        {
            // Define the delegate instances
            void OnPerformed(InputAction.CallbackContext context) => callback.Invoke(performed);
            void OnCanceled(InputAction.CallbackContext context) => callback.Invoke(canceled);

            action.performed += OnPerformed;
            action.canceled += OnCanceled;

            unsubscribeToken = () =>
            {
                action.performed -= OnPerformed;
                action.canceled -= OnCanceled;
            };
            return action;
        }

        public static InputAction WithPerformedAndCanceledRead<T>(this InputAction action, Action<T> callback, out Action unsubscribeToken) where T : struct
        {
            // Define the delegate instances
            void OnPerformed(InputAction.CallbackContext context) => callback.Invoke(context.ReadValue<T>());
            void OnCanceled(InputAction.CallbackContext context) => callback.Invoke(context.ReadValue<T>());

            action.performed += OnPerformed;
            action.canceled += OnCanceled;

            unsubscribeToken = () =>
            {
                action.performed -= OnPerformed;
                action.canceled -= OnCanceled;
            };
            return action;
        }
    }
}
