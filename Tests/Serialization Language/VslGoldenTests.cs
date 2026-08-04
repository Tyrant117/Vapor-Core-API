using NUnit.Framework;
using UnityEngine;
using Vapor.Serialization;

namespace Vapor.Tests.Serialization
{
    /// <summary>
    /// Pins the exact bytes VSL writes.
    /// </summary>
    /// <remarks>
    /// This is the test that matters most for the format's purpose. Round-trip tests pass just as
    /// happily if the layout silently drifts — but the whole point of VSL is that a model reads and
    /// writes it, so an unnoticed change to spacing, ordering, or the choice between inline and block
    /// is a real regression. If this test fails, decide whether the change was intended before
    /// updating the expectation.
    /// </remarks>
    public class VslGoldenTests
    {
        // Built by joining on "\n" rather than as a verbatim literal: the writer always emits "\n",
        // and a verbatim string in a CRLF file would not match.
        private static readonly string Expected = string.Join("\n", new[]
        {
            "@vsl 1",
            "",
            "{",
            "  # 0-1, drives the HUD bar",
            "  healthFraction: 0.62",
            "  label: \"Aria \\\"the Bold\\\"\"",
            "  count: 7",
            "  position: (0, 1.5, -3)",
            "  tint: 0xFF8800FF",
            "  mode: Fire",
            "  wards: Fire | Shock",
            "  values: [ 1  2  3 ]",
            "  counts: { kills: 42 }",
            "  child: { id: \"sword\"  count: 1 }",
            "  notes: \"\"\"",
            "    First line.",
            "    Second line.",
            "    \"\"\"",
            "}",
        });

        [Test]
        public void CanonicalFixtureMatchesTheGoldenDocument()
        {
            Assert.AreEqual(Expected, Vsl.Serialize(GoldenFixture.Seeded()));
        }

        [Test]
        public void GoldenDocumentReadsBackIntoAnEqualObject()
        {
            var fixture = Vsl.Deserialize<GoldenFixture>(Expected);

            Assert.AreEqual(0.62f, fixture.HealthFraction, 1e-6f);
            Assert.AreEqual("Aria \"the Bold\"", fixture.Label);
            Assert.AreEqual(7, fixture.Count);
            Assert.AreEqual(new Vector3(0f, 1.5f, -3f), fixture.Position);
            Assert.AreEqual(Element.Fire, fixture.Mode);
            Assert.AreEqual(Resist.Fire | Resist.Shock, fixture.Wards);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, fixture.Values);
            Assert.AreEqual(42, fixture.Counts["kills"]);
            Assert.AreEqual("sword", fixture.Child.Id);
            Assert.AreEqual("First line.\nSecond line.", fixture.Notes);
        }

        [Test]
        public void HeaderIsOptionalOnRead()
        {
            var withoutHeader = Expected.Substring(Expected.IndexOf('{'));
            Assert.AreEqual("Aria \"the Bold\"", Vsl.Deserialize<GoldenFixture>(withoutHeader).Label);
        }

        [Test]
        public void OptionsChangeLayoutPredictably()
        {
            var options = VslOptions.Default.Clone();
            options.EmitHeader = false;
            options.EmitComments = false;
            options.IndentWidth = 4;

            var text = Vsl.Serialize(GoldenFixture.Seeded(), new VslContext(options));

            StringAssert.DoesNotContain("@vsl", text);
            StringAssert.DoesNotContain("# 0-1", text);
            StringAssert.Contains("\n    healthFraction:", text);
        }
    }
}
