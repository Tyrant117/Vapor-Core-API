using NUnit.Framework;
using Unity.Netcode;
using Vapor.NetworkObjects;

namespace Vapor.Tests
{
    /// <summary>
    /// The relevance rule and the observer bookkeeping, which is where interest management is most
    /// likely to be quietly wrong: a client that keeps seeing an object it left, or stops seeing one it
    /// still has another claim to.
    /// </summary>
    [TestFixture]
    public class InterestRegistryTests
    {
        private const ulong k_ClientA = 1;
        private const ulong k_ClientB = 2;

        private static readonly InterestGroup s_Town = new("zone.town");
        private static readonly InterestGroup s_Dungeon = new("zone.dungeon");

        private InterestRegistry _registry;

        [SetUp]
        public void SetUp() => _registry = new InterestRegistry();

        #region - Relevance -

        [Test]
        public void AnObjectWithNoGroupsIsGlobal()
        {
            var global = Spawned(1);

            Assert.IsTrue(_registry.IsRelevantTo(global, k_ClientA));
            Assert.IsTrue(_registry.IsRelevantTo(global, k_ClientB));
        }

        [Test]
        public void JoiningAGroupNarrowsAnObjectToItsSubscribers()
        {
            var townsfolk = Spawned(1, s_Town);
            _registry.Register(townsfolk);

            Assert.IsFalse(_registry.IsRelevantTo(townsfolk, k_ClientA), "nobody is subscribed yet");

            _registry.Subscribe(k_ClientA, s_Town);

            Assert.IsTrue(_registry.IsRelevantTo(townsfolk, k_ClientA));
            Assert.IsFalse(_registry.IsRelevantTo(townsfolk, k_ClientB));
        }

        [Test]
        public void AnyOneMatchingGroupIsEnough()
        {
            var wanderer = Spawned(1, s_Town, s_Dungeon);
            _registry.Subscribe(k_ClientA, s_Dungeon);

            Assert.IsTrue(_registry.IsRelevantTo(wanderer, k_ClientA));
        }

        /// <summary>
        /// Leaving one channel must not hide an object the client still has another claim to — the
        /// mistake that makes objects vanish at zone borders.
        /// </summary>
        [Test]
        public void LeavingOneOfTwoSharedGroupsKeepsTheObjectRelevant()
        {
            var wanderer = Spawned(1, s_Town, s_Dungeon);
            _registry.Register(wanderer);
            _registry.Subscribe(k_ClientA, s_Town);
            _registry.Subscribe(k_ClientA, s_Dungeon);

            _registry.Unsubscribe(k_ClientA, s_Town);

            Assert.IsTrue(_registry.IsRelevantTo(wanderer, k_ClientA));

            _registry.Unsubscribe(k_ClientA, s_Dungeon);

            Assert.IsFalse(_registry.IsRelevantTo(wanderer, k_ClientA));
        }

        [Test]
        public void AnOwnerOnlyObjectConcernsItsOwnerAlone()
        {
            var privateThing = Spawned(1);
            privateThing.SpawnedOnlyOnOwner = true;
            privateThing.OwnerClientId = k_ClientA;

            Assert.IsTrue(_registry.IsRelevantTo(privateThing, k_ClientA));
            Assert.IsFalse(_registry.IsRelevantTo(privateThing, k_ClientB));
        }

        /// <summary>
        /// Owner-only wins over interest, so subscribing to an object's channel cannot be used to see
        /// somebody else's private state.
        /// </summary>
        [Test]
        public void SubscribingDoesNotExposeAnotherClientsOwnerOnlyObject()
        {
            var privateThing = Spawned(1, s_Town);
            privateThing.SpawnedOnlyOnOwner = true;
            privateThing.OwnerClientId = k_ClientA;
            _registry.Register(privateThing);
            _registry.Subscribe(k_ClientB, s_Town);

            Assert.IsFalse(_registry.IsRelevantTo(privateThing, k_ClientB));
        }

        [Test]
        public void TheServerIsNeverAnObserver()
        {
            var global = Spawned(1);

            Assert.IsFalse(_registry.IsRelevantTo(global, NetworkManager.ServerClientId),
                "the server holds the instance already; sending it to itself would be a loopback");
        }

