using Unity.Scripting.LifecycleManagement;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Vapor.Inspector;
using Object = UnityEngine.Object;

namespace Vapor.Tests.Inspector
{
    /// <summary>
    /// Covers the resolver grammar end to end: parse, bind and compile, then run the delegate.
    /// </summary>
    /// <remarks>
    /// Tested through <see cref="ResolverExpression.TryCompile{T}"/> rather than against the parser
    /// directly, because a resolver that parses but binds to the wrong member is indistinguishable from a
    /// broken one to the person who wrote it, and only the whole path catches that.
    /// </remarks>
    [TestFixture]
    public class ResolverExpressionTests
    {
        public enum Modes { Basic, Advanced, Expert }

        public class Inner
        {
            public int Depth = 7;
            public string Tag = "inner";
            public Inner Next;
        }

        /// <summary>
        /// Exempt from statics cleanup: <see cref="Limit"/> is a fixed value the resolver tests bind
        /// to as a static field, not state that accumulates. Resetting it between play-mode sessions
        /// would restore the number it already has.
        /// </summary>
        [NoAutoStaticsCleanup]
        public class Sample
        {
            public bool IsLocked;
            public int Health = 50;
            public float Ratio = 0.25f;
            public string Name = "hero";
            public Modes Mode = Modes.Advanced;
            public Inner Child;
            public Object Asset;
            public List<int> Items = new() { 1, 2, 3 };

            private bool _hidden = true;

            public bool PrivateProbe => _hidden;
            public bool Computed => Health > 10;
            public int Score() => Health * 2;
            public static int Limit = 99;
        }

        /// <summary>
        /// The compiler caches accessors and failures for the life of the domain, so without this a test
        /// that asserts on a first-time compile would pass on the run after a recompile and fail on every
        /// run after that.
        /// </summary>
        [SetUp]
        public void ResetCompilerCaches()
        {
            ResolverExpression.ResetCaches();
        }

        private static T Eval<T>(string expression, object target)
        {
            Assert.IsTrue(ResolverExpression.TryCompile<T>(target.GetType(), expression, out var accessor, out var error),
                $"'{expression}' failed to compile: {error?.Message}");
            return accessor(target);
        }

        private static string ErrorOf<T>(string expression, object target)
        {
            Assert.IsFalse(ResolverExpression.TryCompile<T>(target.GetType(), expression, out _, out var error),
                $"'{expression}' compiled but should not have.");
            return error.Message;
        }

        #region - Bare members, as they worked before there was a grammar -
        [TestCase("IsLocked", false)]
        [TestCase("Computed", true)]
        [TestCase("PrivateProbe", true)]
        [TestCase("_hidden", true)]
        public void BareMember_Bool(string expression, bool expected)
        {
            Assert.AreEqual(expected, Eval<bool>(expression, new Sample()));
        }

        [TestCase("Health", 50)]
        [TestCase("Score", 100)]
        [TestCase("Score()", 100)]
        [TestCase("Limit", 99)]
        public void BareMember_Int(string expression, int expected)
        {
            Assert.AreEqual(expected, Eval<int>(expression, new Sample()));
        }

        [Test]
        public void BareMember_ReadsStringWithoutQuoting()
        {
            Assert.AreEqual("hero", Eval<string>("Name", new Sample()));
        }

        [Test]
        public void NullTarget_YieldsDefaultRatherThanThrowing()
        {
            Assert.IsTrue(ResolverExpression.TryCompile<bool>(typeof(Sample), "IsLocked", out var accessor, out _));
            Assert.IsFalse(accessor(null));
        }
        #endregion

        #region - Operators -
        [TestCase("!IsLocked", true)]
        [TestCase("Health > 10", true)]
        [TestCase("Health > 100", false)]
        [TestCase("Health >= 50 && !IsLocked", true)]
        [TestCase("IsLocked || Health == 50", true)]
        [TestCase("Health != 50", false)]
        [TestCase("Ratio < 1", true)]
        [TestCase("Ratio > 0.1", true)]
        [TestCase("Health > -5", true)]
        [TestCase("-Health < 0", true)]
        [TestCase("Name == \"hero\"", true)]
        [TestCase("Name != \"villain\"", true)]
        public void Operators(string expression, bool expected)
        {
            Assert.AreEqual(expected, Eval<bool>(expression, new Sample()));
        }

        [TestCase("true || false && false", true, TestName = "AndAlso binds tighter than OrElse")]
        [TestCase("(true || false) && false", false, TestName = "Parentheses override precedence")]
        [TestCase("Health > 10 == true", true, TestName = "Relational binds tighter than equality")]
        [TestCase("!IsLocked && Health > 10 || false", true, TestName = "Not binds tightest")]
        public void Precedence_MatchesCSharp(string expression, bool expected)
        {
            Assert.AreEqual(expected, Eval<bool>(expression, new Sample()));
        }

