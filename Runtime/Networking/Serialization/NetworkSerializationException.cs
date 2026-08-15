using System;

namespace Vapor.Networking
{
    /// <summary>
    /// A packet could not be written or read. Thrown rather than returned so a malformed message
    /// unwinds to the one place that owns the connection, which decides whether to drop the message or
    /// the peer; every intermediate frame would otherwise have to check and propagate a flag.
    /// </summary>
    public class NetworkSerializationException : Exception
    {
        public NetworkSerializationException(string message) : base(message) { }
        public NetworkSerializationException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>A read ran past the end of the buffer — the message is shorter than its type claims.</summary>
    public sealed class EndOfBufferException : NetworkSerializationException
    {
        public EndOfBufferException(int requested, int remaining)
            : base($"Tried to read {requested} byte(s) with only {remaining} remaining.") { }
    }

    /// <summary>A write would grow the buffer past its configured ceiling.</summary>
    public sealed class BufferCapacityException : NetworkSerializationException
    {
        public BufferCapacityException(int requested, int maxCapacity)
            : base($"Writing {requested} byte(s) would exceed the buffer's maximum capacity of {maxCapacity}.") { }
    }

    /// <summary>No formatter is registered for a type that reached the wire.</summary>
    public sealed class NetworkFormatterMissingException : NetworkSerializationException
    {
        public Type ValueType { get; }

        public NetworkFormatterMissingException(Type valueType)
            : base($"No network formatter is registered for '{valueType}'. Mark the type [NetworkSerializable] so the generator emits one, register a formatter with NetworkFormatters.Register<T>(), or use a supported built-in type.")
        {
            ValueType = valueType;
        }
    }
}
