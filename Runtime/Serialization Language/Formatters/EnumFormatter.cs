using System;
using System.Collections.Generic;
using System.Globalization;

namespace Vapor.Serialization
{
    /// <summary>
    /// Writes enums as member names — <c>Additive</c>, or <c>A | B</c> for a <see cref="FlagsAttribute"/>
    /// enum — and reads names or raw numbers back.
    /// </summary>
    /// <remarks>
    /// Names rather than numbers is the point: a model writing <c>mode: Additive</c> needs no lookup
    /// table, and a human reading it needs no decoder. Undeclared values still round-trip as numbers.
    /// </remarks>
    public sealed class EnumFormatter<T> : VslFormatter<T> where T : struct, Enum
    {
        private static readonly string[] s_Names = Enum.GetNames(typeof(T));
        private static readonly T[] s_Values = (T[])Enum.GetValues(typeof(T));
        private static readonly bool s_IsFlags = typeof(T).IsDefined(typeof(FlagsAttribute), false);
        private static readonly bool s_Unsigned = IsUnsignedUnderlyingType();

        public static readonly EnumFormatter<T> Instance = new EnumFormatter<T>();

        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in T value, VslContext context)
        {
            // Exact match against a declared member, using the cached name so the common case
            // allocates nothing.
            var comparer = EqualityComparer<T>.Default;
            for (var i = 0; i < s_Values.Length; i++)
            {
                if (comparer.Equals(s_Values[i], value))
                {
                    writer.WriteIdentifier(s_Names[i]);
                    return;
                }
            }

            if (s_IsFlags)
            {
                // .NET already decomposes a flag combination as "A, B"; re-punctuate it as "A | B".
                var text = value.ToString();
                var start = 0;
                var first = true;
                while (start <= text.Length)
                {
                    var comma = text.IndexOf(',', start);
                    var end = comma < 0 ? text.Length : comma;
                    var part = text.Substring(start, end - start).Trim();

                    if (part.Length > 0)
                    {
                        if (!first)
                        {
                            writer.WriteFlagSeparator();
                        }

                        writer.WriteIdentifier(part);
                        first = false;
                    }

                    if (comma < 0)
                    {
                        break;
                    }

                    start = comma + 1;
                }

                return;
            }

            // A value with no declared member: keep the number so it still round-trips.
            writer.WriteIdentifier(value.ToString());
        }

        public override T Read(ref VslReader reader, VslContext context)
        {
            if (reader.PeekKind() == VslValueKind.Number)
            {
                return FromRaw(reader.ReadUInt64());
            }

            if (reader.TryReadNull())
            {
                return default;
            }

            var accumulated = ParseName(reader.ReadIdentifier(), context);
            if (!reader.TryReadPipe())
            {
                return accumulated;
            }

            var raw = ToRaw(accumulated);
            do
            {
                raw |= ToRaw(ParseName(reader.ReadIdentifier(), context));
            }
            while (reader.TryReadPipe());

            return FromRaw(raw);
        }

        private static T ParseName(ReadOnlySpan<char> name, VslContext context)
        {
            for (var i = 0; i < s_Names.Length; i++)
            {
                if (name.Equals(s_Names[i].AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    return s_Values[i];
                }
            }

            // A bare number can appear where a name was expected, for values with no member.
            if (ulong.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
            {
                return FromRaw(numeric);
            }

            if (context.Options.Strict)
            {
                throw new VslException($"'{name.ToString()}' is not a member of {typeof(T).Name}.");
            }

            return default;
        }

        private static ulong ToRaw(T value) =>
            s_Unsigned
                ? Convert.ToUInt64(value, CultureInfo.InvariantCulture)
                : unchecked((ulong)Convert.ToInt64(value, CultureInfo.InvariantCulture));

        private static T FromRaw(ulong raw) =>
            s_Unsigned
                ? (T)Enum.ToObject(typeof(T), raw)
                : (T)Enum.ToObject(typeof(T), unchecked((long)raw));

        private static bool IsUnsignedUnderlyingType()
        {
            switch (Type.GetTypeCode(Enum.GetUnderlyingType(typeof(T))))
            {
                case TypeCode.Byte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                    return true;
                default:
                    return false;
            }
        }
    }
}
