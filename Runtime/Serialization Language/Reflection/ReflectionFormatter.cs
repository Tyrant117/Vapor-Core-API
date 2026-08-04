using System;

namespace Vapor.Serialization
{
    /// <summary>
    /// Reads and writes an arbitrary type by walking its <see cref="VslTypeSchema"/>.
    /// </summary>
    /// <remarks>
    /// The fallback path. It is correct for everything, including types you cannot edit, and it is
    /// what runs before the source generator has produced a formatter — but it goes through
    /// <c>FieldInfo.GetValue</c>/<c>SetValue</c>, so every member boxes. The generated formatter for
    /// the same type writes byte-identical output with none of that.
    /// </remarks>
    public sealed class ReflectionFormatter<T> : VslFormatter<T>
    {
        private static readonly bool s_IsValueType = typeof(T).IsValueType;
        private static readonly bool s_IsSealed = typeof(T).IsSealed || typeof(T).IsValueType;
        private static readonly bool s_IsAbstract = typeof(T).IsAbstract || typeof(T).IsInterface;

        private VslTypeSchema _schema;
        private VslTypeSchema Schema => _schema ??= VslTypeSchema.Get(typeof(T));

        public override void Write(ref VslWriter writer, in T value, VslContext context)
        {
            if (!s_IsValueType && value is null)
            {
                writer.WriteNull();
                return;
            }

            // A member declared as a base type may hold a derived instance. Tag it and hand off, so
            // the document records what to rebuild rather than just the base type's slice of it.
            if (!s_IsSealed)
            {
                var runtimeType = value.GetType();
                if (runtimeType != typeof(T))
                {
                    writer.WriteTypeTag(VslTypeRegistry.GetTag(runtimeType));
                    VslFormatterRegistry.Get(runtimeType).WriteObject(ref writer, value, context);
                    return;
                }
            }

            var schema = Schema;
            context.EnterDepth();
            try
            {
                writer.BeginObject(schema.PrefersInline(context));
                foreach (var member in schema.Members)
                {
                    if (member.Comment != null)
                    {
                        writer.WriteComment(member.Comment);
                    }

                    writer.WriteMember(member.Name);
                    member.Formatter.WriteObject(ref writer, member.GetValue(value), context);
                }

                writer.EndObject();
            }
            finally
            {
                context.ExitDepth();
            }
        }

        public override T Read(ref VslReader reader, VslContext context)
        {
            if (reader.TryReadNull())
            {
                return default;
            }

            if (reader.TryReadTypeTag(out var tag))
            {
                var concrete = VslTypeRegistry.Resolve(tag, typeof(T));
                if (concrete == null)
                {
                    if (context.Options.Strict)
                    {
                        throw new VslException($"'!{tag.ToString()}' does not name a type assignable to {typeof(T).Name}.");
                    }
                }
                else if (concrete != typeof(T))
                {
                    return (T)VslFormatterRegistry.Get(concrete).ReadObject(ref reader, context);
                }
            }

            if (s_IsAbstract)
            {
                if (context.Options.Strict)
                {
                    throw new VslException($"{typeof(T).Name} is abstract, so the value needs a '!Type' tag.");
                }

                reader.SkipValue();
                return default;
            }

            var instance = VslReflection.CreateInstance(typeof(T));
            VslReflection.Populate(ref reader, instance, context);
            return (T)instance;
        }
    }

    /// <summary>Entry points for the reflection-driven path.</summary>
    public static class VslReflection
    {
        /// <summary>Builds a <see cref="ReflectionFormatter{T}"/> for a type. Wired in as the registry's fallback.</summary>
        public static IVslFormatter CreateFormatter(Type type)
        {
            try
            {
                return (IVslFormatter)Activator.CreateInstance(typeof(ReflectionFormatter<>).MakeGenericType(type));
            }
            catch (Exception ex)
            {
                throw new VslException($"Could not build a reflection formatter for {type}.", ex);
            }
        }

        public static object CreateInstance(Type type)
        {
            try
            {
                return Activator.CreateInstance(type, true);
            }
            catch (Exception ex)
            {
                throw new VslException(
                    $"{type} cannot be deserialized because it has no parameterless constructor.", ex);
            }
        }

        /// <summary>
        /// Reads a <c>{ ... }</c> body into an existing instance.
        /// </summary>
        /// <remarks>
        /// Boxed rather than typed on purpose: for a struct this is the boxed copy the caller unboxes
        /// afterwards, which is the only way reflection can assign its fields.
        /// </remarks>
        public static void Populate(ref VslReader reader, object target, VslContext context)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var schema = VslTypeSchema.Get(target.GetType());
            context.EnterDepth();
            try
            {
                reader.ReadObjectStart();
                while (reader.TryReadMemberName(out var name))
                {
                    var member = schema.Find(name);
                    if (member == null)
                    {
                        // An unknown member is skipped, not an error: partially-specified input is
                        // the normal case for machine-authored documents.
                        if (context.Options.Strict)
                        {
                            throw new VslException($"'{name.ToString()}' is not a serialized member of {schema.Type.Name}.");
                        }

                        reader.SkipValue();
                        continue;
                    }

                    member.SetValue(target, member.Formatter.ReadObject(ref reader, context));
                }
            }
            finally
            {
                context.ExitDepth();
            }
        }
    }
}
