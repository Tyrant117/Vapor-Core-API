using System;
using System.Buffers;
using System.Globalization;

namespace Vapor.Serialization
{
    /// <summary>
    /// Emits a VSL document into a pooled character buffer.
    /// </summary>
    /// <remarks>
    /// A <c>ref struct</c> so a formatter cannot stash it. Layout state for up to 64 nesting levels
    /// is packed into three bitfields rather than a stack object, which keeps writing free of
    /// per-level allocation. Callers must <see cref="Dispose"/> to return the rented buffer;
    /// <see cref="Vsl"/> does this in a finally block.
    /// </remarks>
    public ref struct VslWriter
    {
        private const int MaxTrackedDepth = 64;

        private char[] _buffer;
        private int _length;

        private readonly VslContext _context;
        private readonly int _indentWidth;
        private readonly string _newLine;
        private readonly int _maxDepth;

        // One bit per nesting level.
        private ulong _inlineFlags;
        private ulong _tupleFlags;
        private ulong _firstFlags;
        private int _depth;

        // Set after a member name or a type tag, meaning the next value belongs right here and must
        // not emit a separator of its own.
        private bool _pendingValue;

        public VslWriter(VslContext context)
        {
            _context = context ?? VslContext.Default;
            _indentWidth = _context.Options.IndentWidth;
            _newLine = _context.Options.NewLine ?? "\n";
            _maxDepth = Math.Min(_context.MaxDepth, MaxTrackedDepth);
            _buffer = ArrayPool<char>.Shared.Rent(1024);
            _length = 0;
            _inlineFlags = 0;
            _tupleFlags = 0;
            _firstFlags = 0;
            _depth = 0;
            _pendingValue = false;
        }

        internal VslContext Context => _context;

        public int Length => _length;

        public override string ToString() => new string(_buffer, 0, _length);

        public void Dispose()
        {
            if (_buffer != null)
            {
                ArrayPool<char>.Shared.Return(_buffer);
                _buffer = null;
            }
        }

        #region Document

        /// <summary>Writes the <c>@vsl 1</c> header and the blank line after it.</summary>
        public void WriteHeader()
        {
            if (!_context.Options.EmitHeader)
            {
                return;
            }

            Append("@vsl 1");
            Append(_newLine);
            Append(_newLine);
        }

        #endregion

        #region Containers

        public void BeginObject(bool inline = false)
        {
            BeforeValue();
            Append('{');
            Push(inline, tuple: false);
        }

        public void EndObject() => End('}');

        public void BeginSequence(bool inline = false)
        {
            BeforeValue();
            Append('[');
            Push(inline, tuple: false);
        }

        public void EndSequence() => End(']');

        /// <summary>Begins a fixed-arity value. Tuples are always written on one line.</summary>
        public void BeginTuple()
        {
            BeforeValue();
            Append('(');
            Push(inline: true, tuple: true);
        }

        public void EndTuple() => End(')');

        /// <summary>
        /// Writes a member name and its colon. The next value written becomes its value.
        /// </summary>
        public void WriteMember(string name)
        {
            BeforeEntry();

            if (IsBareIdentifier(name))
            {
                Append(name);
            }
            else
            {
                // Dictionary keys are member names too, and they are not required to look like C#
                // identifiers. Quoting keeps them readable back.
                _pendingValue = true;
                WriteString(name);
            }

            Append(':');
            Append(' ');
            _pendingValue = true;
        }

        private static bool IsBareIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var first = name[0];
            if (!((first >= 'a' && first <= 'z') || (first >= 'A' && first <= 'Z') || first == '_'))
            {
                return false;
            }

            for (var i = 1; i < name.Length; i++)
            {
                var c = name[i];
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '.'))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Writes a <c>#</c> comment on its own line. Ignored inside an inline container, and when
        /// <see cref="VslOptions.EmitComments"/> is off.
        /// </summary>
        public void WriteComment(string text)
        {
            if (!_context.Options.EmitComments || string.IsNullOrEmpty(text) || IsInline)
            {
                return;
            }

            var start = 0;
            while (start <= text.Length)
            {
                var newline = text.IndexOf('\n', start);
                var end = newline < 0 ? text.Length : newline;

                BeforeEntry();
                Append('#');
                Append(' ');
                var line = text.AsSpan(start, end - start);
                if (line.Length > 0 && line[line.Length - 1] == '\r')
                {
                    line = line.Slice(0, line.Length - 1);
                }

                Append(line);

                if (newline < 0)
                {
                    break;
                }

                start = newline + 1;
            }
        }

