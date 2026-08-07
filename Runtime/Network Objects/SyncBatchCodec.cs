using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;

namespace Vapor.NetworkObjects
{
    /// <summary>
    /// The wire format for a batch of object state updates, and the decision about where to split one.
    /// </summary>
    /// <remarks>
    /// Separated from <see cref="NetworkMessages"/> because it is pure framing: a wrong chunk boundary
    /// or a mis-sized prefix silently drops state rather than failing, which is the hardest kind of bug
    /// to notice and the easiest to test directly.
    /// <para>
    /// Layout is <c>[count]</c> then, per entry, <c>[object id][length][payload]</c>. The per-entry
    /// length is what lets a receiver skip an object it does not have instead of abandoning the rest of
    /// the message — which happens legitimately whenever a despawn and an update cross on the wire.
    /// </para>
    /// </remarks>
    internal static class SyncBatchCodec
    {
        public static void WriteCount(FastBufferWriter writer, int entryCount) =>
            BytePacker.WriteValueBitPacked(writer, entryCount);

        public static void WriteEntry(FastBufferWriter writer, ulong networkObjectId, NativeArray<byte> payload)
        {
            BytePacker.WriteValueBitPacked(writer, networkObjectId);
            writer.WriteValueSafe(payload);
        }

        public static int ReadCount(FastBufferReader reader)
        {
            ByteUnpacker.ReadValueBitPacked(reader, out int entryCount);
            return entryCount;
        }

        /// <returns>The entry's object id. The payload is the caller's to dispose.</returns>
        public static ulong ReadEntry(FastBufferReader reader, out NativeArray<byte> payload, Allocator allocator)
        {
            ByteUnpacker.ReadValueBitPacked(reader, out ulong networkObjectId);
            reader.ReadValueSafe(out payload, allocator);
            return networkObjectId;
        }

        /// <summary>
        /// Splits a run of entries into messages that stay under <paramref name="budget"/>.
        /// </summary>
        /// <remarks>
        /// An entry that exceeds the budget on its own still goes out, alone — fragmentation covers it.
        /// Dropping it instead would lose that object's update permanently, since the dirty state is
        /// already consumed by the time this runs.
        /// </remarks>
        public static void Chunk(IReadOnlyList<int> payloadSizes, int budget, int entryOverhead, List<int> chunkCounts)
        {
            chunkCounts.Clear();

            int index = 0;
            while (index < payloadSizes.Count)
            {
                int start = index;
                int bytes = 0;

                while (index < payloadSizes.Count)
                {
                    int cost = payloadSizes[index] + entryOverhead;
                    if (index != start && bytes + cost > budget)
                    {
                        break;
                    }

                    bytes += cost;
                    index++;
                }

                chunkCounts.Add(index - start);
            }
        }
    }
}
