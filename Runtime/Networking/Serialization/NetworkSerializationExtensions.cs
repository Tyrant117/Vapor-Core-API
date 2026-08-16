using System;

namespace Vapor.Networking
{
    /// <summary>
    /// The short forms hand-written payloads reach for: enums, anything with a formatter, object
    /// references. Everything here goes through <see cref="NetworkFormatters"/>, so a value written
    /// with <see cref="WriteValue{T}"/> reads back with <see cref="ReadValue{T}"/> whatever it is.
    /// </summary>
    public static class NetworkSerializationExtensions
    {
        /// <summary>Writes an enum through its formatter (its underlying integer, exact width).</summary>
        public static void WriteEnum<TEnum>(this NetworkWriter writer, TEnum value) where TEnum : struct, Enum =>
            NetworkFormatters.Write(writer, value);

        public static TEnum ReadEnum<TEnum>(this NetworkReader reader) where TEnum : struct, Enum =>
            NetworkFormatters.Read<TEnum>(reader);

        /// <summary>Writes any value that has (or can resolve) a formatter: primitives, payloads, collections, polymorphic bases.</summary>
        public static void WriteValue<T>(this NetworkWriter writer, in T value) => NetworkFormatters.Write(writer, value);

        public static T ReadValue<T>(this NetworkReader reader) => NetworkFormatters.Read<T>(reader);

        /// <summary>Writes a reference to a spawned object (id 0 for null or unspawned).</summary>
        public static void WriteReference(this NetworkWriter writer, VaporNetworkObject value) =>
            writer.WriteVarUInt64(value != null && value.IsSpawned ? value.NetworkObjectId : 0);

        public static void WriteReference(this NetworkWriter writer, VaporNetworkObjectReference value) =>
            writer.WriteVarUInt64(value.NetworkObjectId);

        public static VaporNetworkObjectReference ReadReference(this NetworkReader reader) =>
            new(reader.ReadVarUInt64());
    }
}