        [Test]
        public void AndAlso_ShortCircuits_SoTheRightSideIsNotEvaluatedOnANullChain()
        {
            // Would throw if `Child.Depth` ran while Child is null.
            Assert.IsFalse(Eval<bool>("Child != null && Child.Depth > 5", new Sample()));
        }
        #endregion

        #region - Enums -
        [TestCase("Mode == Modes.Advanced", true)]
        [TestCase("Mode != Modes.Basic", true)]
        [TestCase("Mode > Modes.Basic", true)]
        [TestCase("Mode >= Modes.Advanced", true)]
        [TestCase("Mode < Modes.Expert", true)]
        [TestCase("Mode == Modes.Basic || Mode == Modes.Advanced", true)]
        public void Enums(string expression, bool expected)
        {
            Assert.AreEqual(expected, Eval<bool>(expression, new Sample()));
        }

        [Test]
        public void Enum_ResolvesWhenTheConstantIsWrittenFirst()
        {
            Assert.IsTrue(Eval<bool>("Modes.Advanced == Mode", new Sample()));
        }

        [Test]
        public void Enum_ResolvesFromTheRequestedTypeAlone()
        {
            Assert.AreEqual(Modes.Expert, Eval<Modes>("Modes.Expert", new Sample()));
        }
        #endregion

        #region - Static types -
        [Test]
        public void StaticReadonly_OnValueTypes()
        {
            Assert.AreEqual(Color.red, Eval<Color>("Color.red", new Sample()));
            Assert.AreEqual(Vector3.zero, Eval<Vector3>("Vector3.zero", new Sample()));
            Assert.IsTrue(Eval<bool>("Health < int.MaxValue", new Sample()));
        }
        #endregion

        #region - Chains -
        [Test]
        public void MemberChains()
        {
            var target = new Sample { Child = new Inner { Next = new Inner { Depth = 42 } } };
            Assert.AreEqual(7, Eval<int>("Child.Depth", target));
            Assert.AreEqual(42, Eval<int>("Child.Next.Depth", target));
            Assert.IsTrue(Eval<bool>("Child.Tag == \"inner\"", target));
        }

        [Test]
        public void ChainsReachIntoFrameworkTypes()
        {
            Assert.IsTrue(Eval<bool>("Items.Count > 2", new Sample()));
        }
        #endregion

        #region - Null conditional and coalesce -
        [Test]
        public void NullConditional_ShortCircuitsTheWholeTail()
        {
            var shallow = new Sample { Child = new Inner() };
            var deep = new Sample { Child = new Inner { Next = new Inner { Depth = 42 } } };

            Assert.AreEqual(0, Eval<int>("Child?.Depth ?? 0", new Sample()));
            Assert.AreEqual(7, Eval<int>("Child?.Depth ?? 0", shallow));
            Assert.AreEqual(-1, Eval<int>("Child?.Next?.Depth ?? -1", shallow));
            Assert.AreEqual(42, Eval<int>("Child?.Next?.Depth ?? -1", deep));

            // A ?. earlier in the chain guards the plain dots after it, as it does in C#.
            Assert.AreEqual(-1, Eval<int>("Child?.Next.Depth ?? -1", new Sample()));
        }

        [Test]
        public void NullConditional_OnReferenceResults()
        {
            Assert.AreEqual("none", Eval<string>("Child?.Tag ?? \"none\"", new Sample()));
            Assert.AreEqual("inner", Eval<string>("Child?.Tag ?? \"none\"", new Sample { Child = new Inner() }));
        }

        [Test]
        public void ComparingANullChainIsFalse_NotAnError()
        {
            Assert.IsFalse(Eval<bool>("Child?.Depth > 5", new Sample()));
            Assert.IsTrue(Eval<bool>("Child?.Depth > 5", new Sample { Child = new Inner() }));
        }
        #endregion

        #region - Unity object null -
        [Test]
        public void UnityObjectComparison_UsesUnitySemantics()
        {
            var target = new Sample();
            Assert.IsTrue(Eval<bool>("Asset == null", target));

            target.Asset = ScriptableObject.CreateInstance<ScriptableObject>();
            try
            {
                Assert.IsFalse(Eval<bool>("Asset == null", target));
                Assert.IsTrue(Eval<bool>("Asset != null", target));
            }
            finally
            {
                Object.DestroyImmediate(target.Asset);
            }

            // The managed reference is still there, but Unity considers it null and so do we.
            Assert.IsTrue(Eval<bool>("Asset == null", target));
        }
        #endregion

