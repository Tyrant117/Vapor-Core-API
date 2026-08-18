#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Unity.Scripting.LifecycleManagement;

namespace Vapor.Inspector
{
    /// <summary>
    /// Why a resolver string could not be turned into an accessor, and whether anyone has been told yet.
    /// </summary>
    /// <remarks>
    /// The instance is cached alongside the failure, so <see cref="ClaimConsoleReport"/> is what keeps a
    /// broken resolver from writing a line to the console on every editor update for the rest of the
    /// session. The message itself stays available for as long as the inspector wants to draw it.
    /// </remarks>
    public sealed class ResolverCompileError
    {
        public string Message { get; }

        private bool _reported;

        /// <summary>
        /// Public so the editor assembly can report a failure it detects before compilation is even
        /// attempted — a resolver with no property to read from, say — through the same once-only path.
        /// </summary>
        public ResolverCompileError(string message)
        {
            Message = message;
        }

        /// <summary>
        /// True exactly once per failure, for the caller that should write it to the console.
        /// </summary>
        public bool ClaimConsoleReport()
        {
            if (_reported)
            {
                return false;
            }

            _reported = true;
            return true;
        }
    }

    /// <summary>
    /// Compiles the string behind an <c>@</c> resolver into a delegate that reads it off an object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The inspector polls every resolver on every editor update. Doing that through
    /// <see cref="System.Reflection.MemberInfo"/> cost a reflection dispatch and — worse — boxed the
    /// result of every bool, float and Color resolver once per frame, so a busy inspector produced a
    /// steady stream of garbage to answer questions whose answers almost never changed. A compiled
    /// delegate returns <typeparamref name="T"/> directly and allocates nothing.
    /// </para>
    /// <para>
    /// Editor-only by construction. <see cref="LambdaExpression.Compile()"/> needs a JIT and throws under
    /// IL2CPP, and there is no runtime caller to serve anyway — attribute constructors run
    /// <see cref="ResolverUtility.HasResolver"/>, which is a string test and stays outside this guard.
    /// </para>
    /// </remarks>
    [NoAutoStaticsCleanup]
    public static partial class ResolverExpression
    {
        private readonly struct Key : IEquatable<Key>
        {
            private readonly Type _targetType;
            private readonly string _expression;
            private readonly Type _resultType;

            public Key(Type targetType, string expression, Type resultType)
            {
                _targetType = targetType;
                _expression = expression;
                _resultType = resultType;
            }

            public bool Equals(Key other) => _targetType == other._targetType
                                             && _resultType == other._resultType
                                             && string.Equals(_expression, other._expression, StringComparison.Ordinal);

            public override bool Equals(object obj) => obj is Key other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(_targetType, _expression, _resultType);
        }

        private static readonly Dictionary<Key, Delegate> s_Compiled = new();
        private static readonly Dictionary<Key, ResolverCompileError> s_Failures = new();

        /// <summary>
        /// Cleared on recompile rather than left to <c>[AutoStaticsCleanup]</c>, for the same reason
        /// <c>ReflectionUtility</c> is: the cache keys off <see cref="Type"/>, so it stays valid across a
        /// play-mode entry with domain reload disabled and only a recompile can invalidate it.
        /// </summary>
        [OnCodeInitializing]
        private static void Initialize()
        {
            ResetCaches();
        }

        /// <summary>
        /// Drops every compiled accessor and remembered failure.
        /// </summary>
        /// <remarks>
        /// Exposed for tests. A failure is announced to the console once per domain, which is the right
        /// behaviour and also means a test asserting on that first announcement passes only until the
        /// next run in the same domain. Resetting between tests is what makes them say the same thing
        /// every time rather than only after a recompile.
        /// </remarks>
        internal static void ResetCaches()
        {
            s_Compiled.Clear();
            s_Failures.Clear();
        }

        /// <summary>
        /// Compiles <paramref name="expression"/> against <paramref name="targetType"/>, or explains why
        /// it could not. Results are cached both ways, so a broken resolver costs one dictionary lookup
        /// per tick rather than one failed parse.
        /// </summary>
        /// <param name="targetType">The type the expression's bare names are read from.</param>
        /// <param name="expression">The resolver string with its leading <c>@</c> already removed.</param>
        /// <param name="accessor">Reads the value out of a boxed instance of <paramref name="targetType"/>.</param>
        /// <param name="error">Set when this returns false.</param>
        public static bool TryCompile<T>(Type targetType, string expression, out Func<object, T> accessor, out ResolverCompileError error)
        {
            accessor = null;
            error = null;

            if (targetType == null || string.IsNullOrWhiteSpace(expression))
            {
                // An empty resolver almost always means the '@' was left off the attribute argument:
                // HasResolver keeps only what follows the '@', so a string without one arrives here as
                // nothing at all rather than as the member name the author actually typed.
                error = new ResolverCompileError(targetType == null
                    ? "The resolver has no type to read from. This usually means a missing script."
                    : "The resolver is empty. A resolver argument has to start with '@', as in \"@MyField\".");
                return false;
            }

            var key = new Key(targetType, expression, typeof(T));
            if (s_Compiled.TryGetValue(key, out var cached))
            {
                accessor = (Func<object, T>)cached;
                return true;
            }

            if (s_Failures.TryGetValue(key, out error))
            {
                return false;
            }

            try
            {
                var node = Parse(expression);
                var lambda = ResolverBinder.Bind(node, targetType, typeof(T));
                accessor = (Func<object, T>)lambda.Compile();
                s_Compiled[key] = accessor;
                return true;
            }
            catch (ResolverSyntaxException syntax)
            {
                error = new ResolverCompileError($"Resolver '@{expression}' on {targetType.Name}: {syntax.Describe(expression)}");
            }
            catch (Exception e)
            {
                // Anything reaching here is a hole in the binder rather than a mistake the author made,
                // so it names the expression and then gets out of the way of the real stack trace.
                error = new ResolverCompileError($"Resolver '@{expression}' on {targetType.Name} could not be compiled: {e.Message}");
            }

            s_Failures[key] = error;
            return false;
        }

        /// <summary>
        /// A bare member name skips the lexer and parser entirely.
        /// </summary>
        /// <remarks>
        /// Not for speed — compilation happens once per type and the tokens would be a rounding error.
        /// It is so that every resolver written before there was a grammar keeps binding through the one
        /// path that never had one, and no future change to the operator set can regress them.
        /// </remarks>
        private static ResolverNode Parse(string expression)
        {
            var trimmed = expression.Trim();
            return IsBareIdentifier(trimmed)
                ? new ResolverNameNode { Name = trimmed, Invoked = false, Position = 0 }
                : ResolverParser.Parse(expression);
        }

        private static bool IsBareIdentifier(string text)
        {
            if (text.Length == 0 || !(char.IsLetter(text[0]) || text[0] == '_'))
            {
                return false;
            }

            foreach (var c in text)
            {
                if (!char.IsLetterOrDigit(c) && c != '_')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
#endif