        [Test]
        public void TheServerCannotSubscribe()
        {
            Assert.IsFalse(_registry.Subscribe(NetworkManager.ServerClientId, s_Town));
            Assert.IsFalse(_registry.IsSubscribed(NetworkManager.ServerClientId, s_Town));
        }

        [Test]
        public void TheEmptyGroupIsNotAChannel()
        {
            Assert.IsFalse(_registry.Subscribe(k_ClientA, InterestGroup.None));
            Assert.IsFalse(_registry.IsSubscribed(k_ClientA, InterestGroup.None));
        }

        #endregion

        #region - Subscription bookkeeping -

        [Test]
        public void SubscribingTwiceReportsNoChangeTheSecondTime()
        {
            Assert.IsTrue(_registry.Subscribe(k_ClientA, s_Town));
            Assert.IsFalse(_registry.Subscribe(k_ClientA, s_Town));
        }

        [Test]
        public void UnsubscribingWhatWasNeverSubscribedReportsNoChange()
        {
            Assert.IsFalse(_registry.Unsubscribe(k_ClientA, s_Town));

            _registry.Subscribe(k_ClientA, s_Town);
            Assert.IsTrue(_registry.Unsubscribe(k_ClientA, s_Town));
            Assert.IsFalse(_registry.Unsubscribe(k_ClientA, s_Town));
        }

        [Test]
        public void MembershipTracksTheObjectsInAChannel()
        {
            var first = Spawned(1, s_Town);
            var second = Spawned(2, s_Town);
            _registry.Register(first);
            _registry.Register(second);

            CollectionAssert.AreEquivalent(new[] { first, second }, _registry.MembersOf(s_Town));

            _registry.Leave(second, s_Town);
            CollectionAssert.AreEquivalent(new[] { first }, _registry.MembersOf(s_Town));
        }

        [Test]
        public void AnUnknownChannelHasNoMembers()
        {
            Assert.AreEqual(0, _registry.MembersOf(s_Dungeon).Count);
        }

        #endregion

        #region - Observation -

        [Test]
        public void ObservationIsRecordedPerClient()
        {
            Assert.IsTrue(_registry.MarkObserving(k_ClientA, 1));
            Assert.IsFalse(_registry.MarkObserving(k_ClientA, 1), "already told about it");

            Assert.IsTrue(_registry.IsObserving(k_ClientA, 1));
            Assert.IsFalse(_registry.IsObserving(k_ClientB, 1));

            Assert.IsTrue(_registry.ClearObserving(k_ClientA, 1));
            Assert.IsFalse(_registry.ClearObserving(k_ClientA, 1));
            Assert.IsFalse(_registry.IsObserving(k_ClientA, 1));
        }

        /// <summary>
        /// Subscribing does not by itself make a client an observer — the spawn has to go out first.
        /// Otherwise a sync could be addressed to a client with no instance to apply it to.
        /// </summary>
        [Test]
        public void RelevanceAloneDoesNotMakeAnObserver()
        {
            var townsfolk = Spawned(1, s_Town);
            _registry.Register(townsfolk);
            _registry.Subscribe(k_ClientA, s_Town);

            Assert.IsTrue(_registry.IsRelevantTo(townsfolk, k_ClientA));
            Assert.IsFalse(_registry.IsObserving(k_ClientA, townsfolk.NetworkObjectId));
        }

        [Test]
        public void UnregisteringAnObjectForgetsItEverywhere()
        {
            var townsfolk = Spawned(7, s_Town);
            _registry.Register(townsfolk);
            _registry.MarkObserving(k_ClientA, 7);
            _registry.MarkObserving(k_ClientB, 7);

            _registry.Unregister(townsfolk);

            Assert.IsFalse(_registry.IsObserving(k_ClientA, 7));
            Assert.IsFalse(_registry.IsObserving(k_ClientB, 7));
            Assert.AreEqual(0, _registry.MembersOf(s_Town).Count);
        }

        [Test]
        public void DroppingAClientForgetsItsSubscriptionsAndItsView()
        {
            _registry.Subscribe(k_ClientA, s_Town);
            _registry.MarkObserving(k_ClientA, 1);

            _registry.DropClient(k_ClientA);

            Assert.IsFalse(_registry.IsSubscribed(k_ClientA, s_Town));
            Assert.IsFalse(_registry.IsObserving(k_ClientA, 1));
        }

