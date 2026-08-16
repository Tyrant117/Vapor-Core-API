using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using Object = UnityEngine.Object;

namespace Vapor.Serialization
{
    /// <summary>
    /// Resolves the formatter for a type, and is where generated or hand-written formatters are
    /// registered.
    /// </summary>
    /// <remarks>
    /// Lookups go through <c>Cache&lt;T&gt;.Instance</c> — one static field per closed generic — so
    /// the typed path costs a field read rather than a dictionary probe. The untyped path keeps a
    /// <see cref="ConcurrentDictionary{TKey,TValue}"/> for reflection and polymorphic slots.
    /// </remarks>
    [NoAutoStaticsCleanup]
    public static class VslFormatterRegistry
    {
        private static readonly ConcurrentDictionary<Type, IVslFormatter> s_Formatters =
            new ConcurrentDictionary<Type, IVslFormatter>();

        /// <summary>
        /// Called for a type no built-in rule covers. The reflection layer installs itself here, and
        /// generated formatters register ahead of it.
        /// </summary>
        public static Func<Type, IVslFormatter> FallbackFactory { get; set; }

        static VslFormatterRegistry()
        {
            RegisterBuiltIns();
        }

        [NoAutoStaticsCleanup]
        private static class Cache<T>
        {
            public static IVslFormatter<T> Instance;
        }

        #region Registration

        /// <summary>Registers a formatter, replacing any existing one for the same type.</summary>
        public static void Register<T>(IVslFormatter<T> formatter)
        {
            if (formatter == null)
            {
                throw new ArgumentNullException(nameof(formatter));
            }

            s_Formatters[typeof(T)] = formatter;
            Cache<T>.Instance = formatter;
        }

        /// <summary>True when a formatter is already resolved for this type.</summary>
        public static bool IsRegistered(Type type) => s_Formatters.ContainsKey(type);

        #endregion

        #region Resolution

        public static IVslFormatter<T> Get<T>()
        {
            var cached = Cache<T>.Instance;
            if (cached != null)
            {
                return cached;
            }

            if (Get(typeof(T)) is not IVslFormatter<T> typed)
            {
                throw new VslException($"No VSL formatter is available for {typeof(T)}.");
            }

            Cache<T>.Instance = typed;
            return typed;
        }

        public static IVslFormatter Get(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (s_Formatters.TryGetValue(type, out var existing))
            {
                return existing;
            }

            var created = Create(type);
            if (created == null)
            {
                throw new VslException(
                    $"No VSL formatter is available for {type}. Mark the type [VslSerializable], give its members [VslSerialize], or register a formatter for it.");
            }

            return s_Formatters.GetOrAdd(type, created);
        }

        private static IVslFormatter Create(Type type)
        {
            if (type.IsEnum)
            {
                return Instantiate(typeof(EnumFormatter<>), type);
            }

            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null)
            {
                return Instantiate(typeof(NullableFormatter<>), underlying);
            }

            if (type.IsArray)
            {
                if (type.GetArrayRank() != 1)
                {
                    throw new VslException($"VSL does not support multi-dimensional arrays ({type}).");
                }

                return Instantiate(typeof(ArrayFormatter<>), type.GetElementType());
            }

            // Anything deriving from UnityEngine.Object is a reference, whatever else it looks like.
            if (typeof(Object).IsAssignableFrom(type))
            {
                return Instantiate(typeof(UnityObjectFormatter<>), type);
            }

            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                var arguments = type.GetGenericArguments();

                if (definition == typeof(List<>)) return Instantiate(typeof(ListFormatter<>), arguments);
                if (definition == typeof(HashSet<>)) return Instantiate(typeof(HashSetFormatter<>), arguments);
                if (definition == typeof(Queue<>)) return Instantiate(typeof(QueueFormatter<>), arguments);
                if (definition == typeof(Stack<>)) return Instantiate(typeof(StackFormatter<>), arguments);
                if (definition == typeof(LinkedList<>)) return Instantiate(typeof(LinkedListFormatter<>), arguments);
                if (definition == typeof(Dictionary<,>)) return Instantiate(typeof(DictionaryFormatter<,>), arguments);
                if (definition == typeof(KeyValuePair<,>)) return Instantiate(typeof(KeyValuePairFormatter<,>), arguments);
                if (definition == typeof(ValueTuple<,>)) return Instantiate(typeof(ValueTupleFormatter<,>), arguments);
                if (definition == typeof(ValueTuple<,,>)) return Instantiate(typeof(ValueTupleFormatter<,,>), arguments);
                if (definition == typeof(AssetRef<>)) return Instantiate(typeof(AssetRefFormatter<>), arguments);
            }

