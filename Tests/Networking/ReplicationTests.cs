using System;
using System.Collections.Generic;
using NUnit.Framework;
using Vapor.Networking;

namespace Vapor.Tests.Networking
{
    /// <summary>
    /// The object model over the wire: a server world and client worlds joined by loopback sessions.
    /// Spawn scope, state deltas, components, ownership, rpcs, interest, late joiners, disconnects
    /// and backpressure — the guarantees the actor layer will stand on.
    /// </summary>
    [TestFixture]
    public class ReplicationTests
    {
        #region - Probe types -

        private sealed partial class Probe : VaporNetworkObject
        {
            public const uint PingHash = 0x50494E47u;   // "PING"
            public readonly VaporNetworkVariable<int> Health;
            public readonly VaporNetworkVariable<string> Label;
            public int CustomValue;
            public readonly List<(int, int)> HealthChanges = new();
            public readonly List<int> Pings = new();
            public readonly List<(ulong, ulong)> OwnerChanges = new();
            public int Spawns, Despawns, Ticks;

            public Probe()
            {
                Health = new VaporNetworkVariable<int>(this, 100);
                Label = new VaporNetworkVariable<string>(this, "probe");
                Health.OnValueChanged += (a, b) => HealthChanges.Add((a, b));
            }

            public override bool WantsNetworkTick => true;
            protected override void OnSpawn() => Spawns++;
            protected override void OnDespawn() => Despawns++;
            protected override void OnNetworkTick(uint tick, double dt) => Ticks++;
            protected override void OnOwnershipChanged(ulong previous, ulong current) => OwnerChanges.Add((previous, current));
            protected override void WriteState(NetworkWriter writer, bool full) => writer.WriteVarInt32(CustomValue);
            protected override void ReadState(NetworkReader reader, bool full) => CustomValue = reader.ReadVarInt32();
            public void SetCustom(int value) { CustomValue = value; MarkDirty(); }

            // What the generator will emit, by hand.
            public void PingRpc(int value, RpcTarget target, Delivery delivery = Delivery.ReliableSequenced)
            {
                if (!BeginSendRpc(PingHash, out var w)) return;
                w.WriteVarInt32(value);
                if (!EndSendRpc(w, target, delivery)) return;
                PingRpc_Implementation(value);
            }

            private void PingRpc_Implementation(int value) => Pings.Add(value);

            public static void PingRpc_Receive(IRpcHost target, NetworkReader reader) => ((Probe)target).PingRpc_Implementation(reader.ReadVarInt32());
        }

        private sealed partial class ProbeComponent : NetworkComponent
        {
            public const uint BoostHash = 0x424F4F53u;   // "BOOS"
            public readonly VaporNetworkVariable<float> Speed;
            public string Tag;
            public readonly List<float> Boosts = new();
            public int Spawns, Despawns, Attaches, Detaches;

            public ProbeComponent() { Speed = new VaporNetworkVariable<float>(this, 1f); }
            protected override void OnAttach() => Attaches++;
            protected override void OnDetach() => Detaches++;
            protected override void OnSpawn() => Spawns++;
            protected override void OnDespawn() => Despawns++;
            protected override void WriteState(NetworkWriter writer, bool full) => writer.WriteString(Tag);
            protected override void ReadState(NetworkReader reader, bool full) => Tag = reader.ReadString();
            public void SetTag(string tag) { Tag = tag; MarkDirty(); }

            public void BoostRpc(float amount, RpcTarget target)
            {
                if (!BeginSendRpc(BoostHash, out var w)) return;
                w.WriteSingle(amount);
                if (!EndSendRpc(w, target, Delivery.ReliableSequenced)) return;
                BoostRpc_Implementation(amount);
            }

            private void BoostRpc_Implementation(float amount) => Boosts.Add(amount);
            public static void BoostRpc_Receive(IRpcHost target, NetworkReader reader) => ((ProbeComponent)target).BoostRpc_Implementation(reader.ReadSingle());
        }

        private sealed class SubProbe : VaporNetworkObject { }

        #endregion

        #region - Harness -

