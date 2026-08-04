using System;
using NUnit.Framework;
using Vapor.Serialization;

namespace Vapor.Tests.Serialization
{
    /// <summary>
    /// Lexer, reader and writer behaviour, independent of any object model.
    /// </summary>
    public class VslSyntaxTests
    {
        private static VslReader Reader(string source) => new VslReader(source.AsSpan(), VslContext.Default);

        #region Trivia

        [Test]
        public void CommasAreTrivia()
        {
            // The single most common structural mistake in generated data. Both spellings must parse.
            Assert.AreEqual(2, CountMembers("{\"a\": 1, \"b\": 2,}"));
            Assert.AreEqual(2, CountMembers("{ a: 1  b: 2 }"));
        }

        [Test]
        public void SeparatorsAreOptional()
        {
            Assert.AreEqual(3, CountMembers("{a:1 b:2 c:3}"));
            Assert.AreEqual(3, CountMembers("{\n  a: 1\n  b: 2\n  c: 3\n}"));
        }

        [Test]
        public void CommentsAreLegalAnywhere()
        {
            const string source = @"
# leading
{
  a: 1   # trailing
  # between members
  b: 2
}
# after";
            Assert.AreEqual(2, CountMembers(source));
        }

        [Test]
        public void HandlesCrlfAndLf()
        {
            Assert.AreEqual(2, CountMembers("{\r\n  a: 1\r\n  b: 2\r\n}"));
            Assert.AreEqual(2, CountMembers("{\n  a: 1\n  b: 2\n}"));
        }

        #endregion

        #region Scalars

        [Test]
        public void ParsesNumberForms()
        {
            Assert.AreEqual(-1500d, Reader("-1.5e3").ReadDouble(), 1e-9);
            Assert.AreEqual(255UL, Reader("0xFF").ReadUInt64());
            Assert.AreEqual(3, Reader("3.9").ReadInt64(), "a float literal truncates into an integer slot");
            Assert.IsTrue(double.IsPositiveInfinity(Reader("inf").ReadDouble()));
            Assert.IsTrue(double.IsNegativeInfinity(Reader("-inf").ReadDouble()));
            Assert.IsTrue(double.IsNaN(Reader("nan").ReadDouble()));
        }

        [Test]
        public void DecodesStringEscapes()
        {
            Assert.AreEqual("quoted \"escape\"", Reader("\"quoted \\\"escape\\\"\"").ReadString());
            Assert.AreEqual("tab\there", Reader("\"tab\\there\"").ReadString());
            Assert.AreEqual("\u00e9", Reader("\"\\u00E9\"").ReadString());
        }

        [Test]
        public void AcceptsBareWordAsString()
        {
            Assert.AreEqual("Aria", Reader("Aria").ReadString());
        }

        [Test]
        public void RawStringStripsClosingDelimiterIndent()
        {
            const string source = "\"\"\"\n    line one\n      indented\n    line three\n    \"\"\"";
            Assert.AreEqual("line one\n  indented\nline three", Reader(source).ReadString());
        }

        [Test]
        public void SingleLineRawString()
        {
            Assert.AreEqual("inline", Reader("\"\"\"inline\"\"\"").ReadString());
        }

        #endregion

        #region Fixed arity

        [Test]
        public void ShortTupleDefaultsMissingComponents()
        {
            var reader = Reader("(1, 2)");
            reader.ReadTupleStart();
            var x = reader.ReadSingleOr();
            var y = reader.ReadSingleOr();
            var z = reader.ReadSingleOr();
            reader.ReadTupleEnd();

            Assert.AreEqual(1f, x);
            Assert.AreEqual(2f, y);
            Assert.AreEqual(0f, z);
        }

        [Test]
        public void LongTupleDiscardsExtraComponents()
        {
            var reader = Reader("(1, 2, 3, 4, 5)");
            reader.ReadTupleStart();
            var x = reader.ReadSingleOr();
            var y = reader.ReadSingleOr();
            reader.ReadTupleEnd();

            Assert.AreEqual(1f, x);
            Assert.AreEqual(2f, y);
        }

        [Test]
        public void ReadEndAfterTryReadItemDoesNotOverrun()
        {
            // TryRead*Item consumes the closer itself; a following Read*End has to be a no-op rather
            // than run off the end of the document. A ref struct cannot be captured in a lambda, so
            // this asserts by reaching the end rather than with Assert.DoesNotThrow.
            var reader = Reader("(7, 8)");
            reader.ReadTupleStart();
            var a = reader.TryReadTupleItem() ? reader.ReadSingle() : 0f;
            var b = reader.TryReadTupleItem() ? reader.ReadSingle() : 0f;
            var c = reader.TryReadTupleItem() ? reader.ReadSingle() : 0f;
            reader.ReadTupleEnd();

            Assert.AreEqual(7f, a);
            Assert.AreEqual(8f, b);
            Assert.AreEqual(0f, c);
        }

