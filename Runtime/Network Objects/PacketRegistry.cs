using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Vapor.Unsafe;

namespace Vapor.NetworkObjects
{
    /// <summary>
    /// Maps packet types to the opcode that identifies them on the wire, and builds them again on
    /// arrival.
    /// </summary>
    /// <remarks>
    /// Opcodes are derived from the type's full name, not from the order types were discovered in.
    /// A counter would mean both peers had to walk their assemblies in exactly the same order to agree
    /// on what any packet was — and they do not: an editor host loads editor and test assemblies a
    /// built client never sees, and every packet type registered after one of those would shift.
    /// Nothing about that failure is visible; packets simply arrive as the wrong type.
    /// <para>
    /// Constructors are resolved once at registration. Building a packet by reflection on every
    /// arrival is pure overhead on the hottest path there is.
    /// </para>
    /// </remarks>
    public static class PacketRegistry
    {
        private static readonly Dictionary<uint, Type> s_IDToType = new();
        private static readonly Dictionary<Type, uint> s_TypeToId = new();
        private static readonly Dictionary<uint, Func<INetworkPacket>> s_Factories = new();

        public static bool PacketsAreRegistered;
        public static event Action<IReadOnlyList<Type>> PacketsRegistered;

        /// <summary>
        /// Runs in the editor too. Without it the registry is empty outside play mode, which makes
        /// every packet path untestable and silently breaks editor tooling that round-trips one.
        /// </summary>
#if UNITY_EDITOR
        [InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void RegisterAllPackets()
        {
            s_IDToType.Clear();
            s_TypeToId.Clear();
            s_Factories.Clear();
            PacketsAreRegistered = false;

            var allTypesInAppDomain = new List<Type>();
            var packetTypes = new List<Type>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }

                foreach (var type in types)
                {
                    if (type == null || type.IsInterface || type.IsAbstract)
                    {
                        continue;
                    }

                    allTypesInAppDomain.Add(type);

                    // Collected in the same pass that walks the domain. Filtering the whole list a
                    // second time for the same predicate doubled the startup cost for nothing.
                    if (!type.IsGenericType && typeof(INetworkPacket).IsAssignableFrom(type))
                    {
                        packetTypes.Add(type);
                    }
                }
            }

            foreach (var type in packetTypes)
            {
                Register(type);
            }

            foreach (var wrapped in s_WrappedVariableTypes)
            {
                Register(typeof(NetworkWrappedVariable<>).MakeGenericType(wrapped));
            }

            foreach (var type in packetTypes)
            {
                Register(typeof(NetworkWrappedVariable<>).MakeGenericType(type));
            }

            PacketsRegistered?.Invoke(allTypesInAppDomain);
            PacketsAreRegistered = true;
        }

        /// <summary>
        /// The value types <c>NetworkWrappedVariable&lt;T&gt;</c> is pre-registered for.
        /// </summary>
        /// <remarks>
        /// <c>string</c> and <c>decimal</c> are deliberately absent: both route to
        /// <c>NetworkVariableSerialization&lt;T&gt;</c>, which has no serializer for either, so
        /// registering them only moved the failure from lookup time to send time.
        /// </remarks>
        private static readonly Type[] s_WrappedVariableTypes =
        {
            typeof(bool), typeof(byte), typeof(sbyte), typeof(char),
            typeof(double), typeof(float),
            typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(short), typeof(ushort),
            typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Quaternion), typeof(Color),
        };

        public static void Register<T>() where T : INetworkPacket, new() => Register(typeof(T));

        public static void Register(Type packetType)
        {
            if (packetType == null || s_TypeToId.ContainsKey(packetType))
            {
                return;
            }

            if (!typeof(INetworkPacket).IsAssignableFrom(packetType))
            {
                Debug.LogError($"{packetType.FullName} cannot be registered as a packet: it does not implement {nameof(INetworkPacket)}.");
                return;
            }

            uint id = OpCodeFor(packetType);
            if (s_IDToType.TryGetValue(id, out var existing))
            {
                if (existing != packetType)
                {
                    Debug.LogError($"Packet opcode collision: {packetType.FullName} and {existing.FullName} both hash to [{id:X8}]. {packetType.FullName} will not be sendable; rename it.");
                }

                return;
            }

            s_TypeToId[packetType] = id;
            s_IDToType[id] = packetType;

            var factory = BuildFactory(packetType);
            if (factory != null)
            {
                s_Factories[id] = factory;
            }
        }

        /// <summary>
        /// xxHash32 of the type's full name — the same scheme rpc ids use, and stable across builds,
        /// platforms and assembly load order.
        /// </summary>
        public static uint OpCodeFor(Type packetType) => packetType.FullName.Hash32();

        public static bool TryGetOpCode(Type packetType, out uint opCode) => s_TypeToId.TryGetValue(packetType, out opCode);

        public static bool TryGetType(uint opCode, out Type packetType) => s_IDToType.TryGetValue(opCode, out packetType);

        /// <summary>
        /// Builds a packet from its opcode, through the constructor resolved at registration.
        /// </summary>
        /// <returns>False for an opcode this build does not know, which means the peers disagree.</returns>
        public static bool TryCreate(uint opCode, out INetworkPacket packet)
        {
            if (s_Factories.TryGetValue(opCode, out var factory))
            {
                packet = factory();
                return true;
            }

            packet = null;
            return false;
        }

        public static uint GetOpCode(Type t)
        {
            if (s_TypeToId.TryGetValue(t, out var opCode))
            {
                return opCode;
            }

            Debug.LogError($"{t.FullName} is not a registered packet type. It must be a non-generic, non-abstract {nameof(INetworkPacket)}.");
            return 0;
        }

        public static Type GetType(uint opCode) => s_IDToType.TryGetValue(opCode, out var type) ? type : null;

        private static Func<INetworkPacket> BuildFactory(Type type)
        {
            if (type.IsValueType)
            {
                return () => (INetworkPacket)Activator.CreateInstance(type);
            }

            // Resolved once. Activator.CreateInstance(type, nonPublic: true) repeats this lookup and
            // its binder work on every single received packet.
            var constructor = type.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);

            if (constructor == null)
            {
                Debug.LogWarning($"{type.FullName} is a packet with no parameterless constructor, so it can be sent but never received.");
                return null;
            }

            return () => (INetworkPacket)constructor.Invoke(null);
        }
    }
}