        /// <summary>
        /// Ids are reused when a client reconnects, so a stale view left behind by the previous session
        /// would convince the server the new client already had objects it has never seen.
        /// </summary>
        [Test]
        public void AReconnectingClientStartsWithNoView()
        {
            _registry.Subscribe(k_ClientA, s_Town);
            _registry.MarkObserving(k_ClientA, 1);
            _registry.DropClient(k_ClientA);

            Assert.IsTrue(_registry.MarkObserving(k_ClientA, 1), "the object has to be sent again");
        }

        #endregion

        #region - Hierarchy -

        /// <summary>
        /// A sub-object cannot outlive its parent's relevance. Judged on its own, a global child of a
        /// private parent would be handed to every client.
        /// </summary>
        [Test]
        public void AGlobalChildOfAnOwnerOnlyParentStaysPrivate()
        {
            var backpack = Spawned(1);
            backpack.SpawnedOnlyOnOwner = true;
            backpack.OwnerClientId = k_ClientA;

            var item = Spawned(2);
            item.ParentNetworkObjectId = 1;
            WithHierarchy(backpack, item);

            Assert.IsTrue(_registry.IsRelevantTo(item, k_ClientB), "the child alone looks global");
            Assert.IsFalse(_registry.IsVisibleTo(item, k_ClientB), "but its parent is private");
            Assert.IsTrue(_registry.IsVisibleTo(item, k_ClientA));
        }

        [Test]
        public void AChildIsHiddenWhileItsParentsChannelIsUnsubscribed()
        {
            var chest = Spawned(1, s_Town);
            var contents = Spawned(2);
            contents.ParentNetworkObjectId = 1;
            WithHierarchy(chest, contents);
            _registry.Register(chest);

            Assert.IsFalse(_registry.IsVisibleTo(contents, k_ClientA));

            _registry.Subscribe(k_ClientA, s_Town);
            Assert.IsTrue(_registry.IsVisibleTo(contents, k_ClientA));
        }

        [Test]
        public void VisibilityWalksTheWholeChain()
        {
            var root = Spawned(1, s_Town);
            var middle = Spawned(2);
            middle.ParentNetworkObjectId = 1;
            var leaf = Spawned(3);
            leaf.ParentNetworkObjectId = 2;
            WithHierarchy(root, middle, leaf);

            Assert.IsFalse(_registry.IsVisibleTo(leaf, k_ClientA));

            _registry.Subscribe(k_ClientA, s_Town);
            Assert.IsTrue(_registry.IsVisibleTo(leaf, k_ClientA));
        }

        [Test]
        public void AParentCycleTerminates()
        {
            var first = Spawned(1);
            var second = Spawned(2);
            first.ParentNetworkObjectId = 2;
            second.ParentNetworkObjectId = 1;
            WithHierarchy(first, second);

            Assert.IsTrue(_registry.IsVisibleTo(first, k_ClientA), "a malformed cycle must not hang the walk");
        }

        [Test]
        public void AMissingParentDoesNotHideTheChild()
        {
            var orphan = Spawned(2);
            orphan.ParentNetworkObjectId = 999;
            WithHierarchy(orphan);

            Assert.IsTrue(_registry.IsVisibleTo(orphan, k_ClientA));
        }

        #endregion

        #region - Harness -

        /// <summary>Points the registry's parent walk at a fixed set of objects.</summary>
        private void WithHierarchy(params TestNetworkObject[] objects)
        {
            _registry.ParentLookup = id =>
            {
                foreach (var candidate in objects)
                {
                    if (candidate.NetworkObjectId == id)
                    {
                        return candidate;
                    }
                }

                return null;
            };
        }

        private static TestNetworkObject Spawned(ulong networkObjectId, params InterestGroup[] groups)
        {
            var networkObject = new TestNetworkObject { NetworkObjectId = networkObjectId };
            foreach (var group in groups)
            {
                networkObject.AddInterestGroup(group);
            }

            return networkObject;
        }

        private sealed class TestNetworkObject : VaporNetworkObject
        {
            protected internal override bool ShouldTick => false;

            protected internal override void OnPreSpawn() { }
            protected internal override void OnSpawn() { }
            protected internal override void OnPostSpawn() { }
            protected internal override void OnDespawn() { }
            protected override string ToJson() => string.Empty;
            protected override void FromJson(string json) { }
        }

        #endregion
    }
}