        [Test]
        public void NestedContainersDoNotLeaveStaleState()
        {
            var reader = Reader("[ { a: 1 } { a: 2 } ]");
            reader.ReadSequenceStart();

            var objects = 0;
            while (reader.TryReadSequenceItem())
            {
                reader.ReadObjectStart();
                while (reader.TryReadMemberName(out _))
                {
                    reader.SkipValue();
                }

                reader.ReadObjectEnd();
                objects++;
            }

            reader.ReadSequenceEnd();
            Assert.AreEqual(2, objects);
        }

        #endregion

        #region Errors

        [TestCase("{ a: }", TestName = "missing value")]
        [TestCase("{ a 1 }", TestName = "missing colon")]
        [TestCase("[ 1 2", TestName = "unterminated sequence")]
        [TestCase("{ a: \"oops\n\" }", TestName = "newline in a plain string")]
        [TestCase("\"\"\"never closed", TestName = "unterminated raw string")]
        [TestCase("{ a: [ 1 2 } ]", TestName = "mismatched closer")]
        public void RejectsMalformedInput(string source)
        {
            Assert.Throws<VslException>(() => Drain(source));
        }

        [Test]
        public void ErrorPointsAtTheMistakeNotTheEnd()
        {
            var exception = Assert.Throws<VslException>(() => Drain("{\n  a: 1\n  b: @\n}"));
            Assert.AreEqual(3, exception.Line, "the bad token is on line 3");
        }

        #endregion

        #region Writer

        [Test]
        public void WriterLayoutIsDeterministic()
        {
            var writer = new VslWriter(VslContext.Default);
            try
            {
                writer.BeginObject();

                writer.WriteMember("tuple");
                writer.BeginTuple();
                writer.WriteSingle(0f);
                writer.WriteSingle(1.5f);
                writer.WriteSingle(-3f);
                writer.EndTuple();

                writer.WriteMember("emptySequence");
                writer.BeginSequence();
                writer.EndSequence();

                writer.WriteMember("emptyObject");
                writer.BeginObject();
                writer.EndObject();

                writer.WriteMember("hex");
                writer.WriteHex(0xFF8800FFUL, 8);

                writer.WriteMember("reference");
                writer.WriteReference(1234UL);

                writer.WriteMember("nullReference");
                writer.WriteNullReference();

                writer.WriteMember("special");
                writer.BeginTuple();
                writer.WriteSingle(float.PositiveInfinity);
                writer.WriteSingle(float.NegativeInfinity);
                writer.WriteSingle(float.NaN);
                writer.EndTuple();

                writer.EndObject();

                var text = writer.ToString();
                StringAssert.Contains("tuple: (0, 1.5, -3)", text);
                StringAssert.Contains("emptySequence: []", text);
                StringAssert.Contains("emptyObject: {}", text);
                StringAssert.Contains("hex: 0xFF8800FF", text);
                StringAssert.Contains("reference: @1234", text);
                StringAssert.Contains("nullReference: @null", text);
                StringAssert.Contains("special: (inf, -inf, nan)", text);
            }
            finally
            {
                writer.Dispose();
            }
        }

        [Test]
        public void FlagChainHasNoStraySeparator()
        {
            var writer = new VslWriter(VslContext.Default);
            try
            {
                writer.BeginObject();
                writer.WriteMember("mode");
                writer.WriteIdentifier("Additive");
                writer.WriteFlagSeparator();
                writer.WriteIdentifier("Blend");
                writer.EndObject();

                StringAssert.Contains("mode: Additive | Blend", writer.ToString());
            }
            finally
            {
                writer.Dispose();
            }
        }

        [Test]
        public void QuotesMemberNamesThatAreNotBareIdentifiers()
        {
            var writer = new VslWriter(VslContext.Default);
            try
            {
                writer.BeginObject();
                writer.WriteMember("has space");
                writer.WriteInt64(1);
                writer.EndObject();

                StringAssert.Contains("\"has space\": 1", writer.ToString());
            }
            finally
            {
                writer.Dispose();
            }
        }

        #endregion

        #region Helpers

        private static int CountMembers(string source)
        {
            var reader = Reader(source);
            reader.ReadObjectStart();

            var count = 0;
            while (reader.TryReadMemberName(out _))
            {
                reader.SkipValue();
                count++;
            }

            return count;
        }

        private static void Drain(string source)
        {
            var reader = Reader(source);
            switch (reader.PeekKind())
            {
                case VslValueKind.Object:
                    reader.ReadObjectStart();
                    while (reader.TryReadMemberName(out _))
                    {
                        reader.SkipValue();
                    }

                    break;
                case VslValueKind.Sequence:
                    reader.ReadSequenceStart();
                    reader.ReadSequenceEnd();
                    break;
                default:
                    reader.SkipValue();
                    break;
            }
        }

        #endregion
    }
}
