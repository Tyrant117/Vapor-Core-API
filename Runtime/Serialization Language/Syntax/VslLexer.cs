using System;

namespace Vapor.Serialization
{
    internal enum VslTokenKind : byte
    {
        None = 0,
        EndOfFile,
        ObjectStart,    // {
        ObjectEnd,      // }
        SequenceStart,  // [
        SequenceEnd,    // ]
        TupleStart,     // (
        TupleEnd,       // )
        Colon,          // :
        Pipe,           // |
        TypeTag,        // !Ident        text = the identifier
        Directive,      // @ident        text = the identifier, e.g. "vsl"
        Reference,      // @1234         text = the digits
        ReferenceStart, // @(            the '(' is left for the reader to consume
        NullReference,  // @null
        Identifier,
        String,         // text = content between the quotes
        RawString,      // text = content between the triple quotes, before de-indenting
        Number,         // text = the literal, including any sign / exponent / inf / nan
        Hex,            // text = the digits after 0x
    }

    internal readonly struct VslToken
    {
        public readonly VslTokenKind Kind;
        public readonly int Start;
        public readonly int Length;
        public readonly int Line;
        public readonly int Column;
        public readonly bool HasEscapes;

        public VslToken(VslTokenKind kind, int start, int length, int line, int column, bool hasEscapes = false)
        {
            Kind = kind;
            Start = start;
            Length = length;
            Line = line;
            Column = column;
            HasEscapes = hasEscapes;
        }

        public bool IsValue => Kind is VslTokenKind.Identifier or VslTokenKind.String or VslTokenKind.RawString
            or VslTokenKind.Number or VslTokenKind.Hex or VslTokenKind.Reference or VslTokenKind.NullReference
            or VslTokenKind.ReferenceStart or VslTokenKind.ObjectStart or VslTokenKind.SequenceStart
            or VslTokenKind.TupleStart or VslTokenKind.TypeTag;
    }

