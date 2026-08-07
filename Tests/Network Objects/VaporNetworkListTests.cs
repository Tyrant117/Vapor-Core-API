using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using Vapor.NetworkObjects;

namespace Vapor.Tests
{
    /// <summary>
    /// Round-trips <see cref="VaporNetworkList{T}"/> through the same buffers the transport uses.
    /// </summary>
    /// <remarks>
    /// Every assertion here goes through <see cref="Transfer"/>, which reads the variable id off the
    /// front of the payload exactly as <see cref="VaporNetworkObject.Deserialize"/> does. That framing
    /// is the part worth pinning: a Write that omits the id does not corrupt its own entry, it
    /// corrupts every entry after it in the packet, and nothing about the list itself would look wrong.
    /// </remarks>
    [TestFixture]
    public class VaporNetworkListTests
    {
        private GameObject _host;
        private NetworkMessages _messages;
        private TestNetworkObject _server;
        private TestNetworkObject _client;

        [SetUp]
        public void SetUp()
        {
            // Inactive, so NetworkMessages.Awake never runs — it wants a NetworkManager beside it, and
            // the only member reached here is the send queue, which is initialized inline.
            _host = new GameObject(nameof(VaporNetworkListTests));
            _host.SetActive(false);
            _messages = _host.AddComponent<NetworkMessages>();

            _server = new TestNetworkObject
            {
                NetworkObjectId = 1,
                IsServer = true,
                NetworkMessages = _messages,
            };

            // Never spawned: the receiving side only ever reads.
            _client = new TestNetworkObject();
        }

        [TearDown]
        public void TearDown()
        {
            _server.Destroy();
            _client.Destroy();
            Object.DestroyImmediate(_host);
        }

        #region - Framing and snapshots -

        [Test]
        public void FullSnapshotCarriesEveryElement()
        {
            var server = new VaporNetworkList<int>(new[] { 3, 1, 4, 1, 5 }, _server);
            var client = new VaporNetworkList<int>(_client);

            Transfer(server, client, full: true);

            CollectionAssert.AreEqual(new[] { 3, 1, 4, 1, 5 }, ToArray(client));
        }

        [Test]
        public void FullSnapshotReplacesWhateverTheReceiverHad()
        {
            var server = new VaporNetworkList<int>(new[] { 9 }, _server);
            var client = new VaporNetworkList<int>(new[] { 1, 2, 3, 4 }, _client);

            Transfer(server, client, full: true);

            CollectionAssert.AreEqual(new[] { 9 }, ToArray(client));
        }

        [Test]
        public void EmptyListRoundTrips()
        {
            var server = new VaporNetworkList<int>(_server);
            var client = new VaporNetworkList<int>(new[] { 1, 2 }, _client);

            Transfer(server, client, full: true);

            Assert.AreEqual(0, client.Count);
        }

        #endregion

        #region - Deltas -

        [Test]
        public void AddReplicatesAsADelta()
        {
            var server = new VaporNetworkList<int>(new[] { 1, 2 }, _server);
            var client = new VaporNetworkList<int>(new[] { 1, 2 }, _client);

            server.Add(7);
            server.Add(8);
            Transfer(server, client, full: false);

            CollectionAssert.AreEqual(new[] { 1, 2, 7, 8 }, ToArray(client));
        }

        [Test]
        public void InsertRemoveAtAndSetReplicate()
        {
            var server = new VaporNetworkList<int>(new[] { 10, 20, 30, 40 }, _server);
            var client = new VaporNetworkList<int>(new[] { 10, 20, 30, 40 }, _client);

            server.Insert(1, 15);
            server.RemoveAt(0);
            server.Set(0, 99);
            Transfer(server, client, full: false);

            CollectionAssert.AreEqual(ToArray(server), ToArray(client));
            CollectionAssert.AreEqual(new[] { 99, 20, 30, 40 }, ToArray(client));
        }

        [Test]
        public void RemoveByValueReplicates()
        {
            var server = new VaporNetworkList<int>(new[] { 5, 6, 7 }, _server);
            var client = new VaporNetworkList<int>(new[] { 5, 6, 7 }, _client);

            Assert.IsTrue(server.Remove(6));
            Assert.IsFalse(server.Remove(42));
            Transfer(server, client, full: false);

            CollectionAssert.AreEqual(new[] { 5, 7 }, ToArray(client));
        }