        /// <summary>
        /// Writes a <c>!Name</c> tag. The next value written is the tagged value.
        /// </summary>
        public void WriteTypeTag(string tag)
        {
            BeforeValue();
            Append('!');
            Append(tag);
            Append(' ');
            _pendingValue = true;
        }

        #endregion

        #region Scalars

        public void WriteNull()
        {
            BeforeValue();
            Append("null");
        }

        public void WriteBoolean(bool value)
        {
            BeforeValue();
            Append(value ? "true" : "false");
        }

        public void WriteInt64(long value)
        {
            BeforeValue();
            EnsureCapacity(24);
            if (value.TryFormat(_buffer.AsSpan(_length), out var written, default, CultureInfo.InvariantCulture))
            {
                _length += written;
                return;
            }

            Append(value.ToString(CultureInfo.InvariantCulture));
        }

        public void WriteUInt64(ulong value)
        {
            BeforeValue();
            EnsureCapacity(24);
            if (value.TryFormat(_buffer.AsSpan(_length), out var written, default, CultureInfo.InvariantCulture))
            {
                _length += written;
                return;
            }

            Append(value.ToString(CultureInfo.InvariantCulture));
        }

        public void WriteDouble(double value)
        {
            BeforeValue();
            if (WriteSpecialFloat(value))
            {
                return;
            }

            EnsureCapacity(40);
            if (value.TryFormat(_buffer.AsSpan(_length), out var written, "R".AsSpan(), CultureInfo.InvariantCulture))
            {
                _length += written;
                return;
            }

            Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        public void WriteSingle(float value)
        {
            BeforeValue();
            if (WriteSpecialFloat(value))
            {
                return;
            }

            EnsureCapacity(24);
            if (value.TryFormat(_buffer.AsSpan(_length), out var written, "R".AsSpan(), CultureInfo.InvariantCulture))
            {
                _length += written;
                return;
            }

            Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        public void WriteDecimal(decimal value)
        {
            BeforeValue();
            Append(value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Writes <c>0x</c> followed by <paramref name="digits"/> uppercase hex digits.</summary>
        public void WriteHex(ulong value, int digits)
        {
            BeforeValue();
            Append('0');
            Append('x');
            EnsureCapacity(digits);
            for (var shift = (digits - 1) * 4; shift >= 0; shift -= 4)
            {
                var nibble = (int)((value >> shift) & 0xF);
                _buffer[_length++] = (char)(nibble < 10 ? '0' + nibble : 'A' + (nibble - 10));
            }
        }

        /// <summary>Writes a bare word — an enum member, a gameplay tag, a type name.</summary>
        public void WriteIdentifier(string value)
        {
            BeforeValue();
            Append(value);
        }

        /// <summary>Writes the <c>|</c> that separates flag enum members.</summary>
        /// <remarks>
        /// The members on either side are a single value, so the separator marks the next one as
        /// pending — otherwise the writer would treat it as a new entry and insert a separator of
        /// its own.
        /// </remarks>
        public void WriteFlagSeparator()
        {
            Append(" | ");
            _pendingValue = true;
        }

        public void WriteString(string value)
        {
            if (value == null)
            {
                WriteNull();
                return;
            }

            BeforeValue();

            if (ShouldWriteRawBlock(value))
            {
                WriteRawBlock(value);
                return;
            }

            Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': Append("\\\""); break;
                    case '\\': Append("\\\\"); break;
                    case '\n': Append("\\n"); break;
                    case '\r': Append("\\r"); break;
                    case '\t': Append("\\t"); break;
                    case '\0': Append("\\0"); break;
                    case '\b': Append("\\b"); break;
                    case '\f': Append("\\f"); break;
                    default:
                        if (c < ' ')
                        {
                            Append("\\u");
                            EnsureCapacity(4);
                            for (var shift = 12; shift >= 0; shift -= 4)
                            {
                                var nibble = (c >> shift) & 0xF;
                                _buffer[_length++] = (char)(nibble < 10 ? '0' + nibble : 'A' + (nibble - 10));
                            }
                        }
                        else
                        {
                            Append(c);
                        }

                        break;
                }
            }

            Append('"');
        }

        /// <summary>
        /// Writes an object reference: <c>@null</c>, <c>@id</c>, or <c>@(id, source, "key")</c> when
        /// the object also has a durable asset locator.
        /// </summary>
        public void WriteReference(in VslObjectReference reference)
        {
            if (reference.IsNull)
            {
                WriteNullReference();
                return;
            }

            if (!reference.HasAssetKey)
            {
                WriteReference(reference.EntityId);
                return;
            }

            BeforeValue();
            Append('@');

            // The tuple has to follow the '@' with no separator, so mark it as the pending value.
            _pendingValue = true;
            BeginTuple();

            if (reference.HasEntityId)
            {
                WriteUInt64(reference.EntityId);
            }

            WriteIdentifier(reference.Source == VslAssetSource.Resource ? "resource" : "addressable");
            WriteString(reference.Key);
            EndTuple();
        }

        public void WriteReference(ulong id)
        {
            BeforeValue();
            Append('@');
            EnsureCapacity(24);
            if (id.TryFormat(_buffer.AsSpan(_length), out var written, default, CultureInfo.InvariantCulture))
            {
                _length += written;
                return;
            }

            Append(id.ToString(CultureInfo.InvariantCulture));
        }

        public void WriteNullReference()
        {
            BeforeValue();
            Append("@null");
        }

        #endregion

        #region Layout

        private bool IsInline => _depth > 0 && _depth <= MaxTrackedDepth &&
                                 (_inlineFlags & (1UL << (_depth - 1))) != 0;

        private bool IsTuple => _depth > 0 && _depth <= MaxTrackedDepth &&
                                (_tupleFlags & (1UL << (_depth - 1))) != 0;

        private bool IsFirst => _depth > 0 && _depth <= MaxTrackedDepth &&
                                (_firstFlags & (1UL << (_depth - 1))) != 0;

        private void Push(bool inline, bool tuple)
        {
            // Inline is inherited: nothing inside a one-line container may break the line.
            var parentInline = IsInline;
            _depth++;

            // Guarding here rather than in each formatter is what makes every path cycle-safe: a
            // cycle through a List or a Dictionary never reaches an object formatter's own depth
            // check, but it cannot avoid opening a container.
            if (_depth > _maxDepth)
            {
                throw new VslException(
                    $"Exceeded the maximum nesting depth of {_maxDepth}. This usually means the object graph contains a reference cycle, which VSL has no syntax to encode.");
            }

            if (_depth > MaxTrackedDepth)
            {
                return;
            }

            var bit = 1UL << (_depth - 1);
            if (inline || parentInline)
            {
                _inlineFlags |= bit;
            }
            else
            {
                _inlineFlags &= ~bit;
            }

            if (tuple)
            {
                _tupleFlags |= bit;
            }
            else
            {
                _tupleFlags &= ~bit;
            }

            _firstFlags |= bit;
            _pendingValue = false;
        }

        private void End(char closer)
        {
            var inline = IsInline;
            var empty = IsFirst;
            _depth--;

            if (empty)
            {
                Append(closer);
                return;
            }

            if (inline)
            {
                if (closer != ')')
                {
                    Append(' ');
                }

                Append(closer);
                return;
            }

            Append(_newLine);
            WriteIndent();
            Append(closer);
        }

        private void BeforeEntry()
        {
            if (_depth == 0)
            {
                return;
            }

            var inline = IsInline;
            var tuple = IsTuple;

            if (IsFirst)
            {
                if (_depth <= MaxTrackedDepth)
                {
                    _firstFlags &= ~(1UL << (_depth - 1));
                }

                if (!inline)
                {
                    Append(_newLine);
                    WriteIndent();
                }
                else if (!tuple)
                {
                    Append(' ');
                }

                return;
            }

            if (tuple)
            {
                Append(", ");
            }
            else if (inline)
            {
                Append("  ");
            }
            else
            {
                Append(_newLine);
                WriteIndent();
            }
        }

        private void BeforeValue()
        {
            if (_pendingValue)
            {
                _pendingValue = false;
                return;
            }

            BeforeEntry();
        }

        private void WriteIndent()
        {
            var spaces = _depth * _indentWidth;
            if (spaces <= 0)
            {
                return;
            }

            EnsureCapacity(spaces);
            for (var i = 0; i < spaces; i++)
            {
                _buffer[_length++] = ' ';
            }
        }

        private bool ShouldWriteRawBlock(string value) =>
            value.IndexOf('\n') >= 0 &&
            !IsInline &&
            _depth < MaxTrackedDepth &&
            value.IndexOf("\"\"\"", StringComparison.Ordinal) < 0 &&
            value.IndexOf('\0') < 0;

        /// <summary>
        /// Writes a <c>"""</c> block, indenting the content one level past the member and closing at
        /// that same indent — the layout <see cref="VslReader"/> strips back off on read.
        /// </summary>
        private void WriteRawBlock(string value)
        {
            Append("\"\"\"");

            var contentIndent = (_depth + 1) * _indentWidth;
            var start = 0;
            while (start <= value.Length)
            {
                var newline = value.IndexOf('\n', start);
                var end = newline < 0 ? value.Length : newline;

                var line = value.AsSpan(start, end - start);
                if (line.Length > 0 && line[line.Length - 1] == '\r')
                {
                    line = line.Slice(0, line.Length - 1);
                }

                Append(_newLine);
                AppendSpaces(contentIndent);
                Append(line);

                if (newline < 0)
                {
                    break;
                }

                start = newline + 1;
            }

            Append(_newLine);
            AppendSpaces(contentIndent);
            Append("\"\"\"");
        }

        private bool WriteSpecialFloat(double value)
        {
            if (double.IsNaN(value))
            {
                Append("nan");
                return true;
            }

            if (double.IsPositiveInfinity(value))
            {
                Append("inf");
                return true;
            }

            if (double.IsNegativeInfinity(value))
            {
                Append("-inf");
                return true;
            }

            return false;
        }

        #endregion

        #region Buffer

        private void AppendSpaces(int count)
        {
            if (count <= 0)
            {
                return;
            }

            EnsureCapacity(count);
            for (var i = 0; i < count; i++)
            {
                _buffer[_length++] = ' ';
            }
        }

        private void Append(char c)
        {
            EnsureCapacity(1);
            _buffer[_length++] = c;
        }

        private void Append(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            EnsureCapacity(text.Length);
            text.AsSpan().CopyTo(_buffer.AsSpan(_length));
            _length += text.Length;
        }

        private void Append(ReadOnlySpan<char> text)
        {
            if (text.Length == 0)
            {
                return;
            }

            EnsureCapacity(text.Length);
            text.CopyTo(_buffer.AsSpan(_length));
            _length += text.Length;
        }

        private void EnsureCapacity(int additional)
        {
            if (_length + additional <= _buffer.Length)
            {
                return;
            }

            var capacity = _buffer.Length;
            while (capacity < _length + additional)
            {
                capacity *= 2;
            }

            var next = ArrayPool<char>.Shared.Rent(capacity);
            _buffer.AsSpan(0, _length).CopyTo(next);
            ArrayPool<char>.Shared.Return(_buffer);
            _buffer = next;
        }

        #endregion
    }
}
