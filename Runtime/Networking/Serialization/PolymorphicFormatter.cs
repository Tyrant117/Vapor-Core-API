using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Assemblies;

namespace Vapor.Networking
{
    /// <summary>
    /// A formatter for a base type — an interface or abstract class — that writes the concrete type's
    /// tag ahead of the value, so the receiver reads back exactly what was sent. Null travels as tag 0.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The concrete type's own formatter does the work; this only prefixes the tag. Any type that is
    /// assignable to <typeparamref name="TBase"/> and has (or can resolve) a formatter is accepted, so
    /// registering one of these for <c>IGameplayEventData</c> is what lets an rpc take the interface
    /// and be handed a <c>PoseEventData</c>.
    /// </para>
    /// <para>
    /// The receiver must be able to map the tag to the type. Concrete implementers are registered
    /// with <see cref="NetworkTypeRegistry"/> the first time a tag is not recognised, by scanning the
    /// loaded assemblies for <typeparamref name="TBase"/> implementations, so hand registration is
    /// only needed for types that arrive after that (a late-loaded assembly).
    /// </para>
    /// </remarks>
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class PolymorphicFormatter<TBase> : NetworkFormatter<TBase>
    {
        private static bool s_Scanned;

        public override void Write(NetworkWriter writer, in TBase value)
        {
            if (value == null)
            {
                writer.WriteVarUInt32(0);
                return;
            }

            var type = value.GetType();
            var formatter = NetworkFormatters.Get(type);
            writer.WriteVarUInt32(NetworkTypeRegistry.TagOf(type));
            formatter.WriteBoxed(writer, value);
        }

        public override TBase Read(NetworkReader reader)
        {
            uint tag = reader.ReadVarUInt32();
            if (tag == 0)
            {
                return default;
            }

            if (!NetworkTypeRegistry.TryGetType(tag, out var type))
            {
                RegisterImplementations();
                if (!NetworkTypeRegistry.TryGetType(tag, out type))
                {
                    throw new NetworkSerializationException($"Unknown {typeof(TBase).Name} type tag [{tag:X8}]. Register the type with {nameof(NetworkTypeRegistry)} on this peer.");
                }
            }

            if (!typeof(TBase).IsAssignableFrom(type))
            {
                throw new NetworkSerializationException($"Type tag [{tag:X8}] is {type.FullName}, which is not a {typeof(TBase).Name}.");
            }

            return (TBase)NetworkFormatters.Get(type).ReadBoxed(reader);
        }

        /// <summary>Registers every concrete implementation of <typeparamref name="TBase"/> in the loaded assemblies. Once.</summary>
        public static void RegisterImplementations()
        {
            if (s_Scanned)
            {
                return;
            }

            s_Scanned = true;
            var baseType = typeof(TBase);

            // Unity's loaded set rather than the app domain's: the domain can still hand back assemblies
            // Unity has unloaded, and reflecting over one of those throws or leaks.
            foreach (var assembly in CurrentAssemblies.GetLoadedAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }

                foreach (var type in types)
                {
                    if (type == null || type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition || !baseType.IsAssignableFrom(type))
                    {
                        continue;
                    }

                    NetworkTypeRegistry.Register(type);
                }
            }
        }
    }

    /// <summary>Convenience registrations for base types.</summary>
    public static class PolymorphicFormatters
    {
        /// <summary>Registers a <see cref="PolymorphicFormatter{TBase}"/> for <typeparamref name="TBase"/> and scans its implementations.</summary>
        public static void Register<TBase>()
        {
            NetworkFormatters.Register(new PolymorphicFormatter<TBase>());
            PolymorphicFormatter<TBase>.RegisterImplementations();
        }
    }
}
