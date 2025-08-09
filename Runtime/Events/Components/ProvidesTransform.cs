using UnityEngine;
using Vapor.Inspector;
using Vapor.Keys;

namespace Vapor.Events
{
    public class ProvidesTransform : VaporBehaviour
    {
        [SerializeField, Dropdown(EventKeyUtility.PROVIDERS_CATEGORY_NAME, DropdownAttribute.FilterType.Category), IgnoreCustomDrawer]
        private KeyDropdownValue _key;
        [SerializeField]
        private Transform _transform;

        private void OnEnable()
        {
            if (_key.IsNone) return;

            ProviderBus.Get<CachedProviderData<Transform>>(_key).Subscribe(OnComponentRequested);
        }

        private void OnDisable()
        {
            if (_key.IsNone) return;

            ProviderBus.Get<CachedProviderData<Transform>>(_key).Unsubscribe(OnComponentRequested);
        }

        private Transform OnComponentRequested()
        {
            return _transform;
        }
    }
}
