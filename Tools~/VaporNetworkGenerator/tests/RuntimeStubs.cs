namespace Vapor.Network.SourceGenerator.Tests
{
    /// <summary>
    /// A stand-in for everything generated code touches, with the same signatures and the same
    /// accessibility as the real thing. Keep in step with Runtime/Networking.
    /// </summary>
    internal static class RuntimeStubs
    {
        public const string Source = @"
using System;

namespace UnityEngine
{
    public enum RuntimeInitializeLoadType { AfterSceneLoad = 0, BeforeSceneLoad = 1, AfterAssembliesLoaded = 2, BeforeSplashScreen = 3, SubsystemRegistration = 4 }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute { }

    public struct Vector3 { public float x, y, z; }
}

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class InitializeOnLoadMethodAttribute : Attribute { }
}

namespace Vapor.Serialization
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)] public sealed class VslSerializableAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)] public sealed class VslSerializeAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)] public sealed class VslIgnoreAttribute : Attribute { }
}

namespace Vapor.Networking
{
    public enum RpcTarget : byte { Server, Owner, NotOwner, NotServer, Everyone, Me, NotMe }
    public enum Delivery : byte { Unreliable, UnreliableSequenced, ReliableSequenced, ReliableFragmentedSequenced }

    public sealed class NetworkWriter { public void WriteBool(bool v) { } }
    public sealed class NetworkReader { public bool ReadBool() => true; }

    public interface IRpcHost { VaporNetworkObject RpcObject { get; } ushort RpcComponentId { get; } }
    public delegate void RpcReceiveHandler(IRpcHost target, NetworkReader reader);
    public static class RpcRegistry { public static void Register(uint hash, RpcReceiveHandler handler, string name) { } }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class VaporRpcAttribute : Attribute
    {
        public VaporRpcAttribute(RpcTarget target, Delivery delivery = Delivery.ReliableSequenced) { }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)] public sealed class NetworkSerializableAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)] public sealed class NetworkSerializeAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)] public sealed class NetworkIgnoreAttribute : Attribute { }

    public interface INetworkFormatter<T> { void Write(NetworkWriter writer, in T value); T Read(NetworkReader reader); }
    public abstract class NetworkFormatter<T> : INetworkFormatter<T>
    {
        public abstract void Write(NetworkWriter writer, in T value);
        public abstract T Read(NetworkReader reader);
    }

    public static class NetworkFormatters
    {
        public static void Register<T>(INetworkFormatter<T> formatter) { }
        public static void Write<T>(NetworkWriter writer, in T value) { }
        public static T Read<T>(NetworkReader reader) => default;
    }

    public abstract class VaporNetworkObject : IRpcHost
    {
        public VaporNetworkObject RpcObject => this;
        public ushort RpcComponentId => 0;
        public ulong NetworkObjectId => 0;
        public bool IsSpawned => true;
        protected bool BeginSendRpc(uint hash, out NetworkWriter writer) { writer = null; return true; }
        protected bool EndSendRpc(NetworkWriter writer, RpcTarget target, Delivery delivery) => true;
    }

    public abstract class NetworkComponent : IRpcHost
    {
        public VaporNetworkObject Owner => null;
        public ushort ComponentId => 0;
        public VaporNetworkObject RpcObject => null;
        public ushort RpcComponentId => 0;
        protected bool BeginSendRpc(uint hash, out NetworkWriter writer) { writer = null; return true; }
        protected bool EndSendRpc(NetworkWriter writer, RpcTarget target, Delivery delivery) => true;
    }

    public static class RpcArguments
    {
        public static void WriteObject(NetworkWriter writer, VaporNetworkObject value) { }
        public static VaporNetworkObject ReadObject(IRpcHost host, NetworkReader reader) => null;
        public static void WriteComponent(NetworkWriter writer, NetworkComponent value) { }
        public static NetworkComponent ReadComponent(IRpcHost host, NetworkReader reader) => null;
    }
}
";
    }
}
