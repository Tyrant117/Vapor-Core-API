using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Vapor.Networking;

namespace Vapor.Tests.Networking
{
    /// <summary>
    /// The generator, end to end inside Unity: a [VaporRpc] declared the way an author writes it, with
    /// a [NetworkSerializable] argument and an object reference, sent over loopback and dispatched
    /// through the generated receive stub.
    /// </summary>
    [TestFixture]
    public partial class GeneratedRpcTests   // partial: the generated halves of the nested types land inside it
    {
        [NetworkSerializable]
        public partial struct HitInfo
        {
            public int Amount;
            public Vector3 Direction;
            [NetworkIgnore] public int Scratch;
        }

        public sealed partial class Turret : VaporNetworkObject
        {
            public readonly List<(ulong shooter, HitInfo hit, Turret target)> Hits = new();

            [VaporRpc(RpcTarget.Everyone)]
            private partial void FireRpc(ulong shooter, HitInfo hit, Turret target);

            private partial void FireRpc_Implementation(ulong shooter, HitInfo hit, Turret target) => Hits.Add((shooter, hit, target));

            public void Fire(ulong shooter, HitInfo hit, Turret target) => FireRpc(shooter, hit, target);
        }

        public sealed partial class Gun : NetworkComponent
        {
            public readonly List<float> Recoils = new();

            [VaporRpc(RpcTarget.Server, Delivery.Unreliable)]
            internal partial void RecoilRpc(float amount);

            private partial void RecoilRpc_Implementation(float amount) => Recoils.Add(amount);
        }

        [Test]
        public void GeneratedFormatterRoundTripsAndIgnoresMarkedMembers()
        {
            var w = new NetworkWriter();
            NetworkFormatters.Write(w, new HitInfo { Amount = 7, Direction = Vector3.up, Scratch = 99 });
            var segment = w.WrittenSegment;
            var back = NetworkFormatters.Read<HitInfo>(new NetworkReader(segment.Array, segment.Offset, segment.Count));
            Assert.AreEqual(7, back.Amount);
            Assert.AreEqual(Vector3.up, back.Direction);
            Assert.AreEqual(0, back.Scratch);
            Assert.AreEqual(4 + 12, w.Length);
        }

        [Test]
        public void GeneratedRpcsRunOnEveryTargetWithResolvedReferences()
        {
            var clock = new ManualClock();
            var network = new LoopbackNetwork(clock, seed: 2);
            var serverSession = new NetworkSession(network.CreateTransport(), clock);
            var clientSession = new NetworkSession(network.CreateTransport(), clock);
            using var server = new NetworkWorld(serverSession);
            using var client = new NetworkWorld(clientSession);
            try
            {
                Assert.IsTrue(serverSession.StartServer(TransportEndpoint.Loopback(3)));
                Assert.IsTrue(clientSession.StartClient(TransportEndpoint.Loopback(3)));
                server.BindToSession();
                client.BindToSession();
                Step(clock, serverSession, clientSession, 4);
                Assert.IsTrue(clientSession.IsConnected);

                var turret = new Turret();
                var gun = turret.AddComponent(new Gun());
                var other = new Turret();
                server.Spawn(turret);
                server.Spawn(other);
                Step(clock, serverSession, clientSession, 2);
                Assert.AreEqual(2, client.Count);
                Assert.IsTrue(client.TryGet(turret.NetworkObjectId, out Turret mirror));
                Assert.IsTrue(client.TryGet(other.NetworkObjectId, out Turret otherMirror));

                turret.Fire(42, new HitInfo { Amount = 3, Direction = Vector3.forward }, other);
                Step(clock, serverSession, clientSession, 2);

                Assert.AreEqual(1, turret.Hits.Count);
                Assert.AreSame(other, turret.Hits[0].target);
                Assert.AreEqual(1, mirror.Hits.Count);
                Assert.AreEqual(42ul, mirror.Hits[0].shooter);
                Assert.AreEqual(3, mirror.Hits[0].hit.Amount);
                Assert.AreEqual(Vector3.forward, mirror.Hits[0].hit.Direction);
                Assert.AreSame(otherMirror, mirror.Hits[0].target, "object argument resolved against the receiving world");

                mirror.Get<Gun>().RecoilRpc(0.5f);
                Step(clock, serverSession, clientSession, 2);
                CollectionAssert.AreEqual(new[] { 0.5f }, gun.Recoils);
                Assert.IsEmpty(mirror.Get<Gun>().Recoils);   // Server-targeted: never runs on the client
            }
            finally
            {
                clientSession.Dispose();
                serverSession.Dispose();
            }
        }

        private static void Step(ManualClock clock, NetworkSession server, NetworkSession client, int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.Advance(1.0 / 30.0 + 1e-6);
                server.Update();
                client.Update();
            }
        }
    }
}
