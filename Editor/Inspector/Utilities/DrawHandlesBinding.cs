using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEngine;
using Vapor.Inspector;
using Object = UnityEngine.Object;

namespace VaporEditor.Inspector
{
    /// <summary>
    /// What <see cref="DrawHandlesAttribute"/> resolves to for one type: a compiled invoker, or the
    /// reason there isn't one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cached per type and — this is the point — cached for types that have <em>no</em> attribute too.
    /// The scene view calls <c>OnSceneGUI</c> many times per frame on every selected object, and the old
    /// code only remembered a lookup that succeeded. Every <c>VaporBehaviour</c> without the attribute,
    /// which is nearly all of them, therefore re-ran an inherited <see cref="Attribute"/> search on every
    /// repaint of every scene view, allocating as it went.
    /// </para>
    /// <para>
    /// The handler is invoked through a compiled delegate rather than
    /// <see cref="MethodBase.Invoke(object, object[])"/>, which also means an exception thrown by handle
    /// code arrives with its own stack trace instead of buried inside a
    /// <see cref="TargetInvocationException"/>.
    /// </para>
    /// </remarks>
    [NoAutoStaticsCleanup]
    internal sealed partial class DrawHandlesBinding
    {
        /// <summary>The signatures a handler may declare, in the order they are described to the author.</summary>
        private static readonly Type[][] k_AllowedSignatures =
        {
            Type.EmptyTypes,
            new[] { typeof(SceneView) },
            new[] { typeof(SceneView), typeof(Event) },
        };

        private static readonly Dictionary<Type, DrawHandlesBinding> s_Bindings = new();

        /// <summary>The one binding every type without the attribute shares.</summary>
        private static readonly DrawHandlesBinding s_None = new(null, null);

        private readonly Action<Object, SceneView> _invoke;

        /// <summary>
        /// Set once handle code has thrown. Handles run on every repaint, so a handler that throws would
        /// otherwise bury the console under identical stack traces and take the scene view's frame rate
        /// with it.
        /// </summary>
        private bool _faulted;

        public string Error { get; }

        public bool HasHandler => _invoke != null && !_faulted;

        public bool HasError => Error != null;

        private DrawHandlesBinding(Action<Object, SceneView> invoke, string error)
        {
            _invoke = invoke;
            Error = error;
        }

        [OnCodeInitializing]
        private static void Initialize()
        {
            s_Bindings.Clear();
        }

        public static DrawHandlesBinding Get(Type type)
        {
            if (type == null)
            {
                return s_None;
            }

            if (s_Bindings.TryGetValue(type, out var binding))
            {
                return binding;
            }

            binding = Create(type);
            s_Bindings[type] = binding;

            // Once per type per domain, which is what caching the binding buys. The inspector shows the
            // same text, but only for whoever happens to select the object; the console reaches the
            // author who wrote the attribute.
            if (binding.HasError)
            {
                Debug.LogWarning(binding.Error);
            }

            return binding;
        }

        private static DrawHandlesBinding Create(Type type)
        {
            var attribute = type.GetCustomAttribute<DrawHandlesAttribute>(true);
            if (attribute == null)
            {
                return s_None;
            }

            if (string.IsNullOrWhiteSpace(attribute.MethodName))
            {
                return new DrawHandlesBinding(null, $"[DrawHandles] on {type.Name} does not name a method.");
            }

            var method = FindHandler(type, attribute.MethodName, out var wrongSignature);
            if (method == null)
            {
                return new DrawHandlesBinding(null, wrongSignature != null
                    ? $"[DrawHandles] on {type.Name}: '{attribute.MethodName}' takes ({string.Join(", ", Array.ConvertAll(wrongSignature.GetParameters(), p => p.ParameterType.Name))}). "
                      + "A handles method takes no parameters, or (SceneView), or (SceneView, Event)."
                    : $"[DrawHandles] on {type.Name}: no method named '{attribute.MethodName}'. "
                      + "Use nameof() so a rename cannot silently break it.");
            }

            return new DrawHandlesBinding(Compile(type, method), null);
        }

        /// <summary>
        /// Finds the handler, reporting a same-named method whose parameters disqualify it rather than
        /// letting it read as missing — the two mistakes need very different fixes.
        /// </summary>
        private static MethodInfo FindHandler(Type type, string name, out MethodInfo wrongSignature)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            wrongSignature = null;

            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var candidate in t.GetMethods(flags))
                {
                    if (!string.Equals(candidate.Name, name, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (Matches(candidate, out _))
                    {
                        return candidate;
                    }

                    wrongSignature ??= candidate;
                }
            }

            return null;
        }

        private static bool Matches(MethodInfo method, out int signature)
        {
            var parameters = method.GetParameters();
            for (signature = 0; signature < k_AllowedSignatures.Length; signature++)
            {
                var expected = k_AllowedSignatures[signature];
                if (parameters.Length != expected.Length)
                {
                    continue;
                }

                var ok = true;
                for (var i = 0; i < expected.Length; i++)
                {
                    if (parameters[i].ParameterType == expected[i])
                    {
                        continue;
                    }

                    ok = false;
                    break;
                }

                if (ok)
                {
                    return true;
                }
            }

            signature = -1;
            return false;
        }

        private static Action<Object, SceneView> Compile(Type type, MethodInfo method)
        {
            Matches(method, out var signature);

            var targetParameter = Expression.Parameter(typeof(Object), "target");
            var viewParameter = Expression.Parameter(typeof(SceneView), "view");

            var arguments = signature switch
            {
                1 => new Expression[] { viewParameter },
                2 => new Expression[] { viewParameter, Expression.Property(null, typeof(Event), nameof(Event.current)) },
                _ => Array.Empty<Expression>(),
            };

            var call = method.IsStatic
                ? Expression.Call(method, arguments)
                : Expression.Call(Expression.Convert(targetParameter, type), method, arguments);

            return Expression.Lambda<Action<Object, SceneView>>(call, targetParameter, viewParameter).Compile();
        }

        public void Invoke(Object target, SceneView view)
        {
            if (!HasHandler)
            {
                return;
            }

            try
            {
                _invoke(target, view);
            }
            catch (Exception e)
            {
                _faulted = true;
                Debug.LogError($"[DrawHandles] on {target.GetType().Name} threw and has been disabled until the next recompile.\n{e}", target);
            }
        }
    }
}
