using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using Vapor.NetworkObjects;
using Vapor.Unsafe;

namespace Vapor.Tests
{
    /// <summary>
    /// The packet wire format, and the opcode scheme that decides what a packet even is.
    /// </summary>
    /// <remarks>
    /// Opcodes used to be a counter incremented in assembly-walk order, so two peers agreed only if
    /// they discovered types in the same sequence. They do not: an editor host loads editor and test
    /// assemblies a built client never sees. The tests below pin the property that replaced it — an
    /// opcode is a function of the type's name and nothing else.
    /// </remarks>
    [TestFixture]
    public class PacketHandlerTests
    {
        #region - Opcodes -

        [Test]
        public void AnOpcodeIsAFunctionOfTheTypeNameAlone()
        {
            Assert.AreEqual(typeof(ProbePacket).FullName.Hash32(), PacketRegistry.OpCodeFor(typeof(ProbePacket)));
        }

        /// <summary>
        /// The property that makes an editor host and a built client agree: nothing about when or in
        /// what order a type was registered reaches the wire.
        /// </summary>
        [Test]
        public void RegistrationOrderDoesNotChangeAnOpcode()
        {
            var before = PacketRegistry.OpCodeFor(typeof(ProbePacket));

            // Registering unrelated types first would have shifted every later id under a counter.
            PacketRegistry.Register(typeof(OtherProbePacket));
            PacketRegistry.Register(typeof(ProbePacket));

            Assert.AreEqual(before, PacketRegistry.OpCodeFor(typeof(ProbePacket)));
            Assert.AreNotEqual(PacketRegistry.OpCodeFor(typeof(ProbePacket)), PacketRegistry.OpCodeFor(typeof(OtherProbePacket)));
        }

        [Test]
        public void PacketTypesAreDiscoveredAutomatically()
        {
            Assert.IsTrue(PacketRegistry.TryGetOpCode(typeof(ProbePacket), out var opCode));
            Assert.AreEqual(PacketRegistry.OpCodeFor(typeof(ProbePacket)), opCode);
            Assert.IsTrue(PacketRegistry.TryGetType(opCode, out var type));
            Assert.AreEqual(typeof(ProbePacket), type);
        }

        [Test]
        public void AKnownOpcodeBuildsItsPacket()
        {
            Assert.IsTrue(PacketRegistry.TryCreate(PacketRegistry.OpCodeFor(typeof(ProbePacket)), out var packet));
            Assert.IsInstanceOf<ProbePacket>(packet);
        }

        [Test]
        public void AnUnknownOpcodeBuildsNothingRatherThanThrowing()
        {
            Assert.IsFalse(PacketRegistry.TryCreate(0xDEADBEEF, out var packet));
            Assert.IsNull(packet);
            Assert.IsNull(PacketRegistry.GetType(0xDEADBEEF));
        }

        #endregion

        #region - Round trip -