        [Test]
        public void InsertAtTheEndBehavesAsAnAppend()
        {
            var server = new VaporNetworkList<int>(new[] { 1 }, _server);
            var client = new VaporNetworkList<int>(new[] { 1 }, _client);

            server.Insert(1, 2);
            Transfer(server, client, full: false);

            CollectionAssert.AreEqual(new[] { 1, 2 }, ToArray(client));
        }

        [Test]
        public void ClearReplicatesAndCollapsesWhateverPrecededIt()
        {
            var server = new VaporNetworkList<int>(new[] { 1, 2, 3 }, _server);
            var client = new VaporNetworkList<int>(new[] { 1, 2, 3 }, _client);

            server.Add(4);
            server.Add(5);
            server.Clear();
            Transfer(server, client, full: false);

            Assert.AreEqual(0, client.Count);
        }

        [Test]
        public void SetToAnEqualValueSendsNothing()
        {
            var server = new VaporNetworkList<int>(new[] { 1, 2 }, _server);
            var client = new VaporNetworkList<int>(new[] { 1, 2 }, _client);

            server.Set(0, 1);

            Assert.IsFalse(server.IsDirty, "an assignment that changes nothing should not dirty the list");

            server.Set(0, 1, forceUpdate: true);
            Assert.IsTrue(server.IsDirty, "forceUpdate should replicate regardless");

            Transfer(server, client, full: false);
            CollectionAssert.AreEqual(new[] { 1, 2 }, ToArray(client));
        }

        /// <summary>
        /// More changes than elements means the snapshot is the smaller payload. The receiver is told
        /// which shape it got, so the switch is invisible to it.
        /// </summary>
        [Test]
        public void ADeltaBiggerThanTheListFallsBackToASnapshot()
        {
            var server = new VaporNetworkList<int>(_server);
            var client = new VaporNetworkList<int>(new[] { 99, 98, 97 }, _client);

            server.Add(1);
            server.Add(2);
            server.RemoveAt(0);
            server.Add(3);

            Transfer(server, client, full: false);

            CollectionAssert.AreEqual(ToArray(server), ToArray(client));
            CollectionAssert.AreEqual(new[] { 2, 3 }, ToArray(client));
        }

        [Test]
        public void SuccessiveDeltasStayInStep()
        {
            var server = new VaporNetworkList<int>(new[] { 1, 2, 3, 4, 5 }, _server);
            var client = new VaporNetworkList<int>(new[] { 1, 2, 3, 4, 5 }, _client);

            server.Add(6);
            Transfer(server, client, full: false);

            server.RemoveAt(2);
            Transfer(server, client, full: false);

            server.Set(1, 42);
            Transfer(server, client, full: false);

            CollectionAssert.AreEqual(ToArray(server), ToArray(client));
        }

        #endregion

        #region - Change notification -

        [Test]
        public void TheServerIsToldAboutItsOwnChanges()
        {
            var server = new VaporNetworkList<int>(_server);
            var seen = new List<NetworkListEvent<int>>();
            server.OnListChanged += seen.Add;

            server.Add(1);
            server.Insert(0, 0);
            server.Set(1, 9);
            server.RemoveAt(0);
            server.Clear();

            Assert.AreEqual(5, seen.Count);
            Assert.AreEqual(NetworkListEvent<int>.EventType.Add, seen[0].Type);
            Assert.AreEqual(NetworkListEvent<int>.EventType.Insert, seen[1].Type);
            Assert.AreEqual(NetworkListEvent<int>.EventType.Value, seen[2].Type);
            Assert.AreEqual(NetworkListEvent<int>.EventType.RemoveAt, seen[3].Type);
            Assert.AreEqual(NetworkListEvent<int>.EventType.Clear, seen[4].Type);
        }

        [Test]
        public void TheReceiverIsToldWhatChanged()
        {
            var server = new VaporNetworkList<int>(new[] { 1, 2, 3 }, _server);
            var client = new VaporNetworkList<int>(new[] { 1, 2, 3 }, _client);

            var seen = new List<NetworkListEvent<int>>();
            client.OnListChanged += seen.Add;

            server.Set(1, 20);
            Transfer(server, client, full: false);

            Assert.AreEqual(1, seen.Count);
            Assert.AreEqual(NetworkListEvent<int>.EventType.Value, seen[0].Type);
            Assert.AreEqual(1, seen[0].Index);
            Assert.AreEqual(20, seen[0].Value);
            Assert.AreEqual(2, seen[0].PreviousValue);
        }

