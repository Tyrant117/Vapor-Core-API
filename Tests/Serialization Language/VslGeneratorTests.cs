using NUnit.Framework;
using Vapor.Serialization;

namespace Vapor.Tests.Serialization
{
    /// <summary>
    /// The generated formatter and the reflection formatter must agree byte for byte.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both paths implement the same member-selection rules — one in <c>VslTypeSchema</c>, one in the
    /// generator's <c>VslModelBuilder</c>. Nothing but this test stops them drifting apart, which
    /// would mean a document written before the generator was installed no longer loading after.
    /// </para>
    /// <para>
    /// Unity scopes a Roslyn analyzer by folder, so the generator only reaches assemblies at or below
    /// its own. These tests report themselves inconclusive rather than passing vacuously when no
    /// generated formatter exists — copy the analyzer to <c>Assets/Analyzers/</c> to run them for
    /// real. See <c>Tools~/VaporVslGenerator/README.md</c>.
    /// </para>
    /// </remarks>
    public class VslGeneratorTests
    {
        private const string GeneratedFormatterName = "VslGeneratedFormatter";

        private static IVslFormatter<T> GeneratedFormatterFor<T>()
        {
            var nested = typeof(T).GetNestedType(GeneratedFormatterName);
            if (nested == null)
            {
                return null;
            }

            var instance = nested.GetField("Instance")?.GetValue(null);
            return instance as IVslFormatter<T>;
        }

        private static string SerializeWith<T>(IVslFormatter<T> formatter, T value)
        {
            var context = VslContext.Default;
            var writer = new VslWriter(context);
            try
            {
                writer.WriteHeader();
                formatter.Write(ref writer, value, context);
                return writer.ToString();
            }
            finally
            {
                writer.Dispose();
            }
        }

        private static void AssertMatchesReflection<T>(T value)
        {
            var generated = GeneratedFormatterFor<T>();
            if (generated == null)
            {
                Assert.Inconclusive(
                    $"No generated formatter for {typeof(T).Name}. The VSL analyzer is not in scope for this assembly — " +
                    "copy Vapor.Vsl.SourceGenerator.dll and its .meta to Assets/Analyzers/ to cover the whole project.");
                return;
            }

            var byReflection = SerializeWith(new ReflectionFormatter<T>(), value);
            var byGenerator = SerializeWith(generated, value);

            Assert.AreEqual(byReflection, byGenerator,
                $"{typeof(T).Name}: the generator's member discovery has drifted from VslTypeSchema's.");
        }

        [Test]
        public void GoldenFixtureMatchesReflection() => AssertMatchesReflection(GoldenFixture.Seeded());

        [Test]
        public void UnityTypesMatchReflection() => AssertMatchesReflection(UnityTypesFixture.Seeded());

        [Test]
        public void CollectionsMatchReflection() => AssertMatchesReflection(CollectionsFixture.Seeded());

        [Test]
        public void UnityRulesPolicyMatchesReflection() => AssertMatchesReflection(UnityRulesFixture.Seeded());

        [Test]
        public void InheritedMembersMatchReflection() =>
            AssertMatchesReflection(new FireballAbility { Cooldown = 2.5f, Damage = 25 });

        [Test]
        public void GeneratedReadRoundTrips()
        {
            var generated = GeneratedFormatterFor<GoldenFixture>();
            if (generated == null)
            {
                Assert.Inconclusive("No generated formatter; see AssertMatchesReflection.");
                return;
            }

            var previous = VslFormatterRegistry.Get<GoldenFixture>();
            try
            {
                VslFormatterRegistry.Register(generated);

                var text = Vsl.Serialize(GoldenFixture.Seeded());
                var copy = Vsl.Deserialize<GoldenFixture>(text);

                Assert.AreEqual("Aria \"the Bold\"", copy.Label);
                Assert.AreEqual(7, copy.Count, "a private [SerializeField] is reachable from the nested formatter");
                Assert.AreEqual(42, copy.Counts["kills"]);
                Assert.AreEqual("First line.\nSecond line.", copy.Notes);
                Assert.AreEqual(text, Vsl.Serialize(copy), "generated round trip is byte-stable");
            }
            finally
            {
                VslFormatterRegistry.Register(previous);
            }
        }

        [Test]
        public void GeneratedFormatterIsRegisteredOnLoad()
        {
            var generated = GeneratedFormatterFor<GoldenFixture>();
            if (generated == null)
            {
                Assert.Inconclusive("No generated formatter; see AssertMatchesReflection.");
                return;
            }

            Assert.AreSame(generated, VslFormatterRegistry.Get<GoldenFixture>(),
                "the generated registrar should have replaced the reflection fallback on domain load");
        }
    }
}
