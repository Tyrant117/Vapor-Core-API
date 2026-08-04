using System;
using System.Globalization;

namespace Vapor.Serialization
{
    public sealed class BooleanFormatter : VslFormatter<bool>
    {
        public static readonly BooleanFormatter Instance = new BooleanFormatter();
        public override bool IsScalar => true;
        public override void Write(ref VslWriter writer, in bool value, VslContext context) => writer.WriteBoolean(value);
        public override bool Read(ref VslReader reader, VslContext context) => reader.ReadBoolean();
    }

    public sealed class CharFormatter : VslFormatter<char>
    {
        public static readonly CharFormatter Instance = new CharFormatter();
        public override bool IsScalar => true;
        public override void Write(ref VslWriter writer, in char value, VslContext context) => writer.WriteString(value.ToString());
        public override char Read(ref VslReader reader, VslContext context) => reader.ReadChar();
    }

    public sealed class SByteFormatter : VslFormatter<sbyte>
    {
        public static readonly SByteFormatter Instance = new SByteFormatter();
        public override bool IsScalar => true;
        public override void Write(ref VslWriter writer, in sbyte value, VslContext context) => writer.WriteInt64(value);
        public override sbyte Read(ref VslReader reader, VslContext context) => reader.ReadSByte();
    }

    public sealed class ByteFormatter : VslFormatter<byte>
    {
        public static readonly ByteFormatter Instance = new ByteFormatter();
        public override bool IsScalar => true;
        public override void Write(ref VslWriter writer, in byte value, VslContext context) => writer.WriteUInt64(value);
        public override byte Read(ref VslReader reader, VslContext context) => reader.ReadByte();
    }

    public sealed class Int16Formatter : VslFormatter<short>
    {
        public static readonly Int16Formatter Instance = new Int16Formatter();
        public override bool IsScalar => true;
        public override void Write(ref VslWriter writer, in short value, VslContext context) => writer.WriteInt64(value);
        public override short Read(ref VslReader reader, VslContext context) => reader.ReadInt16();
    }

    public sealed class UInt16Formatter : VslFormatter<ushort>
    {
        public static readonly UInt16Formatter Instance = new UInt16Formatter();
        public override bool IsScalar => true;
        public override void Write(ref VslWriter writer, in ushort value, VslContext context) => writer.WriteUInt64(value);
        public override ushort Read(ref VslReader reader, VslContext context) => reader.ReadUInt16();
    }

    public sealed class Int32Formatter : VslFormatter<int>
    {
        public static readonly Int32Formatter Instance = new Int32Formatter();
        public override bool IsScalar => true;
        public override void Write(ref VslWriter writer, in int value, VslContext context) => writer.WriteInt64(value);
        public override int Read(ref VslReader reader, VslContext context) => reader.ReadInt32();
    }

    public sealed class UInt32Formatter : VslFormatter<uint>
    {
        public static readonly UInt32Formatter Instance = new UInt32Formatter();
        public override bool IsScalar => true;
        public override void Write(ref VslWriter writer, in uint value, VslContext context) => writer.WriteUInt64(value);
        public override uint Read(ref VslReader reader, VslContext context) => reader.ReadUInt32();
    }

    public sealed class Int64Formatter : VslFormatter<long>
    {
        public static readonly Int64Formatter Instance = new Int64Formatter();
        public override bool IsScalar => true;
        public override void Write(ref VslWriter writer, in long value, VslContext context) => writer.WriteInt64(value);
        public override long Read(ref VslReader reader, VslContext context) => reader.ReadInt64();
    }

    public sealed class UInt64Formatter : VslFormatter<ulong>
    {
        public static readonly UInt64Formatter Instance = new UInt64Formatter();
        public override bool IsScalar => true;
        public override void Write(ref VslWriter writer, in ulong value, VslContext context) => writer.WriteUInt64(value);
        public override ulong Read(ref VslReader reader, VslContext context) => reader.ReadUInt64();
    }

    public sealed class SingleFormatter : VslFormatter<float>
    {
        public static readonly SingleFormatter Instance = new SingleFormatter();
        public override bool IsScalar => true;
        public override void Write(ref VslWriter writer, in float value, VslContext context) => writer.WriteSingle(value);
        public override float Read(ref VslReader reader, VslContext context) => reader.ReadSingle();
    }

    public sealed class DoubleFormatter : VslFormatter<double>
    {
        public static readonly DoubleFormatter Instance = new DoubleFormatter();
        public override bool IsScalar => true;
        public override void Write(ref VslWriter writer, in double value, VslContext context) => writer.WriteDouble(value);
        public override double Read(ref VslReader reader, VslContext context) => reader.ReadDouble();
    }