        [Test]
        public void APacketRoundTrips()
        {
            var writer = new FastBufferWriter(1024, Allocator.Temp, 64 * 1024);
            try
            {
                PacketHandler.CreatePacket(writer, new ProbePacket { Value = 4242 });

                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    var packet = PacketHandler.FromPacket(reader) as ProbePacket;
                    Assert.IsNotNull(packet);
                    Assert.AreEqual(4242, packet.Value);
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
        }

        [Test]
        public void SeveralPacketsRoundTripBackToBack()
        {
            var writer = new FastBufferWriter(1024, Allocator.Temp, 64 * 1024);
            try
            {
                for (int i = 1; i <= 4; i++)
                {
                    PacketHandler.CreatePacket(writer, new ProbePacket { Value = i * 11 });
                }

                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    for (int i = 1; i <= 4; i++)
                    {
                        var packet = PacketHandler.FromPacket(reader) as ProbePacket;
                        Assert.IsNotNull(packet, $"packet {i}");
                        Assert.AreEqual(i * 11, packet.Value);
                    }
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
        }

        [Test]
        public void ANullPacketRoundTripsAsNull()
        {
            var writer = new FastBufferWriter(64, Allocator.Temp, 1024);
            try
            {
                PacketHandler.CreatePacket(writer, null);

                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    Assert.IsNull(PacketHandler.FromPacket(reader));
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
        }

        [Test]
        public void TheOwnedCopyOverloadSurvivesItsWriter()
        {
            var packet = PacketHandler.CreatePacket(new ProbePacket { Value = 77 });
            try
            {
                // Under the old view-into-a-disposed-writer version this read freed memory and only
                // worked because Temp allocations outlive the frame.
                Assert.Greater(packet.Length, 0);
                var restored = PacketHandler.FromPacket<ProbePacket>(packet);
                Assert.IsNotNull(restored);
                Assert.AreEqual(77, restored.Value);
            }
            finally
            {
                packet.Dispose();
            }
        }

        #endregion

        #region - Recovering from a mismatch -

        /// <summary>
        /// The reason the length prefix exists. A packet whose type this build has never heard of costs
        /// that packet and nothing else — before, it threw out of the message pump.
        /// </summary>
        [Test]
        public void AnUnknownPacketIsSkippedAndTheStreamSurvives()
        {
            var writer = new FastBufferWriter(1024, Allocator.Temp, 64 * 1024);
            try
            {
                WriteUnknownPacket(writer);
                PacketHandler.CreatePacket(writer, new ProbePacket { Value = 999 });

                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    LogAssert.Expect(LogType.Warning, new Regex("unknown opcode"));
                    Assert.IsNull(PacketHandler.FromPacket(reader), "the unknown packet yields nothing");

                    var packet = PacketHandler.FromPacket(reader) as ProbePacket;
                    Assert.IsNotNull(packet, "the packet after it must still be readable");
                    Assert.AreEqual(999, packet.Value);
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
        }

        [Test]
        public void ReadingIntoTheWrongTypeIsRefused()
        {
            var writer = new FastBufferWriter(1024, Allocator.Temp, 64 * 1024);
            try
            {
                PacketHandler.CreatePacket(writer, new ProbePacket { Value = 1 });

                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    var wrongTarget = new OtherProbePacket();
                    LogAssert.Expect(LogType.Error, new Regex("type mismatch"));

                    Assert.IsFalse(PacketHandler.FromPacketInto(reader, wrongTarget));
                    Assert.AreEqual(0, wrongTarget.Other, "nothing should have been written into it");
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
        }

        [Test]
        public void ReadingIntoTheRightTypeFillsIt()
        {
            var writer = new FastBufferWriter(1024, Allocator.Temp, 64 * 1024);
            try
            {
                PacketHandler.CreatePacket(writer, new ProbePacket { Value = 31337 });

                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    var target = new ProbePacket();
                    Assert.IsTrue(PacketHandler.FromPacketInto(reader, target));
                    Assert.AreEqual(31337, target.Value);
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
        }

        #endregion

        #region - Harness -

        /// <summary>Hand-frames a packet with an opcode nothing will recognise.</summary>
        private static void WriteUnknownPacket(FastBufferWriter writer)
        {
            writer.WriteValueSafe(0);
            int start = writer.Position;
            BytePacker.WriteValuePacked(writer, 0xDEADBEEFu);
            writer.WriteValueSafe(12345);
            int end = writer.Position;

            writer.Seek(start - sizeof(int));
            writer.WriteValueSafe(end - start);
            writer.Seek(end);
        }

        private sealed class ProbePacket : INetworkPacket
        {
            public int Value;

            public void Serialize(FastBufferWriter writer, bool fullPacket) => writer.WriteValueSafe(Value);

            public void Deserialize(FastBufferReader reader, out bool fullPacket)
            {
                fullPacket = true;
                reader.ReadValueSafe(out Value);
            }
        }

        private sealed class OtherProbePacket : INetworkPacket
        {
            public long Other;

            public void Serialize(FastBufferWriter writer, bool fullPacket) => writer.WriteValueSafe(Other);

            public void Deserialize(FastBufferReader reader, out bool fullPacket)
            {
                fullPacket = true;
                reader.ReadValueSafe(out Other);
            }
        }

        #endregion
    }
}