        private sealed class Client
        {
            public LoopbackTransport Transport;
            public NetworkSession Session;
            public NetworkWorld World;
            public ulong ClientId => Session.LocalClientId;
        }

        private ManualClock _clock;
        private LoopbackNetwork _network;
        private LoopbackTransport _serverTransport;
        private NetworkSession _serverSession;
        private NetworkWorld _server;
        private readonly List<Client> _clients = new();

        [OneTimeSetUp]
        public void RegisterRpcs()
        {
            RpcRegistry.Register(Probe.PingHash, Probe.PingRpc_Receive, "Probe.PingRpc");
            RpcRegistry.Register(ProbeComponent.BoostHash, ProbeComponent.BoostRpc_Receive, "ProbeComponent.BoostRpc");
        }

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock();
            _network = new LoopbackNetwork(_clock, seed: 11);
            _serverTransport = _network.CreateTransport();
            _serverSession = new NetworkSession(_serverTransport, _clock, new SessionConfig { TickRate = 30 });
            Assert.IsTrue(_serverSession.StartServer(TransportEndpoint.Loopback(1)));
            _server = new NetworkWorld(_serverSession);
            _server.BindToSession();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var c in _clients)
            {
                c.World.Dispose();
                c.Session.Dispose();
            }

            _clients.Clear();
            _server.Dispose();
            _serverSession.Dispose();
        }

        private Client AddClient()
        {
            var transport = _network.CreateTransport();
            var session = new NetworkSession(transport, _clock, new SessionConfig { TickRate = 30 });
            Assert.IsTrue(session.StartClient(TransportEndpoint.Loopback(1)));
            var world = new NetworkWorld(session);
            world.BindToSession();
            var client = new Client { Transport = transport, Session = session, World = world };
            _clients.Add(client);
            Steps(4);
            Assert.IsTrue(session.IsConnected, "client did not connect");
            return client;
        }

        /// <summary>One network tick: advance the clock by a tick and update every session (server first).</summary>
        private void Step()
        {
            _clock.Advance(1.0 / 30.0 + 1e-6);
            _serverSession.Update();
            foreach (var c in _clients) c.Session.Update();
        }

        private void Steps(int count)
        {
            for (int i = 0; i < count; i++) Step();
        }

        private static T Only<T>(NetworkWorld world) where T : VaporNetworkObject
        {
            T found = null;
            foreach (var o in world.Objects)
            {
                if (o is T typed)
                {
                    Assert.IsNull(found, "more than one");
                    found = typed;
                }
            }

            Assert.IsNotNull(found, $"no {typeof(T).Name} in world");
            return found;
        }

        #endregion

        #region - Spawn and state -

        [Test]
        public void ASpawnArrivesWithIdentityVariablesCustomStateAndComponents()
        {
            var client = AddClient();
            var probe = new Probe { CustomValue = 7 };
            var component = probe.AddComponent(new ProbeComponent { Tag = "swift" });
            component.Speed.Value = 2.5f;
            probe.Label.Value = "hero";
            _server.Spawn(probe, ownerClientId: client.ClientId);
            Assert.AreEqual(1, probe.Spawns);
            Assert.AreEqual(1, component.Spawns);

            Steps(2);

            var mirror = Only<Probe>(client.World);
            Assert.AreEqual(probe.NetworkObjectId, mirror.NetworkObjectId);
            Assert.AreEqual(client.ClientId, mirror.OwnerClientId);
            Assert.IsTrue(mirror.IsOwner);
            Assert.IsFalse(mirror.IsAuthority);
            Assert.AreEqual(100, mirror.Health.Value);
            Assert.AreEqual("hero", mirror.Label.Value);
            Assert.AreEqual(7, mirror.CustomValue);
            Assert.AreEqual(1, mirror.Spawns);

            var mirrorComponent = mirror.Get<ProbeComponent>();
            Assert.IsNotNull(mirrorComponent);
            Assert.AreEqual(component.ComponentId, mirrorComponent.ComponentId);
            Assert.AreEqual(2.5f, mirrorComponent.Speed.Value);
            Assert.AreEqual("swift", mirrorComponent.Tag);
            Assert.AreEqual(1, mirrorComponent.Spawns);
            Assert.AreEqual(1, mirrorComponent.Attaches);
        }

        [Test]
        public void VariableAndCustomStateChangesReplicateAsDeltas()
        {
            var client = AddClient();
            var probe = new Probe();
            _server.Spawn(probe);
            Steps(2);
            var mirror = Only<Probe>(client.World);
            mirror.HealthChanges.Clear();

            probe.Health.Value = 42;
            probe.SetCustom(9);
            Steps(2);

            Assert.AreEqual(42, mirror.Health.Value);
            Assert.AreEqual(9, mirror.CustomValue);
            CollectionAssert.AreEqual(new[] { (100, 42) }, mirror.HealthChanges);

            // Unchanged writes send nothing and fire nothing.
            probe.Health.Value = 42;
            Steps(2);
            Assert.AreEqual(1, mirror.HealthChanges.Count);
        }

        [Test]
        public void ClientsCannotWriteVariables()
        {
            var client = AddClient();
            _server.Spawn(new Probe());
            Steps(2);
            var mirror = Only<Probe>(client.World);
            Assert.Throws<InvalidOperationException>(() => mirror.Health.Value = 1);
        }

        [Test]
        public void ComponentsCanBeAddedRemovedAndUpdatedAtRuntime()
        {
            var client = AddClient();
            var probe = new Probe();
            _server.Spawn(probe);
            Steps(2);
            var mirror = Only<Probe>(client.World);
            Assert.AreEqual(0, mirror.Components.Count);

            var component = probe.AddComponent(new ProbeComponent { Tag = "late" });
            Steps(2);
            var mirrorComponent = mirror.Get<ProbeComponent>();
            Assert.IsNotNull(mirrorComponent);
            Assert.AreEqual("late", mirrorComponent.Tag);
            Assert.AreEqual(1, mirrorComponent.Spawns);

            component.Speed.Value = 9f;
            component.SetTag("later");
            Steps(2);
            Assert.AreEqual(9f, mirrorComponent.Speed.Value);
            Assert.AreEqual("later", mirrorComponent.Tag);

            probe.RemoveComponent(component);
            Assert.AreEqual(1, component.Despawns);
            Assert.AreEqual(1, component.Detaches);
            Steps(2);
            Assert.IsNull(mirror.Get<ProbeComponent>());
            Assert.AreEqual(1, mirrorComponent.Despawns);
            Assert.AreEqual(1, mirrorComponent.Detaches);
        }

        [Test]
        public void ComponentLookupHonoursInterfacesAndDuplicates()
        {
            var probe = new Probe();
            var a = probe.AddComponent(new ProbeComponent { Tag = "a" });
            var b = probe.AddComponent(new ProbeComponent { Tag = "b" });
            Assert.AreSame(a, probe.Get<ProbeComponent>());
            Assert.AreSame(a, probe.Get<INetworkVariableHost>());
            var all = new List<ProbeComponent>();
            probe.GetAll(all);
            CollectionAssert.AreEqual(new[] { a, b }, all);
            Assert.AreSame(b, probe.GetComponentById(b.ComponentId));
            Assert.AreNotEqual(a.ComponentId, b.ComponentId);
            Assert.IsTrue(probe.Has<ProbeComponent>());
            probe.RemoveComponent(a);
            Assert.AreSame(b, probe.Get<ProbeComponent>());
        }

        [Test]
        public void DespawnCascadesToSubObjectsOnBothSides()
        {
            var client = AddClient();
            var probe = new Probe();
            _server.Spawn(probe);
            var sub = new SubProbe();
            _server.Spawn(sub, parent: probe);
            Steps(2);
            Assert.AreEqual(2, client.World.Count);
            var mirrorSub = Only<SubProbe>(client.World);
            Assert.AreEqual(probe.NetworkObjectId, mirrorSub.ParentNetworkObjectId);
            Assert.IsFalse(mirrorSub.IsRoot);

            _server.Despawn(probe);
            Assert.IsFalse(sub.IsSpawned);
            Assert.AreEqual(1, probe.Despawns);
            Assert.AreEqual(0, _server.Count);
            Steps(2);
            Assert.AreEqual(0, client.World.Count);
        }

        [Test]
        public void TicksReachObjectsThatWantThem()
        {
            var client = AddClient();
            var probe = new Probe();
            _server.Spawn(probe);
            Steps(5);
            Assert.GreaterOrEqual(probe.Ticks, 4);
            var mirror = Only<Probe>(client.World);
            Assert.GreaterOrEqual(mirror.Ticks, 1);
        }

        #endregion

        #region - Ownership -

        [Test]
        public void OwnershipTransfersAndBothSidesHearAboutIt()
        {
            var client = AddClient();
            var probe = new Probe();
            _server.Spawn(probe);
            Steps(2);
            var mirror = Only<Probe>(client.World);
            Assert.IsFalse(mirror.IsOwner);

            _server.ChangeOwnership(probe, client.ClientId);
            CollectionAssert.AreEqual(new[] { (0ul, client.ClientId) }, probe.OwnerChanges);
            Steps(2);
            Assert.AreEqual(client.ClientId, mirror.OwnerClientId);
            Assert.IsTrue(mirror.IsOwner);
            CollectionAssert.AreEqual(new[] { (0ul, client.ClientId) }, mirror.OwnerChanges);
        }

        [Test]
        public void OwnerOnlyObjectsFollowTheirOwner()
        {
            var first = AddClient();
            var second = AddClient();
            var probe = new Probe();
            _server.Spawn(probe, ownerClientId: first.ClientId, spawnedOnlyOnOwner: true);
            Steps(2);
            Assert.AreEqual(1, first.World.Count);
            Assert.AreEqual(0, second.World.Count);

            _server.ChangeOwnership(probe, second.ClientId);
            Steps(2);
            Assert.AreEqual(0, first.World.Count);
            Assert.AreEqual(1, second.World.Count);
            Assert.IsTrue(Only<Probe>(second.World).IsOwner);
        }

        [Test]
        public void ADepartingClientsObjectsAreDespawnedAfterTheLeavingHook()
        {
            var client = AddClient();
            var probe = new Probe();
            _server.Spawn(probe, ownerClientId: client.ClientId);
            var kept = new Probe();
            _server.Spawn(kept, ownerClientId: client.ClientId);
            _server.ClientLeaving += id => _server.ChangeOwnership(kept, NetworkSession.ServerClientId);
            Steps(2);

            client.Session.Disconnect();
            Steps(3);
            Assert.IsFalse(probe.IsSpawned);
            Assert.IsTrue(kept.IsSpawned);
            Assert.AreEqual(1, _server.Count);
        }

        #endregion

        #region - Rpcs -

        [Test]
        public void ServerRpcsReachTheRightPeers()
        {
            var owner = AddClient();
            var other = AddClient();
            var probe = new Probe();
            _server.Spawn(probe, ownerClientId: owner.ClientId);
            Steps(2);
            var onOwner = Only<Probe>(owner.World);
            var onOther = Only<Probe>(other.World);

            probe.PingRpc(1, RpcTarget.Everyone);
            probe.PingRpc(2, RpcTarget.Owner);
            probe.PingRpc(3, RpcTarget.NotOwner);
            probe.PingRpc(4, RpcTarget.NotServer);
            probe.PingRpc(5, RpcTarget.Server);
            probe.PingRpc(6, RpcTarget.Me);
            probe.PingRpc(7, RpcTarget.NotMe);
            Steps(2);

            CollectionAssert.AreEqual(new[] { 1, 3, 5, 6 }, probe.Pings);
            CollectionAssert.AreEqual(new[] { 1, 2, 4, 7 }, onOwner.Pings);
            CollectionAssert.AreEqual(new[] { 1, 3, 4, 7 }, onOther.Pings);
        }

        [Test]
        public void ClientRpcsGoToTheServerAndAreProxied()
        {
            var owner = AddClient();
            var other = AddClient();
            var probe = new Probe();
            _server.Spawn(probe, ownerClientId: owner.ClientId);
            Steps(2);
            var onOwner = Only<Probe>(owner.World);
            var onOther = Only<Probe>(other.World);

            onOwner.PingRpc(1, RpcTarget.Server);
            onOwner.PingRpc(2, RpcTarget.Everyone);
            onOwner.PingRpc(3, RpcTarget.NotMe);
            onOwner.PingRpc(4, RpcTarget.Owner);      // owner talking to itself: local only
            onOther.PingRpc(5, RpcTarget.Owner);      // proxied to the owner
            onOwner.PingRpc(6, RpcTarget.NotServer);
            onOwner.PingRpc(7, RpcTarget.Me);
            Steps(3);

            // 5 was Owner-targeted by a non-owner: the server proxies it to the owner and does not run it.
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, probe.Pings);
            CollectionAssert.AreEqual(new[] { 2, 4, 6, 7, 5 }, onOwner.Pings);
            CollectionAssert.AreEqual(new[] { 2, 3, 6 }, onOther.Pings);
        }

        [Test]
        public void ComponentRpcsAreAddressedToTheComponent()
        {
            var client = AddClient();
            var probe = new Probe();
            var component = probe.AddComponent(new ProbeComponent());
            _server.Spawn(probe);
            Steps(2);
            var mirrorComponent = Only<Probe>(client.World).Get<ProbeComponent>();

            component.BoostRpc(1.5f, RpcTarget.Everyone);
            Steps(2);
            CollectionAssert.AreEqual(new[] { 1.5f }, component.Boosts);
            CollectionAssert.AreEqual(new[] { 1.5f }, mirrorComponent.Boosts);

            mirrorComponent.BoostRpc(2.5f, RpcTarget.Server);
            Steps(2);
            CollectionAssert.AreEqual(new[] { 1.5f, 2.5f }, component.Boosts);
        }

        [Test]
        public void UnreliableRpcsAlsoArrive()
        {
            var client = AddClient();
            var probe = new Probe();
            _server.Spawn(probe);
            Steps(2);
            probe.PingRpc(9, RpcTarget.NotServer, Delivery.Unreliable);
            Steps(2);
            CollectionAssert.AreEqual(new[] { 9 }, Only<Probe>(client.World).Pings);
        }

        [Test]
        public void AnRpcOnAnUnspawnedObjectIsRefused()
        {
            var probe = new Probe();
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("before it was spawned"));
            probe.PingRpc(1, RpcTarget.Everyone);
            Assert.IsEmpty(probe.Pings);
        }

        #endregion

        #region - Interest -

        [Test]
        public void ChannelsGateSpawnAndDespawn()
        {
            var client = AddClient();
            var zone = new InterestGroup("Zone.1");
            var probe = new Probe();
            probe.AddInterestGroup(zone);
            _server.Spawn(probe);
            Steps(2);
            Assert.AreEqual(0, client.World.Count);

            Assert.IsTrue(_server.Interest.Subscribe(client.ClientId, zone));
            Steps(2);
            Assert.AreEqual(1, client.World.Count);
            Assert.IsTrue(_server.Interest.IsObserving(client.ClientId, probe));

            Assert.IsTrue(_server.Interest.Unsubscribe(client.ClientId, zone));
            Steps(2);
            Assert.AreEqual(0, client.World.Count);

            // Dropping the group makes it global again.
            probe.ClearInterestGroups();
            Steps(2);
            Assert.AreEqual(1, client.World.Count);
        }

        [Test]
        public void SpatialObjectsUseTheProviderWhenThereIsOne()
        {
            var client = AddClient();
            var near = new Probe { UsesSpatialRelevance = true };
            var far = new Probe { UsesSpatialRelevance = true, CustomValue = 1 };
            _server.Spawn(near);
            _server.Spawn(far);
            Steps(2);
            Assert.AreEqual(2, client.World.Count, "no provider: spatial objects behave as global");

            var provider = new SetRelevance();
            provider.Relevant.Add(near.NetworkObjectId);
            _server.Interest.Spatial = provider;
            _server.Interest.RefreshSpatial();
            Steps(2);
            Assert.AreEqual(1, client.World.Count);
            Assert.AreEqual(near.NetworkObjectId, Only<Probe>(client.World).NetworkObjectId);

            provider.Relevant.Add(far.NetworkObjectId);
            _server.Interest.RefreshSpatial();
            Steps(2);
            Assert.AreEqual(2, client.World.Count);
        }

        private sealed class SetRelevance : ISpatialRelevance
        {
            public readonly HashSet<ulong> Relevant = new();
            public bool IsRelevant(VaporNetworkObject networkObject, ulong clientId, bool currentlyObserving) => Relevant.Contains(networkObject.NetworkObjectId);
        }

        [Test]
        public void TheGridSpawnsAndDespawnsAsFocusMoves()
        {
            var client = AddClient();
            var grid = new SpatialInterestGrid(cellSize: 10f, radiusCells: 2, hysteresisCells: 1);
            _server.Interest.Spatial = grid;

            var near = new Probe { UsesSpatialRelevance = true, CustomValue = 1 };
            var far = new Probe { UsesSpatialRelevance = true, CustomValue = 2 };
            _server.Spawn(near);
            _server.Spawn(far);
            grid.SetPosition(near.NetworkObjectId, new UnityEngine.Vector3(5, 0, 5));
            grid.SetPosition(far.NetworkObjectId, new UnityEngine.Vector3(95, 0, 5));
            grid.SetFocus(client.ClientId, new UnityEngine.Vector3(0, 0, 0));
            _server.Interest.RefreshSpatial();
            Steps(2);
            Assert.AreEqual(1, client.World.Count);
            Assert.AreEqual(1, Only<Probe>(client.World).CustomValue);

            // Walk towards the far one: at x=75 the far object (cell 9) is 2 cells from focus cell 7 -> discovered.
            grid.SetFocus(client.ClientId, new UnityEngine.Vector3(75, 0, 0));
            _server.Interest.RefreshSpatial();
            Steps(2);
            Assert.AreEqual(1, client.World.Count);
            Assert.AreEqual(2, Only<Probe>(client.World).CustomValue);

            // Step back one cell: 3 cells away, inside the hysteresis ring -> still observed.
            grid.SetFocus(client.ClientId, new UnityEngine.Vector3(65, 0, 0));
            _server.Interest.RefreshSpatial();
            Steps(2);
            Assert.AreEqual(1, client.World.Count);

            // Another cell back: 4 away -> gone.
            grid.SetFocus(client.ClientId, new UnityEngine.Vector3(55, 0, 0));
            _server.Interest.RefreshSpatial();
            Steps(2);
            Assert.AreEqual(0, client.World.Count);
        }

        [Test]
        public void TheGridComputesDistanceAndLodTiers()
        {
            var grid = new SpatialInterestGrid(cellSize: 32f);
            grid.SetPosition(1, new UnityEngine.Vector3(0, 0, 0));
            grid.SetPosition(2, new UnityEngine.Vector3(100, 0, 0));
            grid.SetFocus(7, new UnityEngine.Vector3(0, 0, 10));
            Assert.AreEqual(10f, grid.Distance(1, 7), 1e-4f);
            Assert.AreEqual(float.PositiveInfinity, grid.Distance(3, 7));
            Assert.AreEqual(float.PositiveInfinity, grid.Distance(1, 8));

            var profile = NetworkLodProfile.Default;
            Assert.AreEqual(1f, profile.ScaleFor(10f));
            Assert.AreEqual(0.5f, profile.ScaleFor(45f));
            Assert.AreEqual(0.25f, profile.ScaleFor(100f));
            Assert.AreEqual(0.05f, profile.ScaleFor(500f));

            var near = new List<ulong>();
            grid.CollectNear(7, 1, near);
            CollectionAssert.AreEqual(new[] { 1ul }, near);
            grid.Remove(1);
            Assert.AreEqual(1, grid.ObjectCount);
        }

        #endregion

        #region - Snapshot channel -

        private sealed class Mover : VaporNetworkObject
        {
            public UnityEngine.Vector3 Position;
            public readonly List<(uint tick, ulong from, UnityEngine.Vector3 pos)> Received = new();
            public int Written;
            public bool OwnerDriven;

            public override bool HasSnapshotChannel => true;
            public override bool OwnerWritesSnapshots => OwnerDriven;
            protected override void WriteSpawnData(NetworkWriter writer) => writer.WriteBool(OwnerDriven);
            protected override void ReadSpawnData(NetworkReader reader) => OwnerDriven = reader.ReadBool();

            protected override void WriteSnapshot(NetworkWriter writer, uint tick, ulong forClientId)
            {
                Written++;
                writer.WriteVector3(Position);
            }

            protected override void ReadSnapshot(NetworkReader reader, uint tick, ulong fromClientId)
            {
                Position = reader.ReadVector3();
                Received.Add((tick, fromClientId, Position));
            }
        }

        [Test]
        public void SnapshotsFlowServerToClientsAtTheAuthoredRate()
        {
            var client = AddClient();
            var mover = new Mover { SnapshotRateHz = 15f };
            _server.Spawn(mover);
            Steps(2);
            var mirror = Only<Mover>(client.World);
            mirror.Received.Clear();

            for (int i = 0; i < 30; i++)
            {
                mover.Position = new UnityEngine.Vector3(i, 0, 0);
                Step();
            }

            // 30 ticks at 30 Hz with a 15 Hz object: about 15 snapshots, each carrying the latest position.
            Assert.GreaterOrEqual(mirror.Received.Count, 13);
            Assert.LessOrEqual(mirror.Received.Count, 16);
            Assert.AreEqual(mover.Position.x, mirror.Position.x, 1.5f);
            foreach (var (_, from, _) in mirror.Received)
            {
                Assert.AreEqual(NetworkSession.ServerClientId, from);
            }
        }

        [Test]
        public void LodScalesSnapshotRatePerClient()
        {
            var near = AddClient();
            var far = AddClient();
            var grid = new SpatialInterestGrid(cellSize: 100f, radiusCells: 10);
            _server.Interest.Spatial = grid;
            _server.Interest.Lod = grid;

            var mover = new Mover { SnapshotRateHz = 30f, UsesSpatialRelevance = true };
            _server.Spawn(mover);
            grid.SetPosition(mover.NetworkObjectId, UnityEngine.Vector3.zero);
            grid.SetFocus(near.ClientId, new UnityEngine.Vector3(5, 0, 0));      // tier 1.0
            grid.SetFocus(far.ClientId, new UnityEngine.Vector3(100, 0, 0));     // tier 0.25
            _server.Interest.RefreshSpatial();
            Steps(2);
            var onNear = Only<Mover>(near.World);
            var onFar = Only<Mover>(far.World);
            onNear.Received.Clear();
            onFar.Received.Clear();

            Steps(40);
            Assert.GreaterOrEqual(onNear.Received.Count, 36);
            Assert.LessOrEqual(onFar.Received.Count, 12);
            Assert.GreaterOrEqual(onFar.Received.Count, 8);
        }

        [Test]
        public void OwnerAuthoritativeSnapshotsReachTheServerAndAreRelayed()
        {
            var owner = AddClient();
            var watcher = AddClient();
            var mover = new Mover { OwnerDriven = true, SnapshotRateHz = 30f };
            _server.Spawn(mover, ownerClientId: owner.ClientId);
            Steps(2);
            var onOwner = Only<Mover>(owner.World);
            var onWatcher = Only<Mover>(watcher.World);
            Assert.IsTrue(onOwner.OwnerDriven);
            Assert.IsTrue(onOwner.IsOwner);

            for (int i = 1; i <= 20; i++)
            {
                onOwner.Position = new UnityEngine.Vector3(0, 0, i);
                Step();
            }

            Assert.Greater(mover.Received.Count, 10, "server received owner snapshots");
            foreach (var (_, from, _) in mover.Received)
            {
                Assert.AreEqual(owner.ClientId, from);
            }

            Assert.AreEqual(mover.Position.z, onOwner.Position.z, 2f);
            Assert.Greater(onWatcher.Received.Count, 10, "watcher received relayed snapshots");
            Assert.AreEqual(mover.Position.z, onWatcher.Position.z, 2f);
            // The owner is never sent its own state back.
            Assert.AreEqual(0, onOwner.Received.Count);
        }

        [Test]
        public void ANonOwnerCannotPushSnapshots()
        {
            var owner = AddClient();
            var other = AddClient();
            var mover = new Mover { OwnerDriven = true };
            _server.Spawn(mover, ownerClientId: owner.ClientId);
            Steps(2);
            var onOther = Only<Mover>(other.World);
            // 'other' is not the owner, so its world never writes owner snapshots; only the owner's arrive.
            onOther.Position = new UnityEngine.Vector3(99, 99, 99);
            Steps(5);
            Assert.Greater(mover.Received.Count, 0);
            foreach (var (_, from, pos) in mover.Received)
            {
                Assert.AreEqual(owner.ClientId, from);
                Assert.AreNotEqual(99f, pos.x);
            }
        }

        [Test]
        public void ALateJoinerReceivesTheExistingWorld()
        {
            var probe = new Probe();
            probe.AddComponent(new ProbeComponent { Tag = "old" });
            _server.Spawn(probe);
            var sub = new SubProbe();
            _server.Spawn(sub, parent: probe);
            probe.Health.Value = 5;
            Steps(2);

            var late = AddClient();
            Steps(2);
            Assert.AreEqual(2, late.World.Count);
            var mirror = Only<Probe>(late.World);
            Assert.AreEqual(5, mirror.Health.Value);
            Assert.AreEqual("old", mirror.Get<ProbeComponent>().Tag);
            Assert.AreEqual(1, mirror.SubObjects.Count);
        }

        #endregion

        #region - Offline -

        [Test]
        public void AnOfflineWorldIsItsOwnAuthorityAndRunsRpcsLocally()
        {
            using var offline = new NetworkWorld();
            Assert.IsTrue(offline.IsOffline);
            Assert.IsTrue(offline.IsAuthority);
            Assert.IsNull(offline.Interest);

            var probe = new Probe();
            var component = probe.AddComponent(new ProbeComponent());
            offline.Spawn(probe);
            Assert.IsTrue(probe.IsSpawned);
            Assert.IsTrue(probe.IsOwner);
            Assert.IsTrue(probe.IsServer && probe.IsClient);

            probe.Health.Value = 3;
            probe.PingRpc(1, RpcTarget.Server);
            probe.PingRpc(2, RpcTarget.NotServer);
            component.BoostRpc(4f, RpcTarget.Owner);
            CollectionAssert.AreEqual(new[] { 1, 2 }, probe.Pings);
            CollectionAssert.AreEqual(new[] { 4f }, component.Boosts);

            offline.NetworkTick(1, 0.033);
            offline.Despawn(probe);
            Assert.IsFalse(probe.IsSpawned);
            Assert.AreEqual(1, probe.Despawns);
        }

        #endregion

        #region - Backpressure -

        [Test]
        public void ABlockedTransportKeepsRecordsInOrderUntilItDrains()
        {
            var client = AddClient();
            _serverTransport.Conditions.SendQueueCapacity = 1;                       // one reliable packet in flight at a time
            _network.SetMaxPayload(Delivery.ReliableFragmentedSequenced, 64);       // ...and each packet holds one or two records

            var probes = new List<Probe>();
            for (int i = 0; i < 20; i++)
            {
                var p = new Probe { CustomValue = i };
                probes.Add(p);
                _server.Spawn(p);
            }

            for (int i = 0; i < 20; i++)
            {
                probes[i].Health.Value = i;
            }

            Steps(60);
            Assert.AreEqual(20, client.World.Count);
            var seen = new List<int>();
            foreach (var o in client.World.Objects)
            {
                var m = (Probe)o;
                seen.Add(m.CustomValue);
                Assert.AreEqual(m.CustomValue, m.Health.Value);
            }

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19 }, seen);
        }

        #endregion
    }
}