            return FallbackFactory?.Invoke(type);
        }

        /// <summary>
        /// Closes a generic formatter over the given arguments.
        /// </summary>
        /// <remarks>
        /// This is the one AOT-sensitive path in VSL: under IL2CPP, a closed generic over a value
        /// type that appears nowhere in statically reachable code may not have been compiled. The
        /// source generator emits explicit references for every type it sees, which is what keeps
        /// this working in a build; hand-registering a formatter has the same effect.
        /// </remarks>
        private static IVslFormatter Instantiate(Type definition, params Type[] arguments)
        {
            try
            {
                return (IVslFormatter)Activator.CreateInstance(definition.MakeGenericType(arguments));
            }
            catch (Exception ex)
            {
                throw new VslException(
                    $"Could not create {definition.Name} for {string.Join(", ", Array.ConvertAll(arguments, a => a.Name))}.",
                    ex);
            }
        }

        #endregion

        #region Convenience

        public static void Write<T>(ref VslWriter writer, in T value, VslContext context) =>
            Get<T>().Write(ref writer, value, context);

        public static T Read<T>(ref VslReader reader, VslContext context) =>
            Get<T>().Read(ref reader, context);

        #endregion

        private static void RegisterBuiltIns()
        {
            // Anything the built-in rules do not cover falls through to reflection. A generated
            // formatter registers itself over the top of this, so the fallback is only ever the
            // slow-but-correct path.
            FallbackFactory = VslReflection.CreateFormatter;

            Register(BooleanFormatter.Instance);
            Register(CharFormatter.Instance);
            Register(SByteFormatter.Instance);
            Register(ByteFormatter.Instance);
            Register(Int16Formatter.Instance);
            Register(UInt16Formatter.Instance);
            Register(Int32Formatter.Instance);
            Register(UInt32Formatter.Instance);
            Register(Int64Formatter.Instance);
            Register(UInt64Formatter.Instance);
            Register(SingleFormatter.Instance);
            Register(DoubleFormatter.Instance);
            Register(DecimalFormatter.Instance);
            Register(StringFormatter.Instance);

            Register(DateTimeFormatter.Instance);
            Register(DateTimeOffsetFormatter.Instance);
            Register(TimeSpanFormatter.Instance);
            Register(GuidFormatter.Instance);
            Register(UriFormatter.Instance);
            Register(VersionFormatter.Instance);

            Register(Vector2Formatter.Instance);
            Register(Vector3Formatter.Instance);
            Register(Vector4Formatter.Instance);
            Register(Vector2IntFormatter.Instance);
            Register(Vector3IntFormatter.Instance);
            Register(QuaternionFormatter.Instance);
            Register(Matrix4x4Formatter.Instance);

            // The Burst maths types, written identically to the engine ones above. Without them
            // anything written for jobs — where a vector is a float3, not a Vector3 — falls through to
            // the reflection formatter and serializes as a nested object with three members: correct,
            // and unreadable in a format whose purpose is being read.
            Register(Float2Formatter.Instance);
            Register(Float3Formatter.Instance);
            Register(Float4Formatter.Instance);
            Register(Int2Formatter.Instance);
            Register(Int3Formatter.Instance);
            Register(QuaternionMathFormatter.Instance);

            Register(ColorFormatter.Instance);
            Register(Color32Formatter.Instance);
            Register(GradientFormatter.Instance);
            Register(GradientColorKeyFormatter.Instance);
            Register(GradientAlphaKeyFormatter.Instance);

            Register(RectFormatter.Instance);
            Register(RectIntFormatter.Instance);
            Register(BoundsFormatter.Instance);
            Register(BoundsIntFormatter.Instance);

            Register(LayerMaskFormatter.Instance);
            Register(RenderingLayerMaskFormatter.Instance);
            Register(AnimationCurveFormatter.Instance);
            Register(KeyframeFormatter.Instance);
            Register(Hash128Formatter.Instance);

            Register(LocalizedStringFormatter.Instance);

            Register(GameplayTagFormatter.Instance);
            Register(GameplayTagContainerFormatter.Instance);
        }
    }
}
