using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace Vapor.Networking
{
    /// <summary>
    /// A growable little-endian byte writer with variable-length integers, a bit-level side channel for
    /// quantized data, and length-prefix patching.
    /// </summary>
    /// <remarks>
    /// This is a class rather than a struct on purpose. A struct writer has to be passed by
    /// <c>ref</c> everywhere or it silently loses its position on copy, and a <c>ref struct</c> cannot
    /// be stored, captured, or used through generic delegates — all of which the replicator wants to do
    /// with its per-connection outbound buffers. One long-lived instance per connection and delivery
    /// class, plus a small pool for scratch use, makes the allocation cost irrelevant.
    /// <para>
    /// Byte-level writes and bit-level writes may be freely mixed: any byte-level write first
    /// <see cref="FlushBits"/>es, padding the pending bits to a whole byte. <see cref="NetworkReader"/>
    /// mirrors that rule, so a reader that reads the same sequence of bits and bytes always lands on
    /// the same offsets.
    /// </para>
    /// <para>
    /// All Unity platforms are little-endian; multi-byte primitives are written little-endian
    /// explicitly, so a buffer written on one platform reads on any other.
    /// </para>
    /// </remarks>
    public sealed class NetworkWriter
    {
        public const int DefaultInitialCapacity = 256;
        public const int DefaultMaxCapacity = 1 << 20;

        private byte[] _buffer;
        private int _position;
        private int _length;
        private ulong _bitBuffer;
        private int _bitCount;

        public NetworkWriter(int initialCapacity = DefaultInitialCapacity, int maxCapacity = DefaultMaxCapacity)
        {
            if (initialCapacity < 0) throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            if (maxCapacity < initialCapacity) throw new ArgumentOutOfRangeException(nameof(maxCapacity));
            _buffer = new byte[Math.Max(initialCapacity, 16)];
            MaxCapacity = maxCapacity;
        }

        #region - State -

        /// <summary>Offset of the next byte-level write. Pending bits are not counted until flushed.</summary>
        public int Position => _position;

        /// <summary>High-water mark: the number of meaningful bytes in <see cref="Buffer"/>.</summary>
        public int Length => _length;

        public int Capacity => _buffer.Length;

        public int MaxCapacity { get; }

        /// <summary>True when bit-level writes are pending and have not been padded to a byte yet.</summary>
        public bool HasPendingBits => _bitCount > 0;

        /// <summary>The backing array. Only the first <see cref="Length"/> bytes are meaningful.</summary>
        public byte[] Buffer => _buffer;

        /// <summary>The written bytes. Flushes pending bits first, so the view is complete.</summary>
        public ReadOnlySpan<byte> WrittenSpan
        {
            get
            {
                FlushBits();
                return new ReadOnlySpan<byte>(_buffer, 0, _length);
            }
        }

        /// <summary>The written bytes as a segment. Flushes pending bits first.</summary>
        public ArraySegment<byte> WrittenSegment
        {
            get
            {
                FlushBits();
                return new ArraySegment<byte>(_buffer, 0, _length);
            }
        }

        public byte[] ToArray()
        {
            FlushBits();
            var copy = new byte[_length];
            System.Buffer.BlockCopy(_buffer, 0, copy, 0, _length);
            return copy;
        }

        /// <summary>Forgets everything written. The backing array is kept.</summary>
        public void Reset()
        {
            _position = 0;
            _length = 0;
            _bitBuffer = 0;
            _bitCount = 0;
        }

        /// <summary>
        /// Moves the write cursor. Used to patch a value reserved earlier; the high-water mark is kept so
        /// seeking backwards never loses data.
        /// </summary>
        public void Seek(int position)
        {
            FlushBits();
            if (position < 0 || position > _length) throw new ArgumentOutOfRangeException(nameof(position));
            _position = position;
        }

        /// <summary>Drops everything after <paramref name="length"/>.</summary>
        public void Truncate(int length)
        {
            FlushBits();
            if (length < 0 || length > _length) throw new ArgumentOutOfRangeException(nameof(length));
            _length = length;
            if (_position > length) _position = length;
        }

        /// <summary>
        /// Drops the first <paramref name="count"/> bytes and shifts the rest to the front — how an
        /// outbound batch forgets what it has already sent.
        /// </summary>
        public void DiscardPrefix(int count)
        {
            FlushBits();
            if (count < 0 || count > _length) throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0) return;
            int remaining = _length - count;
            if (remaining > 0)
            {
                System.Buffer.BlockCopy(_buffer, count, _buffer, 0, remaining);
            }

            _length = remaining;
            _position = Math.Max(0, _position - count);
        }

        #endregion

        #region - Capacity -

        private void Ensure(int bytes)
        {
            int required = _position + bytes;
            if (required <= _buffer.Length)
            {
                return;
            }

            if (required > MaxCapacity)
            {
                throw new BufferCapacityException(bytes, MaxCapacity);
            }

            int newCapacity = _buffer.Length * 2;
            if (newCapacity < required) newCapacity = required;
            if (newCapacity > MaxCapacity) newCapacity = MaxCapacity;
            Array.Resize(ref _buffer, newCapacity);
        }

        private void Advance(int bytes)
        {
            _position += bytes;
            if (_position > _length) _length = _position;
        }

        #endregion

        #region - Bits -

        /// <summary>Writes the low <paramref name="bitCount"/> bits of <paramref name="value"/> (1–32).</summary>
        public void WriteBits(uint value, int bitCount)
        {
            if (bitCount < 1 || bitCount > 32) throw new ArgumentOutOfRangeException(nameof(bitCount));
            if (bitCount < 32) value &= (1u << bitCount) - 1u;

            _bitBuffer |= (ulong)value << _bitCount;
            _bitCount += bitCount;
            while (_bitCount >= 8)
            {
                Ensure(1);
                _buffer[_position] = (byte)_bitBuffer;
                Advance(1);
                _bitBuffer >>= 8;
                _bitCount -= 8;
            }
        }

        /// <summary>Writes the low <paramref name="bitCount"/> bits of <paramref name="value"/> (1–64).</summary>
        public void WriteBits(ulong value, int bitCount)
        {
            if (bitCount < 1 || bitCount > 64) throw new ArgumentOutOfRangeException(nameof(bitCount));
            if (bitCount <= 32)
            {
                WriteBits((uint)value, bitCount);
                return;
            }

            WriteBits((uint)value, 32);
            WriteBits((uint)(value >> 32), bitCount - 32);
        }

        public void WriteBit(bool value) => WriteBits(value ? 1u : 0u, 1);

        /// <summary>Pads pending bits with zeros to a whole byte and commits them. Idempotent.</summary>
        public void FlushBits()
        {
            if (_bitCount == 0)
            {
                return;
            }

            Ensure(1);
            _buffer[_position] = (byte)_bitBuffer;
            Advance(1);
            _bitBuffer = 0;
            _bitCount = 0;
        }

        #endregion

        #region - Primitives -

        public void WriteByte(byte value)
        {
            FlushBits();
            Ensure(1);
            _buffer[_position] = value;
            Advance(1);
        }

        public void WriteSByte(sbyte value) => WriteByte(unchecked((byte)value));

        public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

        public void WriteInt16(short value)
        {
            FlushBits();
            Ensure(2);
            BinaryPrimitives.WriteInt16LittleEndian(new Span<byte>(_buffer, _position, 2), value);
            Advance(2);
        }

        public void WriteUInt16(ushort value)
        {
            FlushBits();
            Ensure(2);
            BinaryPrimitives.WriteUInt16LittleEndian(new Span<byte>(_buffer, _position, 2), value);
            Advance(2);
        }

        public void WriteInt32(int value)
        {
            FlushBits();
            Ensure(4);
            BinaryPrimitives.WriteInt32LittleEndian(new Span<byte>(_buffer, _position, 4), value);
            Advance(4);
        }

        public void WriteUInt32(uint value)
        {
            FlushBits();
            Ensure(4);
            BinaryPrimitives.WriteUInt32LittleEndian(new Span<byte>(_buffer, _position, 4), value);
            Advance(4);
        }

        public void WriteInt64(long value)
        {
            FlushBits();
            Ensure(8);
            BinaryPrimitives.WriteInt64LittleEndian(new Span<byte>(_buffer, _position, 8), value);
            Advance(8);
        }

        public void WriteUInt64(ulong value)
        {
            FlushBits();
            Ensure(8);
            BinaryPrimitives.WriteUInt64LittleEndian(new Span<byte>(_buffer, _position, 8), value);
            Advance(8);
        }

        public void WriteSingle(float value) => WriteInt32(BitConverter.SingleToInt32Bits(value));

        public void WriteDouble(double value) => WriteInt64(BitConverter.DoubleToInt64Bits(value));

        public void WriteChar(char value) => WriteUInt16(value);

        /// <summary>Half-precision float, 2 bytes. Fine for velocities and normals, not for positions.</summary>
        public void WriteHalf(float value) => WriteUInt16(Mathf.FloatToHalf(value));

        #endregion

        #region - Variable-length integers -

        /// <summary>LEB128: 1 byte below 128, 2 below 16384, and so on. Ids and counts are almost always small.</summary>
        public void WriteVarUInt32(uint value)
        {
            FlushBits();
            Ensure(5);
            while (value >= 0x80)
            {
                _buffer[_position] = (byte)(value | 0x80);
                Advance(1);
                value >>= 7;
            }

            _buffer[_position] = (byte)value;
            Advance(1);
        }

        public void WriteVarUInt64(ulong value)
        {
            FlushBits();
            Ensure(10);
            while (value >= 0x80)
            {
                _buffer[_position] = (byte)(value | 0x80);
                Advance(1);
                value >>= 7;
            }

            _buffer[_position] = (byte)value;
            Advance(1);
        }

        /// <summary>ZigZag-encoded so small negative numbers stay small on the wire.</summary>
        public void WriteVarInt32(int value) => WriteVarUInt32((uint)((value << 1) ^ (value >> 31)));

        public void WriteVarInt64(long value) => WriteVarUInt64((ulong)((value << 1) ^ (value >> 63)));

        #endregion

        #region - Strings and bytes -

        /// <summary>
        /// UTF-8 with a variable-length prefix of <c>byteCount + 1</c>; a prefix of 0 is a null string,
        /// so null and empty round-trip as themselves.
        /// </summary>
        public void WriteString(string value)
        {
            if (value == null)
            {
                WriteVarUInt32(0);
                return;
            }

            int byteCount = Encoding.UTF8.GetByteCount(value);
            WriteVarUInt32((uint)byteCount + 1u);
            FlushBits();
            Ensure(byteCount);
            Encoding.UTF8.GetBytes(value, 0, value.Length, _buffer, _position);
            Advance(byteCount);
        }

        /// <summary>Raw bytes with no prefix. The reader must know the length.</summary>
        public void WriteBytes(ReadOnlySpan<byte> bytes)
        {
            FlushBits();
            Ensure(bytes.Length);
            bytes.CopyTo(new Span<byte>(_buffer, _position, bytes.Length));
            Advance(bytes.Length);
        }

        /// <summary>Bytes with a variable-length count prefix. Null is written as a count of 0.</summary>
        public void WriteBytesWithLength(ReadOnlySpan<byte> bytes)
        {
            WriteVarUInt32((uint)bytes.Length);
            WriteBytes(bytes);
        }

        /// <summary>
        /// Copies a value's in-memory representation. Host byte order and layout — fine for structs of
        /// primitives on Unity's platforms, and used by the fast paths for hot packet types.
        /// </summary>
        public void WriteBlittable<T>(in T value) where T : unmanaged
        {
            FlushBits();
            int size = BlittableSize<T>.Value;
            Ensure(size);
            T copy = value;
            MemoryMarshal.Write(new Span<byte>(_buffer, _position, size), ref copy);
            Advance(size);
        }

        #endregion

        #region - Unity types -

        public void WriteVector2(Vector2 value)
        {
            WriteSingle(value.x);
            WriteSingle(value.y);
        }

        public void WriteVector3(Vector3 value)
        {
            WriteSingle(value.x);
            WriteSingle(value.y);
            WriteSingle(value.z);
        }

        public void WriteVector4(Vector4 value)
        {
            WriteSingle(value.x);
            WriteSingle(value.y);
            WriteSingle(value.z);
            WriteSingle(value.w);
        }

        public void WriteQuaternion(Quaternion value)
        {
            WriteSingle(value.x);
            WriteSingle(value.y);
            WriteSingle(value.z);
            WriteSingle(value.w);
        }

        #endregion

        #region - Reserve and patch -

        /// <summary>Writes a placeholder and returns its offset for <see cref="PatchByte"/>.</summary>
        public int ReserveByte()
        {
            int at = _position;
            WriteByte(0);
            return at;
        }

        public void PatchByte(int at, byte value)
        {
            CheckPatch(at, 1);
            _buffer[at] = value;
        }

        public int ReserveUInt16()
        {
            int at = _position;
            WriteUInt16(0);
            return at;
        }

        public void PatchUInt16(int at, ushort value)
        {
            CheckPatch(at, 2);
            BinaryPrimitives.WriteUInt16LittleEndian(new Span<byte>(_buffer, at, 2), value);
        }

        public int ReserveUInt32()
        {
            int at = _position;
            WriteUInt32(0);
            return at;
        }

        public void PatchUInt32(int at, uint value)
        {
            CheckPatch(at, 4);
            BinaryPrimitives.WriteUInt32LittleEndian(new Span<byte>(_buffer, at, 4), value);
        }

        private void CheckPatch(int at, int size)
        {
            FlushBits();
            if (at < 0 || at + size > _length) throw new ArgumentOutOfRangeException(nameof(at));
        }

        #endregion

        #region - Formatters -

        /// <summary>Writes through the registered formatter for <typeparamref name="T"/>.</summary>
        public void Write<T>(in T value) => NetworkFormatters.Get<T>().Write(this, value);

        #endregion

        /// <summary>
        /// The size of an unmanaged struct, learned once per type without unsafe code: a one-element
        /// span reinterpreted as bytes has exactly <c>sizeof(T)</c> of them.
        /// </summary>
        [NoAutoStaticsCleanup]
        internal static class BlittableSize<T> where T : unmanaged
        {
            public static readonly int Value = MemoryMarshal.AsBytes(new ReadOnlySpan<T>(new T[1])).Length;
        }
    }
}
