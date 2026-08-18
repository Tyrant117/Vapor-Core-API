using System;
using System.Reflection;
using Unity.Scripting.LifecycleManagement;
using UnityEngine.UIElements;

namespace VaporEditor.Inspector
{
    [NoAutoStaticsCleanup]
    public static class BaseListViewReflection
    {
        private static EventInfo _itemsSourceSizeChangedEvent;

        public static void AddItemSourceSizeChangedListener(this BaseListView list, Action callback)
        {
            _itemsSourceSizeChangedEvent ??= typeof(BaseListView).GetEvent("itemsSourceSizeChanged", BindingFlags.NonPublic | BindingFlags.Instance);
            _itemsSourceSizeChangedEvent.AddEventHandler(list, callback);
        }

        public static void RemoveItemSourceSizeChangedListener(this BaseListView list, Action callback)
        {
            _itemsSourceSizeChangedEvent ??= typeof(BaseListView).GetEvent("itemsSourceSizeChanged", BindingFlags.NonPublic | BindingFlags.Instance);
            _itemsSourceSizeChangedEvent.RemoveEventHandler(list, callback);
        }
    }
}
