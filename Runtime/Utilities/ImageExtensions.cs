using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UIElements;

namespace Vapor
{
    public static class ImageExtensions
    {
        public static async Awaitable SetSpriteAsync(this Image image, AsyncOperationHandle<Sprite> handle)
        {
            image.visible = false;
            await handle.Task;
            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                return;
            }

            image.sprite = handle.Result;
            image.visible = true;
        }
    }
}