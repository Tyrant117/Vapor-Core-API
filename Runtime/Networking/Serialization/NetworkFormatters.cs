using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;

namespace Vapor.Networking
{
    /// <summary>
    /// The formatter registry: one formatter per type, resolved once and cached in a static generic
    /// slot so the hot path is a field read.
    /// </summary>
    /// <remarks>
    /// Resolution order for a type nobody registered explicitly: arrays of a formattable element,
    /// generic collections (<c>List&lt;&gt;</c>, <c>Dictionary&lt;,&gt;</c>, <c>HashSet&lt;&gt;</c>,
    /// <c>Nullable&lt;&gt;</c>, <c>KeyValuePair&lt;,&gt;</c>), enums, then any fallback resolvers.
    /// The generated formatters register themselves from a per-assembly registrar at load; explicit
    /// registration always wins over resolution, and re-registering replaces.
    /// <para>
    /// The generic-collection path uses <see cref="Type.MakeGenericType"/>, which under IL2CPP only
    /// works for instantiations the AOT compiler has already seen. The generator emits explicit
    /// registrations for every collection type that reaches the wire in generated code, so a build
    /// never relies on this path for those; it remains for the editor and for hand-written types.
    /// </para>
    /// </remarks>
    [NoAutoStaticsCleanup]
    public static class NetworkFormatters
    {
        private static readonly object s_Lock = new();
        private static readonly Dictionary<Type, INetworkFormatter> s_ByType = new();
        private static readonly Dictionary<Type, Func<Type, INetworkFormatter>> s_GenericFactories = new();
        private static readonly List<Func<Type, INetworkFormatter>> s_Fallbacks = new();
        private static readonly Dictionary<Type, INetworkFormatter> s_Resolved = new();

        static NetworkFormatters()
        {
            BuiltInFormatters.RegisterAll();
        }

        #region - Registration -

        public static void Register<T>(INetworkFormatter<T> formatter)
        {
            if (formatter == null) throw new ArgumentNullException(nameof(formatter));
            lock (s_Lock)
            {
                s_ByType[typeof(T)] = formatter;
                s_Resolved.Remove(typeof(T));
                Cache<T>.Instance = formatter;
            }
        }

        /// <summary>Registers a formatter from a write/read pair.</summary>
        public static void Register<T>(DelegateNetworkFormatter<T>.WriteDelegate write, DelegateNetworkFormatter<T>.ReadDelegate read) =>
            Register(new DelegateNetworkFormatter<T>(write, read));

        /// <summary>
        /// Registers a factory for an open generic type, e.g. <c>typeof(List&lt;&gt;)</c>. The factory
        /// receives the closed type and returns its formatter.
        /// </summary>
        public static void RegisterGenericFactory(Type genericTypeDefinition, Func<Type, INetworkFormatter> factory)
        {
            if (genericTypeDefinition == null) throw new ArgumentNullException(nameof(genericTypeDefinition));
            if (!genericTypeDefinition.IsGenericTypeDefinition) throw new ArgumentException("Expected an open generic type definition.", nameof(genericTypeDefinition));
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            lock (s_Lock)
            {
                s_GenericFactories[genericTypeDefinition] = factory;
            }
        }

        /// <summary>
        /// Adds a last-resort resolver, consulted in registration order for any type nothing else could
        /// serve. Return null to decline.
        /// </summary>
        public static void AddFallback(Func<Type, INetworkFormatter> resolver)
        {
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            lock (s_Lock)
            {
                s_Fallbacks.Add(resolver);
            }
        }

        public static bool IsRegistered(Type type)
        {
            lock (s_Lock)
            {
                return s_ByType.ContainsKey(type);
            }
        }

        #endregion

        #region - Lookup -

        public static INetworkFormatter<T> Get<T>()
        {
            var cached = Cache<T>.Instance;
            if (cached != null)
            {
                return cached;
            }

            if (TryResolve(typeof(T), out var formatter) && formatter is INetworkFormatter<T> typed)
            {
                Cache<T>.Instance = typed;
                return typed;
            }

            throw new NetworkFormatterMissingException(typeof(T));
        }

        public static bool TryGet<T>(out INetworkFormatter<T> formatter)
        {
            var cached = Cache<T>.Instance;
            if (cached != null)
            {
                formatter = cached;
                return true;
            }

            if (TryResolve(typeof(T), out var untyped) && untyped is INetworkFormatter<T> typed)
            {
                Cache<T>.Instance = typed;
                formatter = typed;
                return true;
            }

            formatter = null;
            return false;
        }

        public static INetworkFormatter Get(Type type)
        {
            if (TryResolve(type, out var formatter))
            {
                return formatter;
            }

            throw new NetworkFormatterMissingException(type);
        }

        public static bool TryGet(Type type, out INetworkFormatter formatter) => TryResolve(type, out formatter);

        public static bool CanSerialize(Type type) => TryResolve(type, out _);

        public static void Write<T>(NetworkWriter writer, in T value) => Get<T>().Write(writer, value);

        public static T Read<T>(NetworkReader reader) => Get<T>().Read(reader);

        #endregion

        #region - Resolution -

        private static bool TryResolve(Type type, out INetworkFormatter formatter)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            lock (s_Lock)
            {
                if (s_ByType.TryGetValue(type, out formatter) || s_Resolved.TryGetValue(type, out formatter))
                {
                    return formatter != null;
                }

                formatter = ResolveUncached(type);
                if (formatter != null)
                {
                    // Only successes are cached: a type that is missing now may be registered later,
                    // and its collections must start resolving the moment it is.
                    s_Resolved[type] = formatter;
                }

                return formatter != null;
            }
        }

        private static INetworkFormatter ResolveUncached(Type type)
        {
            if (type.IsArray && type.GetArrayRank() == 1)
            {
                var element = type.GetElementType();
                return element != null && TryResolveNoLock(element, out _)
                    ? Instantiate(typeof(ArrayFormatter<>), element)
                    : null;
            }

            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                var definition = type.GetGenericTypeDefinition();
                if (s_GenericFactories.TryGetValue(definition, out var factory))
                {
                    var built = factory(type);
                    if (built != null)
                    {
                        return built;
                    }
                }
            }

            if (type.IsEnum)
            {
                return Instantiate(typeof(EnumFormatter<>), type);
            }

            foreach (var fallback in s_Fallbacks)
            {
                var candidate = fallback(type);
                if (candidate != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>Resolution for element types, called with the lock already held.</summary>
        private static bool TryResolveNoLock(Type type, out INetworkFormatter formatter)
        {
            if (s_ByType.TryGetValue(type, out formatter) || s_Resolved.TryGetValue(type, out formatter))
            {
                return formatter != null;
            }

            formatter = ResolveUncached(type);
            if (formatter != null)
            {
                s_Resolved[type] = formatter;
            }

            return formatter != null;
        }

        internal static INetworkFormatter Instantiate(Type openFormatter, params Type[] arguments)
        {
            var closed = openFormatter.MakeGenericType(arguments);
            return (INetworkFormatter)Activator.CreateInstance(closed);
        }

        #endregion

        /// <summary>Per-type cache; a static generic class is the cheapest dictionary there is.</summary>
        [NoAutoStaticsCleanup]
        private static class Cache<T>
        {
            public static INetworkFormatter<T> Instance;
        }
    }
}
