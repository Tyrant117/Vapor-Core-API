namespace Vapor.Rpc.SourceGenerator.Tests
{
    /// <summary>
    /// A stand-in for everything generated rpc code touches, with the same signatures and the same
    /// accessibility as the real thing.
    /// </summary>
    /// <remarks>
    /// The accessibility is the point. Generated code lives inside a subclass of VaporNetworkObject
    /// in someone else's assembly, so if the runtime tightened <c>BeginSendRpc</c> or
    /// <c>RpcSerialization</c> past what that reaches, every generated file would stop compiling —
    /// and nothing inside vapor.core.runtime would notice. Keep this in step with
    /// Runtime/Network Objects/VaporNetworkObject.cs and RpcSerialization.cs.
    /// </remarks>
    internal static class RuntimeStubs
    {
        public const string Source = @"
using System;

namespace UnityEngine
{
    public enum RuntimeInitializeLoadType
    {
        AfterSceneLoad = 0,
        BeforeSceneLoad = 1,
        AfterAssembliesLoaded = 2,
        BeforeSplashScreen = 3,
        SubsystemRegistration = 4,
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }
}

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class InitializeOnLoadMethodAttribute : Attribute { }
}

namespace Unity.Netcode
{
    public enum SendTo { Everyone, Server, Owner, NotServer, NotOwner, Me, NotMe, ClientsAndHost, Authority, NotAuthority, SpecifiedInParams }

    public enum NetworkDelivery { Unreliable, UnreliableSequenced, Reliable, ReliableSequenced, ReliableFragmentedSequenced }

    public struct FastBufferWriter : IDisposable { public void Dispose() { } }

    public struct FastBufferReader { }

    public interface INetworkSerializable { }

    public class NetworkBehaviour { }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method, AllowMultiple = true)]
    public class GenerateSerializationForTypeAttribute : Attribute
    {
        public GenerateSerializationForTypeAttribute(Type type) { }
    }
}

namespace Vapor.NetworkObjects
{
    using Unity.Netcode;

    public interface INetworkPacket { }

    [AttributeUsage(AttributeTargets.Method)]
    public class VaporRpcAttribute : Attribute
    {
        public VaporRpcAttribute(SendTo sendTo, NetworkDelivery networkDelivery = NetworkDelivery.ReliableSequenced) { }
    }

    public abstract class VaporNetworkObject : INetworkPacket
    {
        protected delegate void RpcReceiveHandler(VaporNetworkObject target, FastBufferReader reader);

        protected static void RegisterRpc(uint hash, RpcReceiveHandler handler, string name) { }

        protected bool BeginSendRpc(uint hash, out FastBufferWriter writer) { writer = default; return true; }

        protected bool EndSendRpc(FastBufferWriter writer, SendTo sendTo, NetworkDelivery networkDelivery) => true;
    }

    public static class RpcSerialization
    {
        public static void WriteValue<T>(FastBufferWriter writer, T value) { }
        public static T ReadValue<T>(FastBufferReader reader) => default;
        public static void WriteNetworkObject(FastBufferWriter writer, VaporNetworkObject value) { }
        public static VaporNetworkObject ReadNetworkObject(FastBufferReader reader) => null;
        public static void WriteNetworkBehaviour(FastBufferWriter writer, NetworkBehaviour value) { }
        public static NetworkBehaviour ReadNetworkBehaviour(FastBufferReader reader) => null;
        public static void WritePacket(FastBufferWriter writer, INetworkPacket value) { }
        public static INetworkPacket ReadPacket(FastBufferReader reader) => null;
        public static void WriteSerializable<T>(FastBufferWriter writer, T value) where T : INetworkSerializable, new() { }
        public static T ReadSerializable<T>(FastBufferReader reader) where T : INetworkSerializable, new() => default;
    }
}
";
    }
}
