using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace VaporEditor.Inspector
{
    /// <summary>
    /// Drives every attribute resolver in the inspector from a single editor update.
    /// Each element used to run its own endless coroutine, so an inspector with fifty conditional fields
    /// spent every frame starting fifty of them.
    /// </summary>
    internal static class InspectorResolverTicker
    {
        private class Entry
        {
            public readonly List<SerializedResolverContainer> Resolvers = new();

            /// <summary>
            /// Owners register during construction, before they reach a panel. Without this we could not
            /// tell "not attached yet" from "attached and then removed".
            /// </summary>
            public bool WasAttached;
        }

        private static readonly Dictionary<VisualElement, Entry> s_Entries = new();
        private static readonly List<SerializedResolverContainer> s_Pending = new();
        private static readonly List<VisualElement> s_Stale = new();
        private static bool s_Subscribed;

        public static void Register(VisualElement owner, SerializedResolverContainer resolver)
        {
            if (owner == null || resolver == null)
            {
                return;
            }

            if (!s_Entries.TryGetValue(owner, out var entry))
            {
                entry = new Entry();
                s_Entries[owner] = entry;

                // Registered once per owner rather than per resolver, and by the ticker rather than by the
                // owner, so an element that never bothers to hook detach cannot leak its resolvers.
                owner.RegisterCallback<DetachFromPanelEvent>(OnOwnerDetached);
            }

            entry.Resolvers.Add(resolver);

            if (s_Subscribed)
            {
                return;
            }

            s_Subscribed = true;
            EditorApplication.update += Tick;
        }

        public static void Unregister(VisualElement owner)
        {
            if (owner == null || !s_Entries.Remove(owner))
            {
                return;
            }

            if (s_Entries.Count != 0 || !s_Subscribed)
            {
                return;
            }

            s_Subscribed = false;
            EditorApplication.update -= Tick;
        }

        private static void OnOwnerDetached(DetachFromPanelEvent evt)
        {
            Unregister(evt.currentTarget as VisualElement);
        }

        private static void Tick()
        {
            // Collected before resolving, because a resolver can rebuild the tree and mutate the registry.
            s_Pending.Clear();
            foreach (var pair in s_Entries)
            {
                if (pair.Key.panel != null)
                {
                    pair.Value.WasAttached = true;
                }
                else if (pair.Value.WasAttached)
                {
                    // Backstop for an owner that left its panel without us seeing the detach. Left running
                    // it would keep resolving forever and keep its target object alive with it.
                    s_Stale.Add(pair.Key);
                    continue;
                }

                s_Pending.AddRange(pair.Value.Resolvers);
            }

            foreach (var owner in s_Stale)
            {
                Unregister(owner);
            }
            s_Stale.Clear();

            foreach (var resolver in s_Pending)
            {
                resolver.Resolve();
            }
            s_Pending.Clear();
        }
    }
}
