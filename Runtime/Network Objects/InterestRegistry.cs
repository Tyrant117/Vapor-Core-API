using System;
using System.Collections.Generic;
using Unity.Netcode;

namespace Vapor.NetworkObjects
{
    /// <summary>
    /// Decides who can see what, and remembers what each client has actually been told about.
    /// </summary>
    /// <remarks>
    /// Deliberately free of transport and of <see cref="NetworkMessages"/>: relevance and the observer
    /// bookkeeping are the parts most likely to be subtly wrong, and keeping them as plain data lets
    /// them be tested without standing up a NetworkManager.
    /// <para>
    /// The known set is the authority on where anything may be addressed. A client that has never been
    /// sent an object cannot be sent a delta or an rpc for it, whatever its subscriptions say — which
    /// is what stops a subscription change mid-tick from producing a sync for an object the client has
    /// no instance of.
    /// </para>
    /// </remarks>
    internal sealed class InterestRegistry
    {
        /// <summary>A hierarchy deeper than this is malformed; the walk stops rather than looping.</summary>
        private const int k_MaxHierarchyDepth = 64;

        private static readonly HashSet<VaporNetworkObject> s_NoMembers = new();

        /// <summary>
        /// Resolves a network object id to its instance, so relevance can walk up a parent chain.
        /// Supplied by <see cref="NetworkMessages"/>; a null lookup makes every object a root.
        /// </summary>
        public Func<ulong, VaporNetworkObject> ParentLookup;

        private readonly Dictionary<ulong, HashSet<InterestGroup>> _subscriptions = new();
        private readonly Dictionary<InterestGroup, HashSet<VaporNetworkObject>> _members = new();
        private readonly Dictionary<ulong, HashSet<ulong>> _known = new();

        #region - Subscriptions -

        /// <returns>True when this was a new subscription and the client may now see more.</returns>
        public bool Subscribe(ulong clientId, InterestGroup group)
        {
            if (group.IsNone || clientId == NetworkManager.ServerClientId)
            {
                return false;
            }

            if (!_subscriptions.TryGetValue(clientId, out var groups))
            {
                groups = new HashSet<InterestGroup>();
                _subscriptions.Add(clientId, groups);
            }

            return groups.Add(group);
        }

        /// <returns>True when the client really was subscribed and may now see less.</returns>
        public bool Unsubscribe(ulong clientId, InterestGroup group) =>
            !group.IsNone && _subscriptions.TryGetValue(clientId, out var groups) && groups.Remove(group);

        public bool IsSubscribed(ulong clientId, InterestGroup group) =>
            _subscriptions.TryGetValue(clientId, out var groups) && groups.Contains(group);

        #endregion

        #region - Membership -

        public IReadOnlyCollection<VaporNetworkObject> MembersOf(InterestGroup group) =>
            _members.TryGetValue(group, out var members) ? members : s_NoMembers;

        public void Join(VaporNetworkObject networkObject, InterestGroup group)
        {
            if (group.IsNone)
            {
                return;
            }

            if (!_members.TryGetValue(group, out var members))
            {
                members = new HashSet<VaporNetworkObject>();
                _members.Add(group, members);
            }

            members.Add(networkObject);
        }

        public void Leave(VaporNetworkObject networkObject, InterestGroup group)
        {
            if (_members.TryGetValue(group, out var members))
            {
                members.Remove(networkObject);
            }
        }

        public void Register(VaporNetworkObject networkObject)
        {
            foreach (var group in networkObject.InterestGroups)
            {
                Join(networkObject, group);
            }
        }

        public void Unregister(VaporNetworkObject networkObject)
        {
            foreach (var group in networkObject.InterestGroups)
            {
                Leave(networkObject, group);
            }

            foreach (var known in _known.Values)
            {
                known.Remove(networkObject.NetworkObjectId);
            }
        }

        #endregion

        #region - Relevance -

        /// <summary>
        /// An owner-only object concerns its owner alone. Otherwise an object with no groups is global,
        /// and one with groups reaches a client subscribed to any of them.
        /// </summary>
        public bool IsRelevantTo(VaporNetworkObject networkObject, ulong clientId)
        {
            if (clientId == NetworkManager.ServerClientId)
            {
                return false;
            }

            if (networkObject.SpawnedOnlyOnOwner)
            {
                return networkObject.OwnerClientId == clientId;
            }

            if (networkObject.IsGloballyRelevant)
            {
                return true;
            }

            if (!_subscriptions.TryGetValue(clientId, out var groups))
            {
                return false;
            }

            foreach (var group in networkObject.InterestGroups)
            {
                if (groups.Contains(group))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether a client should hold this object at all, accounting for what it hangs from.
        /// </summary>
        /// <remarks>
        /// Relevance flows down a hierarchy: a sub-object is visible only to a client that can also
        /// see its parent. Testing the object alone would hand a global child of an owner-only parent
        /// to every client, and would spawn children whose parent never arrived — the parent link is
        /// resolved once, on arrival, and never retried.
        /// </remarks>
        public bool IsVisibleTo(VaporNetworkObject networkObject, ulong clientId)
        {
            var current = networkObject;
            for (int depth = 0; depth < k_MaxHierarchyDepth; depth++)
            {
                if (!IsRelevantTo(current, clientId))
                {
                    return false;
                }

                if (current.IsRoot || current.ParentIsUnityObject || ParentLookup == null)
                {
                    return true;
                }

                var parent = ParentLookup(current.ParentNetworkObjectId);
                if (parent == null || ReferenceEquals(parent, current))
                {
                    return true;
                }

                current = parent;
            }

            return true;
        }

        #endregion

        #region - Observation -

        public bool IsObserving(ulong clientId, ulong networkObjectId) =>
            _known.TryGetValue(clientId, out var known) && known.Contains(networkObjectId);

        /// <returns>True when the client had not been told about this object before.</returns>
        public bool MarkObserving(ulong clientId, ulong networkObjectId)
        {
            if (!_known.TryGetValue(clientId, out var known))
            {
                known = new HashSet<ulong>();
                _known.Add(clientId, known);
            }

            return known.Add(networkObjectId);
        }

        /// <returns>True when the client had been told about this object and now must forget it.</returns>
        public bool ClearObserving(ulong clientId, ulong networkObjectId) =>
            _known.TryGetValue(clientId, out var known) && known.Remove(networkObjectId);

        public void DropClient(ulong clientId)
        {
            _subscriptions.Remove(clientId);
            _known.Remove(clientId);
        }

        #endregion
    }
}