    /// <summary>
    /// Single-pass tokenizer over a VSL document.
    /// </summary>
    /// <remarks>
    /// A <c>ref struct</c> over the source span: tokens carry offsets rather than substrings, so
    /// scanning a document allocates nothing. Whitespace, newlines, commas and comments are all
    /// trivia — the language carries structure entirely in its brackets, which is what lets a
    /// generated document survive stray or missing commas.
    /// </remarks>
    internal ref struct VslLexer
    {
        private readonly ReadOnlySpan<char> _source;
        private int _index;
        private int _line;
        private int _lineStart;

        public VslToken Current;

        public VslLexer(ReadOnlySpan<char> source)
        {
            _source = source;
            _index = 0;
            _line = 1;
            _lineStart = 0;
            Current = default;
        }

        public ReadOnlySpan<char> Source => _source;

        // Taken by value, not by 'in': a span returned from a by-ref parameter is treated as
        // potentially referencing it, which trips ref-safety analysis. The token is a few words wide,
        // so copying is free.
        public ReadOnlySpan<char> Text(VslToken token) => _source.Slice(token.Start, token.Length);

        /// <summary>Reads the next token, storing it in <see cref="Current"/>.</summary>
        public VslToken Next()
        {
            SkipTrivia();

            if (_index >= _source.Length)
            {
                return Current = new VslToken(VslTokenKind.EndOfFile, _index, 0, _line, ColumnAt(_index));
            }

            var start = _index;
            var line = _line;
            var column = ColumnAt(_index);
            var c = _source[_index];

            switch (c)
            {
                case '{': _index++; return Current = new VslToken(VslTokenKind.ObjectStart, start, 1, line, column);
                case '}': _index++; return Current = new VslToken(VslTokenKind.ObjectEnd, start, 1, line, column);
                case '[': _index++; return Current = new VslToken(VslTokenKind.SequenceStart, start, 1, line, column);
                case ']': _index++; return Current = new VslToken(VslTokenKind.SequenceEnd, start, 1, line, column);
                case '(': _index++; return Current = new VslToken(VslTokenKind.TupleStart, start, 1, line, column);
                case ')': _index++; return Current = new VslToken(VslTokenKind.TupleEnd, start, 1, line, column);
                case ':': _index++; return Current = new VslToken(VslTokenKind.Colon, start, 1, line, column);
                case '|': _index++; return Current = new VslToken(VslTokenKind.Pipe, start, 1, line, column);
                case '!': return Current = ScanTypeTag(line, column);
                case '@': return Current = ScanAt(line, column);
                case '"': return Current = ScanString(line, column);
            }

            if (c == '-' || IsDigit(c))
            {
                return Current = ScanNumber(line, column);
            }

            if (IsIdentifierStart(c))
            {
                while (_index < _source.Length && IsIdentifierPart(_source[_index]))
                {
                    _index++;
                }

                return Current = new VslToken(VslTokenKind.Identifier, start, _index - start, line, column);
            }

            throw new VslException($"Unexpected character '{c}'.", line, column);
        }

        /// <summary>Reads the next token without consuming it.</summary>
        public VslToken Peek()
        {
            var savedIndex = _index;
            var savedLine = _line;
            var savedLineStart = _lineStart;
            var savedCurrent = Current;

            var token = Next();

            _index = savedIndex;
            _line = savedLine;
            _lineStart = savedLineStart;
            Current = savedCurrent;
            return token;
        }

        private void SkipTrivia()
        {
            while (_index < _source.Length)
            {
                var c = _source[_index];
                if (c == '\n')
                {
                    _index++;
                    _line++;
                    _lineStart = _index;
                }
                else if (c == ' ' || c == '\t' || c == '\r' || c == ',')
                {
                    // Commas are trivia. A model that emits JSON-style separators and one that omits
                    // them both produce the same document.
                    _index++;
                }
                else if (c == '#')
                {
                    while (_index < _source.Length && _source[_index] != '\n')
                    {
                        _index++;
                    }
                }
                else
                {
                    break;
                }
            }
        }

        private VslToken ScanTypeTag(int line, int column)
        {
            _index++; // '!'
            var start = _index;
            while (_index < _source.Length && IsIdentifierPart(_source[_index]))
            {
                _index++;
            }

            if (_index == start)
            {
                throw new VslException("Expected a type name after '!'.", line, column);
            }

            return new VslToken(VslTokenKind.TypeTag, start, _index - start, line, column);
        }

        private VslToken ScanAt(int line, int column)
        {
            _index++; // '@'
            var start = _index;

            // '@(' introduces a reference carrying a durable asset locator. The '(' is left in place
            // so the reader can consume it with the ordinary tuple machinery.
            if (_index < _source.Length && _source[_index] == '(')
            {
                return new VslToken(VslTokenKind.ReferenceStart, start, 0, line, column);
            }

            if (_index < _source.Length && IsDigit(_source[_index]))
            {
                while (_index < _source.Length && IsDigit(_source[_index]))
                {
                    _index++;
                }

                return new VslToken(VslTokenKind.Reference, start, _index - start, line, column);
            }

            if (_index < _source.Length && IsIdentifierStart(_source[_index]))
            {
                while (_index < _source.Length && IsIdentifierPart(_source[_index]))
                {
                    _index++;
                }

                var text = _source.Slice(start, _index - start);
                return text.Equals("null".AsSpan(), StringComparison.OrdinalIgnoreCase)
                    ? new VslToken(VslTokenKind.NullReference, start, _index - start, line, column)
                    : new VslToken(VslTokenKind.Directive, start, _index - start, line, column);
            }

            throw new VslException("Expected an id, '(', 'null', or a directive name after '@'.", line, column);
        }

        private VslToken ScanString(int line, int column)
        {
            if (_index + 2 < _source.Length && _source[_index + 1] == '"' && _source[_index + 2] == '"')
            {
                return ScanRawString(line, column);
            }

            _index++; // opening quote
            var start = _index;
            var hasEscapes = false;

            while (true)
            {
                if (_index >= _source.Length)
                {
                    throw new VslException("Unterminated string.", line, column);
                }

                var c = _source[_index];
                if (c == '\\')
                {
                    hasEscapes = true;
                    _index += 2;
                    continue;
                }

                if (c == '"')
                {
                    break;
                }

                if (c == '\n')
                {
                    // Reported against the opening quote, which is where the mistake actually is.
                    throw new VslException("Unterminated string. Use a \"\"\" raw string for multi-line text.", line, column);
                }

                _index++;
            }

            var length = _index - start;
            _index++; // closing quote
            return new VslToken(VslTokenKind.String, start, length, line, column, hasEscapes);
        }

        private VslToken ScanRawString(int line, int column)
        {
            _index += 3; // opening """
            var start = _index;
            var closed = false;

            while (_index <= _source.Length - 3)
            {
                if (_source[_index] == '"' && _source[_index + 1] == '"' && _source[_index + 2] == '"')
                {
                    closed = true;
                    break;
                }

                if (_source[_index] == '\n')
                {
                    _line++;
                    _lineStart = _index + 1;
                }

                _index++;
            }

            if (!closed)
            {
                throw new VslException("Unterminated raw string; expected a closing \"\"\".", line, column);
            }

            var length = _index - start;
            _index += 3; // closing """
            return new VslToken(VslTokenKind.RawString, start, length, line, column);
        }

        private VslToken ScanNumber(int line, int column)
        {
            var start = _index;

            if (_source[_index] == '-')
            {
                _index++;
            }

            // -inf / -infinity: a signed word rather than digits.
            if (_index < _source.Length && IsIdentifierStart(_source[_index]))
            {
                while (_index < _source.Length && IsIdentifierPart(_source[_index]))
                {
                    _index++;
                }

                return new VslToken(VslTokenKind.Number, start, _index - start, line, column);
            }

            if (_index + 1 < _source.Length && _source[_index] == '0' &&
                (_source[_index + 1] == 'x' || _source[_index + 1] == 'X'))
            {
                _index += 2;
                var hexStart = _index;
                while (_index < _source.Length && IsHexDigit(_source[_index]))
                {
                    _index++;
                }

                if (_index == hexStart)
                {
                    throw new VslException("Expected hexadecimal digits after '0x'.", line, column);
                }

                return new VslToken(VslTokenKind.Hex, hexStart, _index - hexStart, line, column);
            }

            while (_index < _source.Length && IsDigit(_source[_index]))
            {
                _index++;
            }

            if (_index + 1 < _source.Length && _source[_index] == '.' && IsDigit(_source[_index + 1]))
            {
                _index++;
                while (_index < _source.Length && IsDigit(_source[_index]))
                {
                    _index++;
                }
            }

            if (_index < _source.Length && (_source[_index] == 'e' || _source[_index] == 'E'))
            {
                // Only consume the exponent if it is well formed; otherwise it belongs to whatever
                // token comes next.
                var save = _index;
                _index++;
                if (_index < _source.Length && (_source[_index] == '+' || _source[_index] == '-'))
                {
                    _index++;
                }

                if (_index < _source.Length && IsDigit(_source[_index]))
                {
                    while (_index < _source.Length && IsDigit(_source[_index]))
                    {
                        _index++;
                    }
                }
                else
                {
                    _index = save;
                }
            }

            if (_index == start)
            {
                throw new VslException("Expected a number.", line, column);
            }

            return new VslToken(VslTokenKind.Number, start, _index - start, line, column);
        }

        private int ColumnAt(int index) => index - _lineStart + 1;

        private static bool IsDigit(char c) => c >= '0' && c <= '9';

        private static bool IsHexDigit(char c) =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        private static bool IsIdentifierStart(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';

        private static bool IsIdentifierPart(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '.';
    }
}
