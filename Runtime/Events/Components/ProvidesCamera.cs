using UnityEngine;
using Vapor.Inspector;
using Vapor.Keys;

namespace Vapor.Events
{
    public class ProvidesCamera : VaporBehaviour
    {
        [SerializeField, Dropdown(EventKeyUtility.PROVIDERS_CATEGORY_NAME, DropdownAttribute.FilterType.Category)]
        private KeyDropdownValue _key;
        [SerializeField]
        private Camera _camera;
        public Camera Camera
        {
            get => _camera;
            set
            {
                _camera = value;
                Debug.Log($"{TooltipMarkup.Class(nameof(ProvidesCamera))} - New Camera Set");
            }
        }

        private void OnEnable()
        {
            if (_key.IsNone) return;

            ProviderBus.Get<ReferenceProviderData<Camera>>(_key).Subscribe(OnComponentRequested);
        }

        private void OnDisable()
        {
            if (_key.IsNone) return;

            ProviderBus.Get<ReferenceProviderData<Camera>>(_key).Unsubscribe(OnComponentRequested);
        }

        private Camera OnComponentRequested()
        {
            return _camera;
        }
    }
}
