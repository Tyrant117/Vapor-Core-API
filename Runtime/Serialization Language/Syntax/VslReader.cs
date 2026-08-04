using System;
using System.Globalization;
using System.Text;

namespace Vapor.Serialization
{
    /// <summary>
    /// The shape of the next value in the document, without consuming it.
    /// </summary>
    public enum VslValueKind : byte
    {
        None = 0,

        /// <summary>End of the document, or the close of the current container.</summary>
        End,
        Null,
        Boolean,
        Number,
        String,

        /// <summary>A bare word: an enum member, a gameplay tag, or an unquoted string.</summary>
        Identifier,
        Object,
        Sequence,
        Tuple,
        Reference,

        /// <summary>A <c>!Name</c> tag introducing a concrete type.</summary>
        TypeTag,
    }

    /// <summary>
    /// Pull parser over a VSL document. This is the surface formatters read through.
    /// </summary>
    /// <remarks>
    /// A <c>ref struct</c> wrapping <see cref="VslLexer"/>: it cannot be captured or stored, so a
    /// formatter can only read forward. Container ends are detected by the <c>TryRead*Item</c>
    /// methods rather than tracked on a stack, which keeps the reader entirely stackless.
    /// </remarks>
    public ref struct VslReader
    {
        private VslLexer _lexer;
        private readonly VslContext _context;

        // True when the token just consumed was a container closer, so a following Read*End knows
        // there is nothing left to close. Without it, the natural fixed-arity pattern — try to read
        // each component, then close — runs off the end of the document as soon as the value
        // supplies fewer components than the target expects.
        //
        // Maintained in Advance() and nowhere else. Deriving it from the last token consumed is what
        // keeps it honest: setting it only where a closer is expected leaves it stale after a nested
        // container finishes, and the next Read*End then skips a closer it should have eaten.
        private bool _closed;

        public VslReader(ReadOnlySpan<char> source, VslContext context)
        {
            _lexer = new VslLexer(source);
            _context = context ?? VslContext.Default;
            _closed = false;
        }

        internal VslContext Context => _context;

        #region Document

        /// <summary>
        /// Consumes an optional <c>@vsl N</c> header and returns N, or 0 when the document has none.
        /// </summary>
        public int ReadHeader()
        {
            var peek = _lexer.Peek();
            if (peek.Kind != VslTokenKind.Directive)
            {
                return 0;
            }

            if (!_lexer.Text(peek).Equals("vsl".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                throw Error($"Unknown directive '@{_lexer.Text(peek).ToString()}'.", peek);
            }

            Advance();

            var version = _lexer.Peek();
            if (version.Kind != VslTokenKind.Number)
            {
                throw Error("Expected a version number after '@vsl'.", version);
            }

            Advance();
            return int.TryParse(_lexer.Text(version), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        #endregion

        #region Peeking

        /// <summary>Reports the shape of the next value without consuming it.</summary>
        public VslValueKind PeekKind()
        {
            var token = _lexer.Peek();
            switch (token.Kind)
            {
                case VslTokenKind.EndOfFile:
                case VslTokenKind.ObjectEnd:
                case VslTokenKind.SequenceEnd:
                case VslTokenKind.TupleEnd:
                    return VslValueKind.End;
                case VslTokenKind.ObjectStart: return VslValueKind.Object;
                case VslTokenKind.SequenceStart: return VslValueKind.Sequence;
                case VslTokenKind.TupleStart: return VslValueKind.Tuple;
                case VslTokenKind.TypeTag: return VslValueKind.TypeTag;
                case VslTokenKind.Reference:
                case VslTokenKind.ReferenceStart:
                case VslTokenKind.NullReference:
                    return VslValueKind.Reference;
                case VslTokenKind.String:
                case VslTokenKind.RawString:
                    return VslValueKind.String;
                case VslTokenKind.Number:
                case VslTokenKind.Hex:
                    return VslValueKind.Number;
                case VslTokenKind.Identifier:
                {
                    var text = _lexer.Text(token);
                    if (IsKeyword(text, "null")) return VslValueKind.Null;
                    if (IsKeyword(text, "true") || IsKeyword(text, "false")) return VslValueKind.Boolean;
                    return VslValueKind.Identifier;
                }
                default:
                    return VslValueKind.None;
            }
        }

        /// <summary>Consumes a <c>null</c> literal if that is what comes next.</summary>
        public bool TryReadNull()
        {
            var token = _lexer.Peek();
            if (token.Kind == VslTokenKind.Identifier && IsKeyword(_lexer.Text(token), "null"))
            {
                Advance();
                return true;
            }

            if (token.Kind == VslTokenKind.NullReference)
            {
                Advance();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Consumes a <c>!Name</c> type tag if one is present, yielding the name.
        /// </summary>
        public bool TryReadTypeTag(out ReadOnlySpan<char> tag)
        {
            var token = _lexer.Peek();
            if (token.Kind != VslTokenKind.TypeTag)
            {
                tag = default;
                return false;
            }

            Advance();
            tag = _lexer.Text(token);
            return true;
        }

        #endregion

        #region Containers

        public void ReadObjectStart()
        {
            Expect(VslTokenKind.ObjectStart, "'{'");
            _closed = false;
        }

        public void ReadSequenceStart()
        {
            Expect(VslTokenKind.SequenceStart, "'['");
            _closed = false;
        }

        public void ReadTupleStart()
        {
            Expect(VslTokenKind.TupleStart, "'('");
            _closed = false;
        }

        /// <summary>
        /// True when the next token closes the current container, or ends the document. Does not
        /// consume anything.
        /// </summary>
        public bool AtEnd()
        {
            var kind = _lexer.Peek().Kind;
            return kind is VslTokenKind.EndOfFile or VslTokenKind.ObjectEnd
                or VslTokenKind.SequenceEnd or VslTokenKind.TupleEnd;
        }

        /// <summary>
        /// Reads the next member name and its <c>:</c>, leaving the reader positioned on the value.
        /// Returns false at <c>}</c>, which it consumes.
        /// </summary>
        public bool TryReadMemberName(out ReadOnlySpan<char> name)
        {
            var token = Advance();
            if (token.Kind == VslTokenKind.ObjectEnd || token.Kind == VslTokenKind.EndOfFile)
            {
                name = default;
                _closed = true;
                return false;
            }

            _closed = false;

            if (token.Kind != VslTokenKind.Identifier && token.Kind != VslTokenKind.String)
            {
                throw Error($"Expected a member name but found {Describe(token)}.", token);
            }

            name = _lexer.Text(token);

            var colon = Advance();
            if (colon.Kind != VslTokenKind.Colon)
            {
                throw Error($"Expected ':' after member '{name.ToString()}' but found {Describe(colon)}.", colon);
            }

            return true;
        }

        /// <summary>
        /// Returns true when another element follows, false at <c>]</c>, which it consumes.
        /// </summary>
        public bool TryReadSequenceItem() => TryReadItem(VslTokenKind.SequenceEnd, "']'");

        /// <summary>
        /// Returns true when another component follows, false at <c>)</c>, which it consumes.
        /// </summary>
        public bool TryReadTupleItem() => TryReadItem(VslTokenKind.TupleEnd, "')'");

        /// <summary>
        /// Consumes the rest of the current tuple, including its <c>)</c>. Lets a formatter accept a
        /// tuple carrying more components than it needs.
        /// </summary>
        public void ReadTupleEnd()
        {
            if (ConsumeAlreadyClosed())
            {
                return;
            }

            while (TryReadTupleItem())
            {
                SkipValue();
            }
        }

        /// <summary>Consumes the rest of the current sequence, including its <c>]</c>.</summary>
        public void ReadSequenceEnd()
        {
            if (ConsumeAlreadyClosed())
            {
                return;
            }

            while (TryReadSequenceItem())
            {
                SkipValue();
            }
        }

        /// <summary>Consumes the rest of the current object, including its <c>}</c>.</summary>
        public void ReadObjectEnd()
        {
            if (ConsumeAlreadyClosed())
            {
                return;
            }

            while (TryReadMemberName(out _))
            {
                SkipValue();
            }
        }

        private bool ConsumeAlreadyClosed()
        {
            if (!_closed)
            {
                return false;
            }

            _closed = false;
            return true;
        }

        private bool TryReadItem(VslTokenKind closer, string closerText)
        {
            var token = _lexer.Peek();
            if (token.Kind == closer)
            {
                Advance();
                _closed = true;
                return false;
            }

            _closed = false;

            if (token.Kind == VslTokenKind.EndOfFile)
            {
                throw Error($"Expected {closerText} but reached the end of the document.", token);
            }

            // A mismatched closer means the brackets do not balance; say so where it happens rather
            // than letting the error surface somewhere unrelated.
            if (token.Kind is VslTokenKind.ObjectEnd or VslTokenKind.SequenceEnd or VslTokenKind.TupleEnd)
            {
                throw Error($"Expected {closerText} but found {Describe(token)}.", token);
            }

            return true;
        }

        #endregion

        #region Scalars

        public bool ReadBoolean()
        {
            var token = Advance();
            switch (token.Kind)
            {
                case VslTokenKind.Identifier:
                {
                    var text = _lexer.Text(token);
                    if (IsKeyword(text, "true")) return true;
                    if (IsKeyword(text, "false")) return false;
                    if (IsKeyword(text, "null")) return false;
                    break;
                }
                case VslTokenKind.Number:
                {
                    // 0/1 are accepted so numeric-flavoured input still binds to a bool.
                    if (long.TryParse(_lexer.Text(token), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                    {
                        return n != 0;
                    }

                    break;
                }
            }

            throw Error($"Expected a boolean but found {Describe(token)}.", token);
        }

        public long ReadInt64()
        {
            var token = Advance();
            switch (token.Kind)
            {
                case VslTokenKind.Number:
                {
                    var text = _lexer.Text(token);
                    if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                    {
                        return n;
                    }

                    // Tolerate a float literal in an integer slot: 3.0 -> 3.
                    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    {
                        return (long)d;
                    }

                    break;
                }
                case VslTokenKind.Hex:
                    return unchecked((long)ParseHex(token));
                case VslTokenKind.Identifier when IsKeyword(_lexer.Text(token), "null"):
                    return 0;
            }

            throw Error($"Expected an integer but found {Describe(token)}.", token);
        }

        public ulong ReadUInt64()
        {
            var token = Advance();
            switch (token.Kind)
            {
                case VslTokenKind.Number:
                {
                    var text = _lexer.Text(token);
                    if (ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                    {
                        return n;
                    }

                    if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
                    {
                        return unchecked((ulong)s);
                    }

                    break;
                }
                case VslTokenKind.Hex:
                    return ParseHex(token);
                case VslTokenKind.Identifier when IsKeyword(_lexer.Text(token), "null"):
                    return 0;
            }

            throw Error($"Expected an unsigned integer but found {Describe(token)}.", token);
        }

        public double ReadDouble()
        {
            var token = Advance();
            switch (token.Kind)
            {
                case VslTokenKind.Number:
                {
                    var text = _lexer.Text(token);
                    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    {
                        return d;
                    }

                    if (TryParseSpecialFloat(text, out d))
                    {
                        return d;
                    }

                    break;
                }
                case VslTokenKind.Hex:
                    return ParseHex(token);
                case VslTokenKind.Identifier:
                {
                    var text = _lexer.Text(token);
                    if (IsKeyword(text, "null")) return 0d;
                    if (TryParseSpecialFloat(text, out var d)) return d;
                    break;
                }
            }

            throw Error($"Expected a number but found {Describe(token)}.", token);
        }

        public float ReadSingle() => (float)ReadDouble();

        public int ReadInt32() => (int)ReadInt64();

        public short ReadInt16() => (short)ReadInt64();

        public sbyte ReadSByte() => (sbyte)ReadInt64();

        public uint ReadUInt32() => (uint)ReadUInt64();

        public ushort ReadUInt16() => (ushort)ReadUInt64();

        public byte ReadByte() => (byte)ReadUInt64();

        public char ReadChar()
        {
            var text = ReadString();
            return string.IsNullOrEmpty(text) ? '\0' : text[0];
        }

        /// <summary>
        /// Reads a component of a fixed-arity value, yielding <paramref name="fallback"/> when the
        /// value ran out of components early.
        /// </summary>
        /// <remarks>
        /// This is the pattern fixed-arity formatters should use — read each component with a
        /// <c>*Or</c> method, then call the matching <c>Read*End</c>. It accepts both a short value
        /// (<c>(1, 2)</c> into a <c>Vector3</c>) and a long one (<c>(1, 2, 3, 4)</c> into a
        /// <c>Vector3</c>) without either becoming an error.
        /// </remarks>
        public float ReadSingleOr(float fallback = 0f) => AtEnd() ? fallback : ReadSingle();

        /// <inheritdoc cref="ReadSingleOr"/>
        public double ReadDoubleOr(double fallback = 0d) => AtEnd() ? fallback : ReadDouble();

        /// <inheritdoc cref="ReadSingleOr"/>
        public long ReadInt64Or(long fallback = 0L) => AtEnd() ? fallback : ReadInt64();

        /// <inheritdoc cref="ReadSingleOr"/>
        public int ReadInt32Or(int fallback = 0) => AtEnd() ? fallback : ReadInt32();

        /// <inheritdoc cref="ReadSingleOr"/>
        public ulong ReadUInt64Or(ulong fallback = 0UL) => AtEnd() ? fallback : ReadUInt64();

        /// <inheritdoc cref="ReadSingleOr"/>
        public bool ReadBooleanOr(bool fallback = false) => AtEnd() ? fallback : ReadBoolean();

        /// <inheritdoc cref="ReadSingleOr"/>
        public string ReadStringOr(string fallback = null) => AtEnd() ? fallback : ReadString();

        public decimal ReadDecimal()
        {
            var token = Advance();
            if (token.Kind == VslTokenKind.Number &&
                decimal.TryParse(_lexer.Text(token), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                return d;
            }

            throw Error($"Expected a decimal but found {Describe(token)}.", token);
        }

        /// <summary>
        /// Reads a string. A bare identifier or a number is accepted and returned verbatim, so
        /// unquoted input still binds to a string member.
        /// </summary>
        public string ReadString()
        {
            var token = Advance();
            switch (token.Kind)
            {
                case VslTokenKind.String:
                    return token.HasEscapes
                        ? Unescape(_lexer.Text(token), token)
                        : _lexer.Text(token).ToString();
                case VslTokenKind.RawString:
                    return DeIndentRawString(_lexer.Text(token));
                case VslTokenKind.Identifier:
                {
                    var text = _lexer.Text(token);
                    return IsKeyword(text, "null") ? null : text.ToString();
                }
                case VslTokenKind.Number:
                    return _lexer.Text(token).ToString();
                case VslTokenKind.Hex:
                    return "0x" + _lexer.Text(token).ToString();
                case VslTokenKind.NullReference:
                    return null;
            }

            throw Error($"Expected a string but found {Describe(token)}.", token);
        }

        /// <summary>
        /// Reads a bare word — an enum member, a gameplay tag, or an unquoted string — as a span into
        /// the source.
        /// </summary>
        public ReadOnlySpan<char> ReadIdentifier()
        {
            var token = Advance();
            if (token.Kind is VslTokenKind.Identifier or VslTokenKind.String or VslTokenKind.Number)
            {
                return _lexer.Text(token);
            }

            throw Error($"Expected a name but found {Describe(token)}.", token);
        }

        /// <summary>Consumes a <c>|</c> if one is next, for reading flag enums.</summary>
        public bool TryReadPipe()
        {
            if (_lexer.Peek().Kind != VslTokenKind.Pipe)
            {
                return false;
            }

            Advance();
            return true;
        }

        /// <summary>
        /// Reads an object reference in any of its forms — <c>@null</c>, <c>@id</c>,
        /// <c>@(id, source, "key")</c>, <c>@(source, "key")</c>, or the unbracketed
        /// <c>@source "key"</c>. Returns false for a null reference.
        /// </summary>
        public bool TryReadReference(out VslObjectReference reference)
        {
            reference = VslObjectReference.Null;

            var token = Advance();
            switch (token.Kind)
            {
                case VslTokenKind.Reference:
                {
                    ulong.TryParse(_lexer.Text(token), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id);
                    reference = new VslObjectReference(id);
                    return !reference.IsNull;
                }

                case VslTokenKind.ReferenceStart:
                    reference = ReadReferenceTuple();
                    return !reference.IsNull;

                case VslTokenKind.Directive:
                {
                    // The unbracketed form: @resource "UI/Icons/Sword".
                    var source = ParseAssetSource(_lexer.Text(token));
                    if (source == VslAssetSource.None)
                    {
                        throw Error($"'@{_lexer.Text(token).ToString()}' is not a known asset source; expected 'resource' or 'addressable'.", token);
                    }

                    reference = new VslObjectReference(source, ReadString());
                    return !reference.IsNull;
                }

                case VslTokenKind.NullReference:
                    return false;

                case VslTokenKind.Identifier when IsKeyword(_lexer.Text(token), "null"):
                    return false;

                case VslTokenKind.Number:
                {
                    // A bare number in a reference slot is accepted rather than rejected.
                    ulong.TryParse(_lexer.Text(token), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id);
                    reference = new VslObjectReference(id);
                    return !reference.IsNull;
                }

                case VslTokenKind.String:
                case VslTokenKind.RawString:
                {
                    // A bare string is read as a Resources path, which is the most likely thing a
                    // hand-written document means by it.
                    var key = token.HasEscapes ? Unescape(_lexer.Text(token), token) : _lexer.Text(token).ToString();
                    reference = new VslObjectReference(VslAssetSource.Resource, key);
                    return !reference.IsNull;
                }
            }

            throw Error($"Expected an object reference but found {Describe(token)}.", token);
        }

        private VslObjectReference ReadReferenceTuple()
        {
            ReadTupleStart();

            var id = 0UL;
            if (PeekKind() == VslValueKind.Number)
            {
                id = ReadUInt64();
            }

            var source = VslAssetSource.None;
            string key = null;

            if (!AtEnd())
            {
                source = ParseAssetSource(ReadIdentifier());
                if (!AtEnd())
                {
                    key = ReadString();
                }
            }

            ReadTupleEnd();
            return new VslObjectReference(id, source, key);
        }

        private static VslAssetSource ParseAssetSource(ReadOnlySpan<char> text)
        {
            if (IsKeyword(text, "resource") || IsKeyword(text, "res") || IsKeyword(text, "resources"))
            {
                return VslAssetSource.Resource;
            }

            if (IsKeyword(text, "addressable") || IsKeyword(text, "addr") || IsKeyword(text, "addressables"))
            {
                return VslAssetSource.Addressable;
            }

            return VslAssetSource.None;
        }

        #endregion

        #region Skipping

        /// <summary>Consumes the next value entirely, whatever its shape.</summary>
        public void SkipValue()
        {
            var token = Advance();
            switch (token.Kind)
            {
                case VslTokenKind.ObjectStart:
                    while (TryReadMemberName(out _))
                    {
                        SkipValue();
                    }

                    return;
                case VslTokenKind.SequenceStart:
                    ReadSequenceEnd();
                    return;
                case VslTokenKind.TupleStart:
                    ReadTupleEnd();
                    return;
                case VslTokenKind.TypeTag:
                    SkipValue();
                    return;
                case VslTokenKind.ReferenceStart:
                    ReadTupleStart();
                    ReadTupleEnd();
                    return;
                case VslTokenKind.Directive:
                    // '@resource "key"' — the key belongs to the reference, not to the next member.
                    if (ParseAssetSource(_lexer.Text(token)) != VslAssetSource.None &&
                        PeekKind() == VslValueKind.String)
                    {
                        SkipValue();
                    }

                    return;
                case VslTokenKind.Identifier:
                    // Consume any trailing flag chain so 'A | B | C' skips as one value.
                    while (_lexer.Peek().Kind == VslTokenKind.Pipe)
                    {
                        Advance();
                        Advance();
                    }

                    return;
                case VslTokenKind.EndOfFile:
                    throw Error("Expected a value but reached the end of the document.", token);

                // A closer or a stray colon here means the value itself is missing, as in '{ a: }'.
                // Every typed Read* method already rejects this; skipping must too, or a malformed
                // member would slip through unnoticed.
                case VslTokenKind.ObjectEnd:
                case VslTokenKind.SequenceEnd:
                case VslTokenKind.TupleEnd:
                case VslTokenKind.Colon:
                    throw Error($"Expected a value but found {Describe(token)}.", token);
                default:
                    return;
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// The single point at which a token is consumed. Every read goes through here so
        /// <see cref="_closed"/> always reflects the token actually just taken.
        /// </summary>
        private VslToken Advance()
        {
            var token = _lexer.Next();
            _closed = token.Kind is VslTokenKind.ObjectEnd or VslTokenKind.SequenceEnd or VslTokenKind.TupleEnd;
            return token;
        }

        private void Expect(VslTokenKind kind, string what)
        {
            var token = Advance();
            if (token.Kind != kind)
            {
                throw Error($"Expected {what} but found {Describe(token)}.", token);
            }
        }

        private ulong ParseHex(VslToken token)
        {
            var text = _lexer.Text(token);
            if (ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            throw Error($"'0x{text.ToString()}' is not a valid hexadecimal number.", token);
        }

        private static bool TryParseSpecialFloat(ReadOnlySpan<char> text, out double value)
        {
            var negative = text.Length > 0 && text[0] == '-';
            var body = negative ? text.Slice(1) : text;

            if (body.Equals("inf".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                body.Equals("infinity".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                value = negative ? double.NegativeInfinity : double.PositiveInfinity;
                return true;
            }

            if (body.Equals("nan".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                value = double.NaN;
                return true;
            }

            value = 0d;
            return false;
        }

        private static bool IsKeyword(ReadOnlySpan<char> text, string keyword) =>
            text.Equals(keyword.AsSpan(), StringComparison.OrdinalIgnoreCase);

        private string Unescape(ReadOnlySpan<char> text, VslToken token)
        {
            // Escapes only ever shrink the text, so the source length is a safe upper bound.
            Span<char> buffer = text.Length <= 256 ? stackalloc char[text.Length] : new char[text.Length];
            var length = 0;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c != '\\')
                {
                    buffer[length++] = c;
                    continue;
                }

                i++;
                if (i >= text.Length)
                {
                    throw Error("Dangling '\\' at the end of a string.", token);
                }

                switch (text[i])
                {
                    case '"': buffer[length++] = '"'; break;
                    case '\\': buffer[length++] = '\\'; break;
                    case '/': buffer[length++] = '/'; break;
                    case 'n': buffer[length++] = '\n'; break;
                    case 'r': buffer[length++] = '\r'; break;
                    case 't': buffer[length++] = '\t'; break;
                    case 'b': buffer[length++] = '\b'; break;
                    case 'f': buffer[length++] = '\f'; break;
                    case '0': buffer[length++] = '\0'; break;
                    case 'u':
                    {
                        if (i + 4 >= text.Length)
                        {
                            throw Error("Incomplete '\\u' escape; four hex digits are required.", token);
                        }

                        var hex = text.Slice(i + 1, 4);
                        if (!ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                        {
                            throw Error($"'\\u{hex.ToString()}' is not a valid escape.", token);
                        }

                        buffer[length++] = (char)code;
                        i += 4;
                        break;
                    }
                    default:
                        throw Error($"Unknown escape '\\{text[i]}'.", token);
                }
            }

            return new string(buffer.Slice(0, length));
        }

        /// <summary>
        /// Applies the raw-string layout rules: drop the newline after the opening delimiter, and
        /// strip the closing delimiter's indentation from every line. Same shape as a C# raw string
        /// literal, so multi-line text sits at the document's indentation without carrying it.
        /// </summary>
        private static string DeIndentRawString(ReadOnlySpan<char> text)
        {
            if (text.Length == 0)
            {
                return string.Empty;
            }

            // Skip blank space then the first newline.
            var start = 0;
            while (start < text.Length && (text[start] == ' ' || text[start] == '\t'))
            {
                start++;
            }

            if (start < text.Length && text[start] == '\r')
            {
                start++;
            }

            if (start < text.Length && text[start] == '\n')
            {
                start++;
            }
            else
            {
                start = 0; // Single-line raw string: keep everything.
            }

            var body = text.Slice(start);

            // The whitespace on the closing delimiter's line sets the indentation to strip.
            var indent = 0;
            var end = body.Length;
            var lastNewline = body.LastIndexOf('\n');
            if (lastNewline >= 0)
            {
                var trailing = body.Slice(lastNewline + 1);
                var allWhitespace = true;
                foreach (var c in trailing)
                {
                    if (c != ' ' && c != '\t')
                    {
                        allWhitespace = false;
                        break;
                    }
                }

                if (allWhitespace)
                {
                    indent = trailing.Length;
                    end = lastNewline;
                    if (end > 0 && body[end - 1] == '\r')
                    {
                        end--;
                    }
                }
            }

            body = body.Slice(0, Math.Max(0, end));
            if (indent == 0)
            {
                return body.ToString();
            }

            var builder = new StringBuilder(body.Length);
            var lineStart = 0;
            while (lineStart <= body.Length)
            {
                var newline = body.Slice(lineStart).IndexOf('\n');
                var lineEnd = newline < 0 ? body.Length : lineStart + newline;
                var line = body.Slice(lineStart, lineEnd - lineStart);

                var strip = 0;
                while (strip < indent && strip < line.Length && (line[strip] == ' ' || line[strip] == '\t'))
                {
                    strip++;
                }

                var trimmed = line.Slice(strip);
                if (trimmed.Length > 0 && trimmed[trimmed.Length - 1] == '\r')
                {
                    trimmed = trimmed.Slice(0, trimmed.Length - 1);
                }

                builder.Append(trimmed.ToString());

                if (newline < 0)
                {
                    break;
                }

                builder.Append('\n');
                lineStart = lineEnd + 1;
            }

            return builder.ToString();
        }

        private string Describe(VslToken token)
        {
            switch (token.Kind)
            {
                case VslTokenKind.EndOfFile: return "the end of the document";
                case VslTokenKind.ObjectStart: return "'{'";
                case VslTokenKind.ObjectEnd: return "'}'";
                case VslTokenKind.SequenceStart: return "'['";
                case VslTokenKind.SequenceEnd: return "']'";
                case VslTokenKind.TupleStart: return "'('";
                case VslTokenKind.TupleEnd: return "')'";
                case VslTokenKind.Colon: return "':'";
                case VslTokenKind.Pipe: return "'|'";
                case VslTokenKind.TypeTag: return $"the type tag '!{_lexer.Text(token).ToString()}'";
                case VslTokenKind.Directive: return $"the directive '@{_lexer.Text(token).ToString()}'";
                case VslTokenKind.Reference: return $"the reference '@{_lexer.Text(token).ToString()}'";
                case VslTokenKind.ReferenceStart: return "'@('";
                case VslTokenKind.NullReference: return "'@null'";
                case VslTokenKind.String:
                case VslTokenKind.RawString:
                    return "a string";
                case VslTokenKind.Number: return $"the number '{_lexer.Text(token).ToString()}'";
                case VslTokenKind.Hex: return $"the number '0x{_lexer.Text(token).ToString()}'";
                case VslTokenKind.Identifier: return $"'{_lexer.Text(token).ToString()}'";
                default: return "an unexpected token";
            }
        }

        private static VslException Error(string message, VslToken token) =>
            new VslException(message, token.Line, token.Column);

        #endregion
    }
}