        #region - Ternary and coercion -
        [Test]
        public void Ternary()
        {
            var target = new Sample();
            Assert.AreEqual(2, Eval<int>("IsLocked ? 1 : 2", target));
            Assert.AreEqual("big", Eval<string>("Health > 10 ? \"big\" : \"small\"", target));
            Assert.AreEqual(Color.red, Eval<Color>("Health > 10 ? Color.red : Color.white", target));
            Assert.AreEqual(Color.white, Eval<Color>("IsLocked ? Color.red : Color.white", target));
        }

        [Test]
        public void Ternary_PromotesMixedNumericBranches()
        {
            Assert.AreEqual(0.25f, Eval<float>("IsLocked ? 1 : Ratio", new Sample()));
        }

        [Test]
        public void WideningConversionsAreImplicit()
        {
            var target = new Sample();
            Assert.AreEqual(50f, Eval<float>("Health", target));
            Assert.AreEqual(50, Eval<object>("Health", target));
        }
        #endregion

        #region - Diagnostics -
        [TestCase("Nope", "could not be resolved")]
        [TestCase("Health >", "Expected a value")]
        [TestCase("Health &  1", "Use '&&'")]
        [TestCase("Health + 1 > 2", "Unexpected character '+'")]
        [TestCase("Items[0] > 1", "Unexpected character '['")]
        [TestCase("Score(1)", "without arguments")]
        [TestCase("Name > 1", "needs two numbers")]
        [TestCase("Child.Missing != null", "has no 'Missing'")]
        [TestCase("Mode == Modes.Nope", "has no static 'Nope'")]
        [TestCase("Health ? 1 : 2", "Expected a true/false value")]
        [TestCase("(Health > 1", "Expected ')'")]
        [TestCase("Health >= 1 &&", "Expected a value")]
        public void Rejections_ExplainThemselves(string expression, string expectedFragment)
        {
            StringAssert.Contains(expectedFragment, ErrorOf<bool>(expression, new Sample()));
        }

        [Test]
        public void ResultTypeMismatchIsRejectedRatherThanCastAtRuntime()
        {
            StringAssert.Contains("cannot be read as", ErrorOf<bool>("Health", new Sample()));
        }

        [Test]
        public void CoalesceOnANonNullableLeftIsRejected()
        {
            StringAssert.Contains("needs something that can be null", ErrorOf<int>("Health ?? 1", new Sample()));
        }

        [Test]
        public void ParseErrorsPointAtTheOffendingColumn()
        {
            var message = ErrorOf<bool>("Health >= 1 &  2", new Sample());
            StringAssert.Contains("^", message);
        }

        [Test]
        public void AFailureIsAnnouncedToTheConsoleOnlyOnce()
        {
            ResolverExpression.TryCompile<bool>(typeof(Sample), "DefinitelyNotAMember", out _, out var first);
            Assert.IsTrue(first.ClaimConsoleReport(), "The first observer should report.");
            Assert.IsFalse(first.ClaimConsoleReport(), "The same failure must not report twice.");

            ResolverExpression.TryCompile<bool>(typeof(Sample), "DefinitelyNotAMember", out _, out var second);
            Assert.AreSame(first, second, "The failure should be cached, not rebuilt.");
            Assert.IsFalse(second.ClaimConsoleReport(), "A later observer of a reported failure stays quiet.");
        }
        #endregion

        #region - Caching -
        [Test]
        public void CompilingTheSameResolverTwiceReturnsTheSameDelegate()
        {
            Assert.IsTrue(ResolverExpression.TryCompile<bool>(typeof(Sample), "Health > 3 && !IsLocked", out var first, out _));
            Assert.IsTrue(ResolverExpression.TryCompile<bool>(typeof(Sample), "Health > 3 && !IsLocked", out var second, out _));
            Assert.AreSame(first, second);
        }

        [Test]
        public void TheCacheDistinguishesRequestedTypes()
        {
            Assert.IsTrue(ResolverExpression.TryCompile<int>(typeof(Sample), "Health", out _, out _));
            Assert.IsTrue(ResolverExpression.TryCompile<float>(typeof(Sample), "Health", out var asFloat, out _));
            Assert.AreEqual(50f, asFloat(new Sample()));
        }

        [Test]
        public void EmptyAndNullInputsFailCleanly()
        {
            Assert.IsFalse(ResolverExpression.TryCompile<bool>(typeof(Sample), "", out _, out var empty));
            Assert.IsNotNull(empty);

            Assert.IsFalse(ResolverExpression.TryCompile<bool>(null, "IsLocked", out _, out var noType));
            Assert.IsNotNull(noType);
        }
        #endregion
    }
}
