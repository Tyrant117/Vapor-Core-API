#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Vapor.Inspector
{
    /// <summary>
    /// The token kinds the resolver grammar recognises.
    /// </summary>
    internal enum ResolverTokenType
    {
        End,
        Identifier,
        Number,
        String,
        Char,
        True,
        False,
        Null,

        Dot,
        QuestionDot,
        Question,
        Colon,
        QuestionQuestion,

        Not,
        Minus,
        AndAlso,
        OrElse,
        Equal,
        NotEqual,
        Less,
        Greater,
        LessOrEqual,
        GreaterOrEqual,

        OpenParen,
        CloseParen,
    }

    internal readonly struct ResolverToken
    {
        public readonly ResolverTokenType Type;
        public readonly string Text;
        public readonly object Value;
        public readonly int Position;

        public ResolverToken(ResolverTokenType type, string text, int position, object value = null)
        {
            Type = type;
            Text = text;
            Position = position;
            Value = value;
        }

        public override string ToString() => Text ?? Type.ToString();
    }

    /// <summary>
    /// Raised for anything the author could have written differently — a stray character, a missing
    /// operand, a name that is not a member. Carries the column so the message can point at it.
    /// </summary>
    /// <remarks>
    /// Binding failures use this too, not just lexing and parsing. From the author's side there is no
    /// difference worth surfacing between "I cannot read this" and "this reads fine but names nothing",
    /// and both need the same thing from the message: where to look.
    /// </remarks>
    internal sealed class ResolverSyntaxException : Exception
    {
        public int Position { get; }

        public ResolverSyntaxException(string message, int position) : base(message)
        {
            Position = position;
        }

        /// <summary>
        /// The message with a caret line under the offending column.
        /// </summary>
        public string Describe(string expression)
        {
            if (expression == null)
            {
                return Message;
            }

            var caret = new StringBuilder(expression.Length + 1);
            var column = Math.Clamp(Position, 0, expression.Length);
            caret.Append(' ', column).Append('^');
            return $"{Message}\n    {expression}\n    {caret}";
        }
    }

    #region - Syntax Tree -
    internal abstract class ResolverNode
    {
        public int Position;
    }

    internal sealed class ResolverLiteralNode : ResolverNode
    {
        public object Value;

        /// <summary>
        /// Null for the <c>null</c> literal, which takes its type from whatever it is compared against.
        /// </summary>
        public Type Type;
    }

    /// <summary>
    /// A bare name at the head of a chain. Resolved against the parent object first, and only if that
    /// fails considered as a type name — see <see cref="ResolverBinder"/>.
    /// </summary>
    internal sealed class ResolverNameNode : ResolverNode
    {
        public string Name;

        /// <summary>
        /// True when the author wrote the parentheses. A zero-argument method binds either way; the
        /// parentheses only make the intent explicit.
        /// </summary>
        public bool Invoked;
    }

    internal sealed class ResolverMemberNode : ResolverNode
    {
        public ResolverNode Target;
        public string Name;
        public bool Invoked;

        /// <summary>
        /// True for <c>?.</c> — the chain yields null instead of throwing when the target is null.
        /// </summary>
        public bool NullConditional;
    }

    internal sealed class ResolverUnaryNode : ResolverNode
    {
        public ResolverTokenType Operator;
        public ResolverNode Operand;
    }

    internal sealed class ResolverBinaryNode : ResolverNode
    {
        public ResolverTokenType Operator;
        public ResolverNode Left;
        public ResolverNode Right;
    }

    internal sealed class ResolverCoalesceNode : ResolverNode
    {
        public ResolverNode Left;
        public ResolverNode Right;
    }

    internal sealed class ResolverConditionalNode : ResolverNode
    {
        public ResolverNode Condition;
        public ResolverNode IfTrue;
        public ResolverNode IfFalse;
    }
    #endregion

    /// <summary>
    /// Turns a resolver string into tokens. Deliberately smaller than C#: no arithmetic, no indexers and
    /// no method arguments, because every one of those widens the surface an inspector attribute can fail
    /// on without answering a question a conditional actually asks.
    /// </summary>
    internal static class ResolverLexer
    {
        public static List<ResolverToken> Tokenize(string expression)
        {
            var tokens = new List<ResolverToken>(16);
            var i = 0;
            while (i < expression.Length)
            {
                var c = expression[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                var start = i;
                if (char.IsLetter(c) || c == '_')
                {
                    while (i < expression.Length && (char.IsLetterOrDigit(expression[i]) || expression[i] == '_'))
                    {
                        i++;
                    }

                    var word = expression[start..i];
                    tokens.Add(word switch
                    {
                        "true" => new ResolverToken(ResolverTokenType.True, word, start, true),
                        "false" => new ResolverToken(ResolverTokenType.False, word, start, false),
                        "null" => new ResolverToken(ResolverTokenType.Null, word, start),
                        _ => new ResolverToken(ResolverTokenType.Identifier, word, start),
                    });
                    continue;
                }

                if (char.IsDigit(c))
                {
                    tokens.Add(ReadNumber(expression, ref i));
                    continue;
                }

                switch (c)
                {
                    case '"':
                        tokens.Add(ReadString(expression, ref i));
                        continue;
                    case '\'':
                        tokens.Add(ReadChar(expression, ref i));
                        continue;
                }

                var two = i + 1 < expression.Length ? expression.Substring(i, 2) : null;
                switch (two)
                {
                    case "?.":
                        tokens.Add(new ResolverToken(ResolverTokenType.QuestionDot, two, start));
                        i += 2;
                        continue;
                    case "??":
                        tokens.Add(new ResolverToken(ResolverTokenType.QuestionQuestion, two, start));
                        i += 2;
                        continue;
                    case "&&":
                        tokens.Add(new ResolverToken(ResolverTokenType.AndAlso, two, start));
                        i += 2;
                        continue;
                    case "||":
                        tokens.Add(new ResolverToken(ResolverTokenType.OrElse, two, start));
                        i += 2;
                        continue;
                    case "==":
                        tokens.Add(new ResolverToken(ResolverTokenType.Equal, two, start));
                        i += 2;
                        continue;
                    case "!=":
                        tokens.Add(new ResolverToken(ResolverTokenType.NotEqual, two, start));
                        i += 2;
                        continue;
                    case "<=":
                        tokens.Add(new ResolverToken(ResolverTokenType.LessOrEqual, two, start));
                        i += 2;
                        continue;
                    case ">=":
                        tokens.Add(new ResolverToken(ResolverTokenType.GreaterOrEqual, two, start));
                        i += 2;
                        continue;
                }

                var single = c switch
                {
                    '.' => ResolverTokenType.Dot,
                    '?' => ResolverTokenType.Question,
                    ':' => ResolverTokenType.Colon,
                    '!' => ResolverTokenType.Not,
                    '-' => ResolverTokenType.Minus,
                    '<' => ResolverTokenType.Less,
                    '>' => ResolverTokenType.Greater,
                    '(' => ResolverTokenType.OpenParen,
                    ')' => ResolverTokenType.CloseParen,
                    _ => ResolverTokenType.End,
                };

                if (single == ResolverTokenType.End)
                {
                    // '&' and '|' get a pointed message because writing the single form is the most
                    // likely way to reach here, and "unexpected character" would not say why.
                    var hint = c is '&' or '|' ? $" Use '{c}{c}' rather than '{c}'." : string.Empty;
                    throw new ResolverSyntaxException($"Unexpected character '{c}'.{hint}", start);
                }

                tokens.Add(new ResolverToken(single, c.ToString(), start));
                i++;
            }

            tokens.Add(new ResolverToken(ResolverTokenType.End, "end of expression", expression.Length));
            return tokens;
        }

        /// <summary>
        /// Integers bind as <see cref="int"/> and anything with a point or an <c>f</c> suffix as
        /// <see cref="float"/>, so that <c>@Health &gt; 0.5</c> compares against a float field without a
        /// double-to-float narrowing the author never asked for.
        /// </summary>
        private static ResolverToken ReadNumber(string expression, ref int i)
        {
            var start = i;
            var isFloat = false;
            while (i < expression.Length && char.IsDigit(expression[i]))
            {
                i++;
            }

            if (i < expression.Length && expression[i] == '.' && i + 1 < expression.Length && char.IsDigit(expression[i + 1]))
            {
                isFloat = true;
                i++;
                while (i < expression.Length && char.IsDigit(expression[i]))
                {
                    i++;
                }
            }

            var digits = expression[start..i];
            if (i < expression.Length && (expression[i] == 'f' || expression[i] == 'F'))
            {
                isFloat = true;
                i++;
            }

            if (isFloat)
            {
                if (!float.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                {
                    throw new ResolverSyntaxException($"'{digits}' is not a valid number.", start);
                }

                return new ResolverToken(ResolverTokenType.Number, digits, start, f);
            }

            if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            {
                throw new ResolverSyntaxException($"'{digits}' does not fit in an int.", start);
            }

            return new ResolverToken(ResolverTokenType.Number, digits, start, n);
        }

        private static ResolverToken ReadString(string expression, ref int i)
        {
            var start = i;
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= expression.Length)
                {
                    throw new ResolverSyntaxException("Unterminated string literal.", start);
                }

                var c = expression[i];
                if (c == '"')
                {
                    i++;
                    return new ResolverToken(ResolverTokenType.String, sb.ToString(), start, sb.ToString());
                }

                sb.Append(c == '\\' ? ReadEscape(expression, ref i) : c);
                i++;
            }
        }

        private static ResolverToken ReadChar(string expression, ref int i)
        {
            var start = i;
            i++;
            if (i >= expression.Length)
            {
                throw new ResolverSyntaxException("Unterminated character literal.", start);
            }

            var value = expression[i] == '\\' ? ReadEscape(expression, ref i) : expression[i];
            i++;
            if (i >= expression.Length || expression[i] != '\'')
            {
                throw new ResolverSyntaxException("Unterminated character literal.", start);
            }

            i++;
            return new ResolverToken(ResolverTokenType.Char, value.ToString(), start, value);
        }

        /// <summary>
        /// Reads the character after a backslash, leaving <paramref name="i"/> on the last character
        /// consumed so the caller's own increment lands past it.
        /// </summary>
        private static char ReadEscape(string expression, ref int i)
        {
            i++;
            if (i >= expression.Length)
            {
                throw new ResolverSyntaxException("Expression ends with an incomplete escape sequence.", i - 1);
            }

            return expression[i] switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                '0' => '\0',
                '\\' => '\\',
                '"' => '"',
                '\'' => '\'',
                var other => throw new ResolverSyntaxException($"Unknown escape sequence '\\{other}'.", i - 1),
            };
        }
    }

    /// <summary>
    /// Recursive descent over the token list, one method per precedence level, lowest first.
    /// </summary>
    /// <remarks>
    /// The grammar is a strict subset of C# and follows C#'s precedence exactly, so an author who guesses
    /// from C# habits is never wrong about what binds tighter. What is missing is missing outright rather
    /// than reinterpreted — there is no operator here that means something different than it does in C#.
    /// </remarks>
    internal static class ResolverParser
    {
        public static ResolverNode Parse(string expression)
        {
            var tokens = ResolverLexer.Tokenize(expression);
            var index = 0;
            var node = ParseTernary(tokens, ref index);
            var token = tokens[index];
            if (token.Type != ResolverTokenType.End)
            {
                throw new ResolverSyntaxException($"Unexpected '{token.Text}' after a complete expression.", token.Position);
            }

            return node;
        }

        private static ResolverNode ParseTernary(List<ResolverToken> tokens, ref int i)
        {
            var condition = ParseCoalesce(tokens, ref i);
            if (tokens[i].Type != ResolverTokenType.Question)
            {
                return condition;
            }

            var position = tokens[i].Position;
            i++;
            var ifTrue = ParseTernary(tokens, ref i);
            Expect(tokens, ref i, ResolverTokenType.Colon, "':'");
            var ifFalse = ParseTernary(tokens, ref i);
            return new ResolverConditionalNode { Condition = condition, IfTrue = ifTrue, IfFalse = ifFalse, Position = position };
        }

        /// <summary>Right associative, as in C#, so <c>a ?? b ?? c</c> is <c>a ?? (b ?? c)</c>.</summary>
        private static ResolverNode ParseCoalesce(List<ResolverToken> tokens, ref int i)
        {
            var left = ParseOrElse(tokens, ref i);
            if (tokens[i].Type != ResolverTokenType.QuestionQuestion)
            {
                return left;
            }

            var position = tokens[i].Position;
            i++;
            var right = ParseCoalesce(tokens, ref i);
            return new ResolverCoalesceNode { Left = left, Right = right, Position = position };
        }

        private static ResolverNode ParseOrElse(List<ResolverToken> tokens, ref int i)
        {
            var left = ParseAndAlso(tokens, ref i);
            while (tokens[i].Type == ResolverTokenType.OrElse)
            {
                var position = tokens[i].Position;
                i++;
                var right = ParseAndAlso(tokens, ref i);
                left = new ResolverBinaryNode { Operator = ResolverTokenType.OrElse, Left = left, Right = right, Position = position };
            }

            return left;
        }

        private static ResolverNode ParseAndAlso(List<ResolverToken> tokens, ref int i)
        {
            var left = ParseEquality(tokens, ref i);
            while (tokens[i].Type == ResolverTokenType.AndAlso)
            {
                var position = tokens[i].Position;
                i++;
                var right = ParseEquality(tokens, ref i);
                left = new ResolverBinaryNode { Operator = ResolverTokenType.AndAlso, Left = left, Right = right, Position = position };
            }

            return left;
        }

        private static ResolverNode ParseEquality(List<ResolverToken> tokens, ref int i)
        {
            var left = ParseRelational(tokens, ref i);
            while (tokens[i].Type is ResolverTokenType.Equal or ResolverTokenType.NotEqual)
            {
                var op = tokens[i].Type;
                var position = tokens[i].Position;
                i++;
                var right = ParseRelational(tokens, ref i);
                left = new ResolverBinaryNode { Operator = op, Left = left, Right = right, Position = position };
            }

            return left;
        }

        private static ResolverNode ParseRelational(List<ResolverToken> tokens, ref int i)
        {
            var left = ParseUnary(tokens, ref i);
            while (tokens[i].Type is ResolverTokenType.Less or ResolverTokenType.Greater
                   or ResolverTokenType.LessOrEqual or ResolverTokenType.GreaterOrEqual)
            {
                var op = tokens[i].Type;
                var position = tokens[i].Position;
                i++;
                var right = ParseUnary(tokens, ref i);
                left = new ResolverBinaryNode { Operator = op, Left = left, Right = right, Position = position };
            }

            return left;
        }

        private static ResolverNode ParseUnary(List<ResolverToken> tokens, ref int i)
        {
            var token = tokens[i];
            if (token.Type is not (ResolverTokenType.Not or ResolverTokenType.Minus))
            {
                return ParsePrimary(tokens, ref i);
            }

            i++;
            var operand = ParseUnary(tokens, ref i);
            return new ResolverUnaryNode { Operator = token.Type, Operand = operand, Position = token.Position };
        }

        private static ResolverNode ParsePrimary(List<ResolverToken> tokens, ref int i)
        {
            var token = tokens[i];
            switch (token.Type)
            {
                case ResolverTokenType.Number:
                    i++;
                    return new ResolverLiteralNode { Value = token.Value, Type = token.Value.GetType(), Position = token.Position };
                case ResolverTokenType.String:
                    i++;
                    return new ResolverLiteralNode { Value = token.Value, Type = typeof(string), Position = token.Position };
                case ResolverTokenType.Char:
                    i++;
                    return new ResolverLiteralNode { Value = token.Value, Type = typeof(char), Position = token.Position };
                case ResolverTokenType.True:
                case ResolverTokenType.False:
                    i++;
                    return new ResolverLiteralNode { Value = token.Value, Type = typeof(bool), Position = token.Position };
                case ResolverTokenType.Null:
                    i++;
                    return new ResolverLiteralNode { Value = null, Type = null, Position = token.Position };
                case ResolverTokenType.OpenParen:
                {
                    i++;
                    var inner = ParseTernary(tokens, ref i);
                    Expect(tokens, ref i, ResolverTokenType.CloseParen, "')'");
                    return inner;
                }
                case ResolverTokenType.Identifier:
                    return ParseChain(tokens, ref i);
                default:
                    throw new ResolverSyntaxException($"Expected a value but found '{token.Text}'.", token.Position);
            }
        }

        private static ResolverNode ParseChain(List<ResolverToken> tokens, ref int i)
        {
            var head = tokens[i];
            i++;
            ResolverNode node = new ResolverNameNode { Name = head.Text, Invoked = TryEatEmptyArgumentList(tokens, ref i), Position = head.Position };

            while (tokens[i].Type is ResolverTokenType.Dot or ResolverTokenType.QuestionDot)
            {
                var nullConditional = tokens[i].Type == ResolverTokenType.QuestionDot;
                i++;
                var name = tokens[i];
                if (name.Type != ResolverTokenType.Identifier)
                {
                    throw new ResolverSyntaxException($"Expected a member name after '{(nullConditional ? "?." : ".")}' but found '{name.Text}'.", name.Position);
                }

                i++;
                node = new ResolverMemberNode
                {
                    Target = node,
                    Name = name.Text,
                    NullConditional = nullConditional,
                    Invoked = TryEatEmptyArgumentList(tokens, ref i),
                    Position = name.Position,
                };
            }

            return node;
        }

        /// <summary>
        /// Consumes <c>()</c> when it is there. Arguments are rejected here rather than in the binder so
        /// the message can name the real limit instead of complaining about a stray token.
        /// </summary>
        private static bool TryEatEmptyArgumentList(List<ResolverToken> tokens, ref int i)
        {
            if (tokens[i].Type != ResolverTokenType.OpenParen)
            {
                return false;
            }

            var open = tokens[i];
            i++;
            if (tokens[i].Type != ResolverTokenType.CloseParen)
            {
                throw new ResolverSyntaxException("Resolver methods are called without arguments.", open.Position);
            }

            i++;
            return true;
        }

        private static void Expect(List<ResolverToken> tokens, ref int i, ResolverTokenType type, string expected)
        {
            if (tokens[i].Type != type)
            {
                throw new ResolverSyntaxException($"Expected {expected} but found '{tokens[i].Text}'.", tokens[i].Position);
            }

            i++;
        }
    }
}
#endif
