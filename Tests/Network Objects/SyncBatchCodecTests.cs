using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using Vapor.NetworkObjects;

namespace Vapor.Tests
{
    /// <summary>
    /// The batch wire format and the split decision.
    /// </summary>
    /// <remarks>
    /// Both fail quietly rather than loudly — a wrong chunk boundary drops an object's update, and a
    /// mis-sized prefix desynchronises everything after it in the message. Neither throws.
    /// </remarks>
    [TestFixture]
    public class SyncBatchCodecTests
    {
        private const int k_Overhead = 13;

        #region - Framing -

        [Test]
        public void EntriesRoundTrip()
        {
            var sent = new (ulong Id, byte[] Payload)[]
            {
                (1, new byte[] { 1, 2, 3 }),
                (4200000000, new byte[] { 9 }),
                (7, new byte[0]),
            };

            var writer = new FastBufferWriter(1024, Allocator.Temp, 64 * 1024);
            try
            {
                SyncBatchCodec.WriteCount(writer, sent.Length);
                foreach (var (id, bytes) in sent)
                {
                    using var payload = ToNative(bytes);
                    SyncBatchCodec.WriteEntry(writer, id, payload);
                }

                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    Assert.AreEqual(sent.Length, SyncBatchCodec.ReadCount(reader));

                    foreach (var (id, bytes) in sent)
                    {
                        var readId = SyncBatchCodec.ReadEntry(reader, out var payload, Allocator.Temp);
                        Assert.AreEqual(id, readId);
                        CollectionAssert.AreEqual(bytes, payload.ToArray());
                        payload.Dispose();
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

        /// <summary>
        /// The point of the per-entry length: an object the receiver does not have costs it that entry
        /// and nothing more.
        /// </summary>
        [Test]
        public void AnUnknownEntryCanBeSkippedWithoutLosingTheRest()
        {
            var writer = new FastBufferWriter(1024, Allocator.Temp, 64 * 1024);
            try
            {
                SyncBatchCodec.WriteCount(writer, 3);
                foreach (var (id, value) in new (ulong, byte)[] { (1, 10), (999, 20), (3, 30) })
                {
                    using var payload = ToNative(new byte[] { value, value, value });
                    SyncBatchCodec.WriteEntry(writer, id, payload);
                }

                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    var recovered = new List<ulong>();
                    int count = SyncBatchCodec.ReadCount(reader);
                    for (int i = 0; i < count; i++)
                    {
                        var id = SyncBatchCodec.ReadEntry(reader, out var payload, Allocator.Temp);
                        if (id != 999)
                        {
                            recovered.Add(id);
                            Assert.AreEqual(3, payload.Length);
                        }

                        payload.Dispose();
                    }

                    CollectionAssert.AreEqual(new ulong[] { 1, 3 }, recovered);
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

        #region - Chunking -

        [Test]
        public void NothingToSendProducesNoMessages()
        {
            Assert.AreEqual(new int[0], Chunk(new int[0], 100));
        }

        [Test]
        public void EverythingUnderBudgetGoesInOneMessage()
        {
            // 3 entries at 10+13 = 69 total, well under.
            Assert.AreEqual(new[] { 3 }, Chunk(new[] { 10, 10, 10 }, 100));
        }

        [Test]
        public void TheSplitHappensAtTheBudget()
        {
            // Each entry costs 23. Two fit in 50, the third starts a new message.
            Assert.AreEqual(new[] { 2, 2, 1 }, Chunk(new[] { 10, 10, 10, 10, 10 }, 50));
        }

        [Test]
        public void AnEntryLandingExactlyOnTheBudgetStillFits()
        {
            // Two entries at 23 each is exactly 46.
            Assert.AreEqual(new[] { 2 }, Chunk(new[] { 10, 10 }, 46));
        }

        /// <summary>
        /// Dropping it would lose that object's update for good — the dirty state is already spent by
        /// the time the split runs.
        /// </summary>
        [Test]
        public void AnOversizedEntryGoesOutAloneRatherThanBeingDropped()
        {
            var chunks = Chunk(new[] { 5, 10_000, 5 }, 100);

            Assert.AreEqual(new[] { 1, 1, 1 }, chunks);
            Assert.AreEqual(3, Total(chunks), "every entry must still be accounted for");
        }

        [Test]
        public void EveryEntryIsAccountedForAcrossChunks()
        {
            var sizes = new int[37];
            for (int i = 0; i < sizes.Length; i++)
            {
                sizes[i] = (i * 7) % 60;
            }

            Assert.AreEqual(sizes.Length, Total(Chunk(sizes, 120)));
        }

        #endregion

        #region - Harness -

        private static NativeArray<byte> ToNative(byte[] bytes)
        {
            var array = new NativeArray<byte>(bytes.Length, Allocator.Temp);
            for (int i = 0; i < bytes.Length; i++)
            {
                array[i] = bytes[i];
            }

            return array;
        }

        private static int[] Chunk(IReadOnlyList<int> sizes, int budget)
        {
            var chunkCounts = new List<int>();
            SyncBatchCodec.Chunk(sizes, budget, k_Overhead, chunkCounts);
            return chunkCounts.ToArray();
        }

        private static int Total(IEnumerable<int> chunks)
        {
            int total = 0;
            foreach (var chunk in chunks)
            {
                total += chunk;
            }

            return total;
        }

        #endregion
    }
}
