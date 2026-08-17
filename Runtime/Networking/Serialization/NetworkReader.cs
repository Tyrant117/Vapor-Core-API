using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace Vapor.Networking
{
    /// <summary>
    /// The read half of <see cref="NetworkWriter"/>: little-endian primitives, variable-length integers,
    /// a bit-level side channel, and bounds checks that throw <see cref="EndOfBufferException"/> instead
    /// of returning garbage.
    /// </summary>
    /// <remarks>
    /// A reader is re-pointed at a new source with <see cref="SetSource(byte[], int, int)"/> rather than
    /// re-created, so the replicator can keep one per connection. It never copies: it reads in place
    /// from the array it was given, which must stay valid for the duration of the read.
    /// <para>
    /// The bit rule mirrors the writer's: any byte-level read first <see cref="AlignBits"/>, discarding
    /// whatever remained of a partially consumed byte. Because the writer pads pending bits to a whole
    /// byte before every byte-level write, the two always agree on where the next byte begins.
    /// </para>
    /// </remarks>
    [NoAutoStaticsCleanup]
    public sealed class NetworkReader
    {
        public const int DefaultMaxCollectionElements = 16 * 1024;

        private static readonly byte[] s_Empty = Array.Empty<byte>();

        private byte[] _buffer = s_Empty;
        private int _start;
        private int _end;
        private int _position;
        private ulong _bitBuffer;
        private int _bitCount;

        public NetworkReader() { }

        public NetworkReader(byte[] buffer) => SetSource(buffer, 0, buffer?.Length ?? 0);

        public NetworkReader(byte[] buffer, int offset, int length) => SetSource(buffer, offset, length);

        public NetworkReader(ArraySegment<byte> segment) => SetSource(segment.Array, segment.Offset, segment.Count);

        #region - Source and state -

        /// <summary>Points the reader at a new range. Position resets to the start of the range.</summary>
        public void SetSource(byte[] buffer, int offset, int length)
        {
            buffer ??= s_Empty;
            if (offset < 0 || length < 0 || offset > buffer.Length - length) throw new ArgumentOutOfRangeException(nameof(length));
            _buffer = buffer;
            _start = offset;
            _end = offset + length;
            _position = offset;
            _bitBuffer = 0;
            _bitCount = 0;
        }

        public void SetSource(ArraySegment<byte> segment) => SetSource(segment.Array, segment.Offset, segment.Count);

        /// <summary>Offset of the next byte-level read, relative to the start of the source range.</summary>
        public int Position => _position - _start;

        /// <summary>Total length of the source range.</summary>
        public int Length => _end - _start;

        /// <summary>Whole bytes not yet consumed. Bits buffered from a partially read byte are not counted.</summary>
        public int Remaining => _end - _position;

        public bool IsAtEnd => _position >= _end && _bitCount == 0;

        /// <summary>The backing array; the source range is <c>[Offset, Offset + Length)</c>.</summary>
        public byte[] Buffer => _buffer;

        public int Offset => _start;

        /// <summary>
        /// Maximum number of elements a built-in collection formatter may allocate from this reader.
        /// Increase this explicitly for protocols that intentionally carry larger collections.
        /// </summary>
        public int MaxCollectionElements { get; set; } = DefaultMaxCollectionElements;

        /// <summary>The unread remainder as a span. Aligns bits first.</summary>
        public ReadOnlySpan<byte> RemainingSpan
        {
            get
            {
                AlignBits();
                return new ReadOnlySpan<byte>(_buffer, _position, _end - _position);
            }
        }

        /// <summary>Moves the read cursor, relative to the start of the source range. Discards pending bits.</summary>
        public void Seek(int position)
        {
            AlignBits();
            if (position < 0 || position > Length) throw new ArgumentOutOfRangeException(nameof(position));
            _position = _start + position;
        }

        /// <summary>Advances past <paramref name="count"/> bytes without reading them.</summary>
        public void Skip(int count)
        {
            AlignBits();
            Require(count);
            _position += count;
        }

        #endregion

        #region - Bounds -

        private void Require(int bytes)
        {
            if (bytes < 0)
            {
                throw new NetworkSerializationException($"A negative byte count ({bytes}) was read from the wire.");
            }

            if (bytes > _end - _position)
            {
                throw new EndOfBufferException(bytes, _end - _position);
            }
        }

        internal int ValidateCollectionCount(uint encodedCount)
        {
            if (encodedCount > int.MaxValue)
            {
                throw new NetworkSerializationException($"Collection count {encodedCount} exceeds the supported range.");
            }

            int count = (int)encodedCount;
            if (count > MaxCollectionElements)
            {
                throw new NetworkSerializationException($"Collection count {count} exceeds the configured limit of {MaxCollectionElements}.");
            }

            return count;
        }

        #endregion

        #region - Bits -

        /// <summary>Reads <paramref name="bitCount"/> bits (1–32) written by <see cref="NetworkWriter.WriteBits(uint,int)"/>.</summary>
        public uint ReadBits(int bitCount)
        {
            if (bitCount < 1 || bitCount > 32) throw new ArgumentOutOfRangeException(nameof(bitCount));

            while (_bitCount < bitCount)
            {
                Require(1);
                _bitBuffer |= (ulong)_buffer[_position++] << _bitCount;
                _bitCount += 8;
            }

            uint value = bitCount == 32 ? (uint)_bitBuffer : (uint)(_bitBuffer & ((1ul << bitCount) - 1ul));
            _bitBuffer >>= bitCount;
            _bitCount -= bitCount;
            return value;
        }

        /// <summary>Reads <paramref name="bitCount"/> bits (1–64).</summary>
        public ulong ReadBits64(int bitCount)
        {
            if (bitCount < 1 || bitCount > 64) throw new ArgumentOutOfRangeException(nameof(bitCount));
            if (bitCount <= 32)
            {
                return ReadBits(bitCount);
            }

            ulong low = ReadBits(32);
            ulong high = ReadBits(bitCount - 32);
            return low | (high << 32);
        }

        public bool ReadBit() => ReadBits(1) != 0;

        /// <summary>Discards the rest of a partially consumed byte. Idempotent.</summary>
        public void AlignBits()
        {
            _bitBuffer = 0;
            _bitCount = 0;
        }

        #endregion

        #region - Primitives -

        public byte ReadByte()
        {
            AlignBits();
            Require(1);
            return _buffer[_position++];
        }

        public sbyte ReadSByte() => unchecked((sbyte)ReadByte());

        public bool ReadBool() => ReadByte() != 0;

        public short ReadInt16()
        {
            AlignBits();
            Require(2);
            short value = BinaryPrimitives.ReadInt16LittleEndian(new ReadOnlySpan<byte>(_buffer, _position, 2));
            _position += 2;
            return value;
        }

        public ushort ReadUInt16()
        {
            AlignBits();
            Require(2);
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(_buffer, _position, 2));
            _position += 2;
            return value;
        }

        public int ReadInt32()
        {
            AlignBits();
            Require(4);
            int value = BinaryPrimitives.ReadInt32LittleEndian(new ReadOnlySpan<byte>(_buffer, _position, 4));
            _position += 4;
            return value;
        }

        public uint ReadUInt32()
        {
            AlignBits();
            Require(4);
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(_buffer, _position, 4));
            _position += 4;
            return value;
        }

        public long ReadInt64()
        {
            AlignBits();
            Require(8);
            long value = BinaryPrimitives.ReadInt64LittleEndian(new ReadOnlySpan<byte>(_buffer, _position, 8));
            _position += 8;
            return value;
        }

        public ulong ReadUInt64()
        {
            AlignBits();
            Require(8);
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(new ReadOnlySpan<byte>(_buffer, _position, 8));
            _position += 8;
            return value;
        }

        public float ReadSingle() => BitConverter.Int32BitsToSingle(ReadInt32());

        public double ReadDouble() => BitConverter.Int64BitsToDouble(ReadInt64());

        public char ReadChar() => (char)ReadUInt16();

        public float ReadHalf() => Mathf.HalfToFloat(ReadUInt16());

        #endregion

        #region - Variable-length integers -

        public uint ReadVarUInt32()
        {
            AlignBits();
            uint result = 0;
            int shift = 0;
            while (true)
            {
                Require(1);
                byte b = _buffer[_position++];
                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    return result;
                }

                shift += 7;
                if (shift > 28)
                {
                    throw new NetworkSerializationException("Malformed variable-length integer: more than 5 bytes.");
                }
            }
        }

        public ushort ReadVarUInt16()
        {
            uint value = ReadVarUInt32();
            if (value > ushort.MaxValue)
            {
                throw new NetworkSerializationException($"Value {value} does not fit in a UInt16.");
            }

            return (ushort)value;
        }

        public ulong ReadVarUInt64()
        {
            AlignBits();
            ulong result = 0;
            int shift = 0;
            while (true)
            {
                Require(1);
                byte b = _buffer[_position++];
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    return result;
                }

                shift += 7;
                if (shift > 63)
                {
                    throw new NetworkSerializationException("Malformed variable-length integer: more than 10 bytes.");
                }
            }
        }

        public int ReadVarInt32()
        {
            uint zig = ReadVarUInt32();
            return (int)(zig >> 1) ^ -(int)(zig & 1);
        }

        public long ReadVarInt64()
        {
            ulong zig = ReadVarUInt64();
            return (long)(zig >> 1) ^ -(long)(zig & 1);
        }

        #endregion

        #region - Strings and bytes -

        public string ReadString()
        {
            uint prefix = ReadVarUInt32();
            if (prefix == 0)
            {
                return null;
            }

            uint encodedByteCount = prefix - 1;
            if (encodedByteCount > int.MaxValue)
            {
                throw new NetworkSerializationException($"String byte count {encodedByteCount} exceeds the supported range.");
            }

            int byteCount = (int)encodedByteCount;
            if (byteCount == 0)
            {
                return string.Empty;
            }

            Require(byteCount);
            string value = Encoding.UTF8.GetString(_buffer, _position, byteCount);
            _position += byteCount;
            return value;
        }

        /// <summary>Reads exactly <paramref name="count"/> raw bytes.</summary>
        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            AlignBits();
            Require(count);
            var span = new ReadOnlySpan<byte>(_buffer, _position, count);
            _position += count;
            return span;
        }

        public void ReadBytes(Span<byte> destination)
        {
            ReadBytes(destination.Length).CopyTo(destination);
        }

        /// <summary>Reads bytes written by <see cref="NetworkWriter.WriteBytesWithLength"/>.</summary>
        public ReadOnlySpan<byte> ReadBytesWithLength()
        {
            uint encodedCount = ReadVarUInt32();
            if (encodedCount > int.MaxValue)
            {
                throw new NetworkSerializationException($"Byte count {encodedCount} exceeds the supported range.");
            }

            int count = (int)encodedCount;
            return ReadBytes(count);
        }

        public byte[] ReadBytesWithLengthToArray() => ReadBytesWithLength().ToArray();

        public T ReadBlittable<T>() where T : unmanaged
        {
            AlignBits();
            int size = NetworkWriter.BlittableSize<T>.Value;
            Require(size);
            T value = MemoryMarshal.Read<T>(new ReadOnlySpan<byte>(_buffer, _position, size));
            _position += size;
            return value;
        }

        #endregion

        #region - Unity types -

        public Vector2 ReadVector2() => new(ReadSingle(), ReadSingle());

        public Vector3 ReadVector3() => new(ReadSingle(), ReadSingle(), ReadSingle());

        public Vector4 ReadVector4() => new(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());

        public Quaternion ReadQuaternion() => new(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());

        #endregion

        #region - Formatters -

        /// <summary>Reads through the registered formatter for <typeparamref name="T"/>.</summary>
        public T Read<T>() => NetworkFormatters.Get<T>().Read(this);

        #endregion
    }
}