    public sealed class DecimalFormatter : VslFormatter<decimal>
    {
        public static readonly DecimalFormatter Instance = new DecimalFormatter();
        public override bool IsScalar => true;
        public override void Write(ref VslWriter writer, in decimal value, VslContext context) => writer.WriteDecimal(value);
        public override decimal Read(ref VslReader reader, VslContext context) => reader.ReadDecimal();
    }

    public sealed class StringFormatter : VslFormatter<string>
    {
        public static readonly StringFormatter Instance = new StringFormatter();
        public override bool IsScalar => true;
        public override void Write(ref VslWriter writer, in string value, VslContext context) => writer.WriteString(value);
        public override string Read(ref VslReader reader, VslContext context) => reader.ReadString();
    }

    /// <summary>Round-trip ISO-8601, so the value survives a change of culture or time zone.</summary>
    public sealed class DateTimeFormatter : VslFormatter<DateTime>
    {
        public static readonly DateTimeFormatter Instance = new DateTimeFormatter();
        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in DateTime value, VslContext context) =>
            writer.WriteString(value.ToString("o", CultureInfo.InvariantCulture));

        public override DateTime Read(ref VslReader reader, VslContext context)
        {
            var text = reader.ReadString();
            return string.IsNullOrEmpty(text)
                ? default
                : DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }
    }

    public sealed class DateTimeOffsetFormatter : VslFormatter<DateTimeOffset>
    {
        public static readonly DateTimeOffsetFormatter Instance = new DateTimeOffsetFormatter();
        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in DateTimeOffset value, VslContext context) =>
            writer.WriteString(value.ToString("o", CultureInfo.InvariantCulture));

        public override DateTimeOffset Read(ref VslReader reader, VslContext context)
        {
            var text = reader.ReadString();
            return string.IsNullOrEmpty(text)
                ? default
                : DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }
    }

    public sealed class TimeSpanFormatter : VslFormatter<TimeSpan>
    {
        public static readonly TimeSpanFormatter Instance = new TimeSpanFormatter();
        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in TimeSpan value, VslContext context) =>
            writer.WriteString(value.ToString("c", CultureInfo.InvariantCulture));

        public override TimeSpan Read(ref VslReader reader, VslContext context)
        {
            var text = reader.ReadString();
            return string.IsNullOrEmpty(text)
                ? default
                : TimeSpan.Parse(text, CultureInfo.InvariantCulture);
        }
    }

    public sealed class GuidFormatter : VslFormatter<Guid>
    {
        public static readonly GuidFormatter Instance = new GuidFormatter();
        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in Guid value, VslContext context) =>
            writer.WriteString(value.ToString("D", CultureInfo.InvariantCulture));

        public override Guid Read(ref VslReader reader, VslContext context)
        {
            var text = reader.ReadString();
            return string.IsNullOrEmpty(text) ? default : Guid.Parse(text);
        }
    }

    public sealed class UriFormatter : VslFormatter<Uri>
    {
        public static readonly UriFormatter Instance = new UriFormatter();
        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in Uri value, VslContext context)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteString(value.OriginalString);
        }

        public override Uri Read(ref VslReader reader, VslContext context)
        {
            var text = reader.ReadString();
            return string.IsNullOrEmpty(text) ? null : new Uri(text, UriKind.RelativeOrAbsolute);
        }
    }

    public sealed class VersionFormatter : VslFormatter<Version>
    {
        public static readonly VersionFormatter Instance = new VersionFormatter();
        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in Version value, VslContext context)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteString(value.ToString());
        }

        public override Version Read(ref VslReader reader, VslContext context)
        {
            var text = reader.ReadString();
            return string.IsNullOrEmpty(text) ? null : Version.Parse(text);
        }
    }

    /// <summary>
    /// Wraps a value-type formatter so the member can also be null.
    /// </summary>
    public sealed class NullableFormatter<T> : VslFormatter<T?> where T : struct
    {
        private readonly IVslFormatter<T> _inner;

        public NullableFormatter(IVslFormatter<T> inner) => _inner = inner;

        public NullableFormatter() : this(VslFormatterRegistry.Get<T>())
        {
        }

        public override bool IsScalar => _inner.IsScalar;

        public override void Write(ref VslWriter writer, in T? value, VslContext context)
        {
            if (!value.HasValue)
            {
                writer.WriteNull();
                return;
            }

            _inner.Write(ref writer, value.Value, context);
        }

        public override T? Read(ref VslReader reader, VslContext context)
        {
            if (reader.TryReadNull())
            {
                return null;
            }

            return _inner.Read(ref reader, context);
        }
    }
}
