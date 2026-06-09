using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Vapor.NetworkObjects
{
    public static class PacketRegistry
    {
        private static readonly Dictionary<uint, Type> s_IDToType = new();
        private static readonly Dictionary<Type, uint> s_TypeToId = new();
        private static uint s_NextId = 1;

        public static bool PacketsAreRegistered;
        public static event Action<List<Type>> PacketsRegistered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void RegisterAllPackets()
        {
            s_IDToType.Clear();
            s_TypeToId.Clear();
            s_NextId = 1;
            PacketsAreRegistered = false;

            var allTypesInAppDomain = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try
                    {
                        return a.GetTypes();
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        return ex.Types.Where(t => t != null)!;
                    }
                })
                .Where(t => t is { IsInterface: false, IsAbstract: false }) // Filter nulls, interfaces, abstracts early
                .ToList();

            foreach (var type in allTypesInAppDomain.Where(t => typeof(INetworkPacket).IsAssignableFrom(t) && !t.IsGenericType))
            {
                Register(type);
            }


            // var resolverTypes = allTypesInAppDomain
            //     .Where(t => t.IsValueType && 
            //                 !t.IsGenericType && 
            //                 t.GetInterfaces()
            //                     .Any(i => i.IsGenericType && 
            //                               i.GetGenericTypeDefinition() == typeof(IAttributeModifierResolver)))
            //     .ToList();

            // All Ummanged types.
            // All Types implementing INetworkPacket
            var unmanagedAndPacketTypes = allTypesInAppDomain
                .Where(t => typeof(INetworkPacket).IsAssignableFrom(t) && !t.IsGenericType)
                .ToList();
            unmanagedAndPacketTypes.Add(typeof(bool));
            unmanagedAndPacketTypes.Add(typeof(byte));
            unmanagedAndPacketTypes.Add(typeof(sbyte));
            unmanagedAndPacketTypes.Add(typeof(char));
            unmanagedAndPacketTypes.Add(typeof(decimal));
            unmanagedAndPacketTypes.Add(typeof(double));
            unmanagedAndPacketTypes.Add(typeof(float));
            unmanagedAndPacketTypes.Add(typeof(int));
            unmanagedAndPacketTypes.Add(typeof(uint));
            unmanagedAndPacketTypes.Add(typeof(long));
            unmanagedAndPacketTypes.Add(typeof(ulong));
            unmanagedAndPacketTypes.Add(typeof(short));
            unmanagedAndPacketTypes.Add(typeof(ushort));
            unmanagedAndPacketTypes.Add(typeof(string));
            unmanagedAndPacketTypes.Add(typeof(Vector2));
            unmanagedAndPacketTypes.Add(typeof(Vector3));
            unmanagedAndPacketTypes.Add(typeof(Vector4));
            unmanagedAndPacketTypes.Add(typeof(Quaternion));
            unmanagedAndPacketTypes.Add(typeof(Color));

            // Debug.Log($"Found {resolverTypes.Count} resolvers");
            // foreach (var resolverType in resolverTypes)
            // {
            //     // Check if the resolverType meets the constraints of the generic argument
            //     // For AttributeModifier<TResolver>, TResolver must be 'struct, IAttributeModifierResolver'
            //     // We already filtered for `IsValueType` and `IAttributeModifierResolver` above.
            //     // You might need more sophisticated constraint checking if your generics are complex.
            //     Debug.Log($"Registering AttributeModifier<> {resolverType.FullName}");
            //     try
            //     {
            //         var valueType = resolverType.GetInterfaces().First(rt => rt.GenericTypeArguments is { Length: > 0 }).GenericTypeArguments[0];
            //         Debug.Log($"Registering AttributeModifier<> {resolverType.FullName} | {valueType.FullName}");
            //         Type closedGenericType = typeof(AttributeModifier<>).MakeGenericType(valueType);
            //         Register(closedGenericType);
            //     }
            //     catch (ArgumentException ex)
            //     {
            //         // This can happen if resolverType doesn't meet hidden constraints
            //         // e.g., if there's a 'new()' constraint and the resolver doesn't have it
            //         throw new ArgumentException($"Failed to register AttributeModifier resolver {resolverType.FullName}: {ex.Message}", ex);
            //     }
            // }

            Debug.Log($"Found {unmanagedAndPacketTypes.Count} unmanaged and PacketTypes");
            foreach (var unmanaged in unmanagedAndPacketTypes)
            {
                try
                {
                    // Debug.Log($"Registering NetworkWrappedVariable<> {unmanaged.FullName}");
                    Type closedGenericType = typeof(NetworkWrappedVariable<>).MakeGenericType(unmanaged);
                    Register(closedGenericType);
                }
                catch (ArgumentException ex)
                {
                    // This can happen if resolverType doesn't meet hidden constraints
                    // e.g., if there's a 'new()' constraint and the resolver doesn't have it
                    throw new ArgumentException($"Failed to register NetworkWrappedVariable {unmanaged.FullName}: {ex.Message}", ex);
                }
            }

            PacketsRegistered?.Invoke(allTypesInAppDomain);
            PacketsAreRegistered = true;
        }

        public static void Register<T>() where T : INetworkPacket, new()
        {
            var type = typeof(T);
            if (s_TypeToId.ContainsKey(type)) return;

            uint id = s_NextId++;
            s_TypeToId[type] = id;
            s_IDToType[id] = type;
        }

        public static void Register(Type packetType)
        {
            if (s_TypeToId.ContainsKey(packetType)) return;

            uint id = s_NextId++;
            s_TypeToId[packetType] = id;
            s_IDToType[id] = packetType;
        }

        public static uint GetOpCode(Type t) => s_TypeToId[t];
        public static Type GetType(uint opCode) => s_IDToType[opCode];
    }
}