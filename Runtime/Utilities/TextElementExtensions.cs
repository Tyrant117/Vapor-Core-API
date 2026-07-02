using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UIElements;

namespace Vapor
{
    public static class TextElementExtensions
    {
        public static async Awaitable SetTextAsync(this TextElement textElement, AsyncOperationHandle<string> handle)
        {
            await handle.Task;
            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                return;
            }

            textElement.text = handle.Result;
        }
    }
}