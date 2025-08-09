using UnityEngine;
using UnityEngine.Assertions;
using Vapor.Inspector;
using Vapor.Keys;

namespace Vapor.Events
{
    public abstract class ProvidesBehaviour : VaporBehaviour
    {
        [BoxGroup("Provided Key", order: -10000), SerializeField, Dropdown(EventKeyUtility.PROVIDERS_CATEGORY_NAME, DropdownAttribute.FilterType.Category), IgnoreCustomDrawer]
        protected KeyDropdownValue Key;

        protected virtual void OnEnable()
        {
            if (Key.IsNone) return;

            ProviderBus.Get<CachedProviderData<ProvidesBehaviour>>(Key).Subscribe(OnComponentRequested);
        }

        protected virtual void OnDisable()
        {
            if (Key.IsNone) return;

            ProviderBus.Get<CachedProviderData<ProvidesBehaviour>>(Key).Unsubscribe(OnComponentRequested);
        }

        protected ProvidesBehaviour OnComponentRequested()
        {
            return this;
        }

        /// <summary>
        /// Returns the <see cref="ProvidesBehaviour"/> cast to its inherited class.
        /// The result should be cached if used more than once.
        /// </summary>
        /// <typeparam name="T">The type to cast to. Must inherit from <see cref="ProvidesBehaviour"/></typeparam>
        /// <returns>T: Cannot return null</returns>
        public T As<T>() where T : ProvidesBehaviour
        {
            Assert.IsNotNull((T)this, $"Type {TooltipMarkup.Class(nameof(T))} must inherit from {TooltipMarkup.Class(nameof(ProvidesBehaviour))}");
            return (T)this;
        }
    }
}
