using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using Vapor.Inspector;
using Vapor.Keys;

namespace Vapor.Events
{
    public abstract class ProvidedMonoBehaviour : VaporBehaviour
    {
        [BoxGroup("Provided Key", order: -10000), SerializeField, Dropdown("@GetAllProviderKeyValues"), IgnoreCustomDrawer]
        protected KeyDropdownValue Key;

        protected virtual void OnEnable()
        {
            if (Key.IsNone) return;

            ProviderBus.Get<CachedProviderData<Component>>(Key).Subscribe(OnComponentRequested);
        }

        protected virtual void OnDisable()
        {
            if (Key.IsNone) return;

            ProviderBus.Get<CachedProviderData<Component>>(Key).Unsubscribe(OnComponentRequested);
        }

        protected Component OnComponentRequested()
        {
            return this;
        }

        /// <summary>
        /// Returns the <see cref="ProvidedMonoBehaviour"/> cast to its inherited class.
        /// The result should be cached if used more than once.
        /// </summary>
        /// <typeparam name="T">The type to cast to. Must inherit from <see cref="ProvidedMonoBehaviour"/></typeparam>
        /// <returns>T: Cannot return null</returns>
        public T As<T>() where T : ProvidedMonoBehaviour
        {
            Assert.IsNotNull((T)this, $"Type {TooltipMarkup.Class(nameof(T))} must inherit from {TooltipMarkup.Class(nameof(ProvidedMonoBehaviour))}");
            return (T)this;
        }

        public static IEnumerable<DropdownModel> GetAllProviderKeyValues()
        {
            return EventKeyUtility.GetAllProviderKeyValues();
        }
    }
}