        [Test]
        public void ASnapshotTellsTheReceiverTheWholeListChanged()
        {
            var server = new VaporNetworkList<int>(new[] { 1 }, _server);
            var client = new VaporNetworkList<int>(_client);

            var seen = new List<NetworkListEvent<int>>();
            client.OnListChanged += seen.Add;

            Transfer(server, client, full: true);

            Assert.AreEqual(1, seen.Count);
            Assert.AreEqual(NetworkListEvent<int>.EventType.Full, seen[0].Type);
        }

        #endregion

        #region - Access and permission -

        [Test]
        public void ReadsBehaveLikeAList()
        {
            var list = new VaporNetworkList<int>(new[] { 4, 5, 6 }, _server);

            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(5, list[1]);
            Assert.IsTrue(list.Contains(6));
            Assert.IsFalse(list.Contains(7));
            Assert.AreEqual(2, list.IndexOf(6));
            Assert.AreEqual(-1, list.IndexOf(7));
            CollectionAssert.AreEqual(new[] { 4, 5, 6 }, ToArray(list));
        }

        [Test]
        public void AClientCannotMutateTheList()
        {
            var spawnedClient = new TestNetworkObject
            {
                NetworkObjectId = 2,
                IsServer = false,
                NetworkMessages = _messages,
            };

            var list = new VaporNetworkList<int>(new[] { 1 }, spawnedClient);

            LogAssert.Expect(LogType.Error, new Regex("Write Permission"));
            list.Add(2);

            Assert.AreEqual(1, list.Count, "a client-side mutation must not take effect locally either");
            spawnedClient.Destroy();
        }

        /// <summary>
        /// Seeding in OnPreSpawn happens before there is any authority to check, and the contents ride
        /// out on the spawn snapshot rather than as a delta.
        /// </summary>
        [Test]
        public void MutatingBeforeSpawnIsAllowedAndQueuesNothing()
        {
            var unspawned = new TestNetworkObject();
            var list = new VaporNetworkList<int>(unspawned);

            list.Add(1);
            list.Add(2);

            Assert.AreEqual(2, list.Count);
            Assert.IsFalse(list.IsDirty, "nothing is queued before the object exists on the wire");
            unspawned.Destroy();
        }

        [Test]
        public void DestroyingTheOwnerReleasesTheNativeMemory()
        {
            var owner = new TestNetworkObject();
            var list = new VaporNetworkList<int>(new[] { 1, 2, 3 }, owner);

            owner.Destroy();

            // Touching a released NativeList trips the collections safety checks, which is the only
            // observable proof the owner actually disposed it rather than just dropping the reference.
            Assert.Catch(() =>
            {
                int _ = list.Count;
            });
        }

        #endregion

        #region - Harness -

        /// <summary>
        /// Writes one variable and reads it back the way <see cref="VaporNetworkObject"/> would,
        /// consuming the leading variable id before handing the rest to the receiver.
        /// </summary>
        private static void Transfer(VaporNetworkList<int> from, VaporNetworkList<int> to, bool full)
        {
            var writer = new FastBufferWriter(1024, Allocator.Temp, 1024 * 64);
            try
            {
                if (full)
                {
                    from.WriteFull(writer);
                }
                else
                {
                    from.Write(writer);
                }

                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    ByteUnpacker.ReadValueBitPacked(reader, out uint networkVariableId);
                    Assert.AreEqual(from.NetworkVariableId, networkVariableId,
                        "every Write must lead with its variable id or it desyncs the rest of the packet");
                    to.Read(reader);
                }
                finally
                {
                    reader.Dispose();
                }
            }
            finally
            {
                writer.Dispose();
            }

            Assert.IsFalse(from.IsDirty, "writing should clear the dirty state");
        }

        private static int[] ToArray(VaporNetworkList<int> list)
        {
            var values = new int[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                values[i] = list[i];
            }

            return values;
        }

        private sealed class TestNetworkObject : VaporNetworkObject
        {
            protected internal override bool ShouldTick => false;

            public void Destroy() => InternalDestroy();

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
