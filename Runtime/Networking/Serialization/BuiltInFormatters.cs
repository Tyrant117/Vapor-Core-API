using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace Vapor.Networking
{
    /// <summary>
    /// Formatters for the primitives, the common System types, Unity's math types, enums and the
    /// generic collections. Registered by <see cref="NetworkFormatters"/>' type initializer.
    /// </summary>
    [NoAutoStaticsCleanup]
    internal static class BuiltInFormatters
    {
        private static bool s_Registered;

        public static void RegisterAll()
        {
            if (s_Registered)
            {
                return;
            }

            s_Registered = true;

            // Primitives
            NetworkFormatters.Register<byte>((NetworkWriter w, in byte v) => w.WriteByte(v), r => r.ReadByte());
            NetworkFormatters.Register<sbyte>((NetworkWriter w, in sbyte v) => w.WriteSByte(v), r => r.ReadSByte());
            NetworkFormatters.Register<bool>((NetworkWriter w, in bool v) => w.WriteBool(v), r => r.ReadBool());
            NetworkFormatters.Register<short>((NetworkWriter w, in short v) => w.WriteInt16(v), r => r.ReadInt16());
            NetworkFormatters.Register<ushort>((NetworkWriter w, in ushort v) => w.WriteUInt16(v), r => r.ReadUInt16());
            NetworkFormatters.Register<int>((NetworkWriter w, in int v) => w.WriteInt32(v), r => r.ReadInt32());
            NetworkFormatters.Register<uint>((NetworkWriter w, in uint v) => w.WriteUInt32(v), r => r.ReadUInt32());
            NetworkFormatters.Register<long>((NetworkWriter w, in long v) => w.WriteInt64(v), r => r.ReadInt64());
            NetworkFormatters.Register<ulong>((NetworkWriter w, in ulong v) => w.WriteUInt64(v), r => r.ReadUInt64());
            NetworkFormatters.Register<float>((NetworkWriter w, in float v) => w.WriteSingle(v), r => r.ReadSingle());
            NetworkFormatters.Register<double>((NetworkWriter w, in double v) => w.WriteDouble(v), r => r.ReadDouble());
            NetworkFormatters.Register<char>((NetworkWriter w, in char v) => w.WriteChar(v), r => r.ReadChar());
            NetworkFormatters.Register<string>((NetworkWriter w, in string v) => w.WriteString(v), r => r.ReadString());
            NetworkFormatters.Register<decimal>(WriteDecimal, ReadDecimal);

            // System
            NetworkFormatters.Register<Guid>(WriteGuid, ReadGuid);
            NetworkFormatters.Register<DateTime>(WriteDateTime, ReadDateTime);
            NetworkFormatters.Register<TimeSpan>((NetworkWriter w, in TimeSpan v) => w.WriteInt64(v.Ticks), r => new TimeSpan(r.ReadInt64()));
            NetworkFormatters.Register<byte[]>((NetworkWriter w, in byte[] v) => WriteByteArray(w, v), ReadByteArray);

            // Unity
            NetworkFormatters.Register<Vector2>((NetworkWriter w, in Vector2 v) => w.WriteVector2(v), r => r.ReadVector2());
            NetworkFormatters.Register<Vector3>((NetworkWriter w, in Vector3 v) => w.WriteVector3(v), r => r.ReadVector3());
            NetworkFormatters.Register<Vector4>((NetworkWriter w, in Vector4 v) => w.WriteVector4(v), r => r.ReadVector4());
            NetworkFormatters.Register<Quaternion>((NetworkWriter w, in Quaternion v) => w.WriteQuaternion(v), r => r.ReadQuaternion());
            NetworkFormatters.Register<Vector2Int>(WriteVector2Int, ReadVector2Int);
            NetworkFormatters.Register<Vector3Int>(WriteVector3Int, ReadVector3Int);
            NetworkFormatters.Register<Color>(WriteColor, ReadColor);
            NetworkFormatters.Register<Color32>(WriteColor32, ReadColor32);
            NetworkFormatters.Register<Rect>(WriteRect, ReadRect);
            NetworkFormatters.Register<Bounds>(WriteBounds, ReadBounds);
            NetworkFormatters.Register<Pose>(WritePose, ReadPose);
            NetworkFormatters.Register<Matrix4x4>(WriteMatrix, ReadMatrix);

            // Generic collections
            NetworkFormatters.RegisterGenericFactory(typeof(List<>), t => NetworkFormatters.Instantiate(typeof(ListFormatter<>), t.GetGenericArguments()));
            NetworkFormatters.RegisterGenericFactory(typeof(HashSet<>), t => NetworkFormatters.Instantiate(typeof(HashSetFormatter<>), t.GetGenericArguments()));
            NetworkFormatters.RegisterGenericFactory(typeof(Dictionary<,>), t => NetworkFormatters.Instantiate(typeof(DictionaryFormatter<,>), t.GetGenericArguments()));
            NetworkFormatters.RegisterGenericFactory(typeof(Nullable<>), t => NetworkFormatters.Instantiate(typeof(NullableFormatter<>), t.GetGenericArguments()));
            NetworkFormatters.RegisterGenericFactory(typeof(KeyValuePair<,>), t => NetworkFormatters.Instantiate(typeof(KeyValuePairFormatter<,>), t.GetGenericArguments()));

            // Anything that writes itself.
            NetworkFormatters.AddFallback(PayloadFormatter<INetworkPayload>.Resolve);

            // Object references, so a formatter that carries one can resolve before any world exists.
            NetworkFormatters.Register(new VaporNetworkObjectReference.Formatter());
        }

        #region - System -

        private static void WriteDecimal(NetworkWriter w, in decimal v)
        {
            var bits = decimal.GetBits(v);
            w.WriteInt32(bits[0]);
            w.WriteInt32(bits[1]);
            w.WriteInt32(bits[2]);
            w.WriteInt32(bits[3]);
        }

        private static decimal ReadDecimal(NetworkReader r) => new(new[] { r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32() });

        private static void WriteGuid(NetworkWriter w, in Guid v)
        {
            Span<byte> bytes = stackalloc byte[16];
            v.TryWriteBytes(bytes);
            w.WriteBytes(bytes);
        }

        private static Guid ReadGuid(NetworkReader r) => new(r.ReadBytes(16));

        private static void WriteDateTime(NetworkWriter w, in DateTime v)
        {
            w.WriteInt64(v.Ticks);
            w.WriteByte((byte)v.Kind);
        }

        private static DateTime ReadDateTime(NetworkReader r)
        {
            long ticks = r.ReadInt64();
            var kind = (DateTimeKind)r.ReadByte();
            return new DateTime(ticks, kind);
        }

        private static void WriteByteArray(NetworkWriter w, byte[] v)
        {
            if (v == null)
            {
                w.WriteVarUInt32(0);
                return;
            }

            w.WriteVarUInt32((uint)v.Length + 1u);
            w.WriteBytes(v);
        }

        private static byte[] ReadByteArray(NetworkReader r)
        {
            uint prefix = r.ReadVarUInt32();
            if (prefix == 0)
            {
                return null;
            }

            return r.ReadBytes(checked((int)(prefix - 1))).ToArray();
        }

        #endregion

        #region - Unity -

        private static void WriteVector2Int(NetworkWriter w, in Vector2Int v)
        {
            w.WriteVarInt32(v.x);
            w.WriteVarInt32(v.y);
        }

        private static Vector2Int ReadVector2Int(NetworkReader r) => new(r.ReadVarInt32(), r.ReadVarInt32());

        private static void WriteVector3Int(NetworkWriter w, in Vector3Int v)
        {
            w.WriteVarInt32(v.x);
            w.WriteVarInt32(v.y);
            w.WriteVarInt32(v.z);
        }

        private static Vector3Int ReadVector3Int(NetworkReader r) => new(r.ReadVarInt32(), r.ReadVarInt32(), r.ReadVarInt32());

        private static void WriteColor(NetworkWriter w, in Color v)
        {
            w.WriteSingle(v.r);
            w.WriteSingle(v.g);
            w.WriteSingle(v.b);
            w.WriteSingle(v.a);
        }

        private static Color ReadColor(NetworkReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        private static void WriteColor32(NetworkWriter w, in Color32 v)
        {
            w.WriteByte(v.r);
            w.WriteByte(v.g);
            w.WriteByte(v.b);
            w.WriteByte(v.a);
        }

        private static Color32 ReadColor32(NetworkReader r) => new(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());

        private static void WriteRect(NetworkWriter w, in Rect v)
        {
            w.WriteSingle(v.x);
            w.WriteSingle(v.y);
            w.WriteSingle(v.width);
            w.WriteSingle(v.height);
        }

        private static Rect ReadRect(NetworkReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        private static void WriteBounds(NetworkWriter w, in Bounds v)
        {
            w.WriteVector3(v.center);
            w.WriteVector3(v.size);
        }

        private static Bounds ReadBounds(NetworkReader r) => new(r.ReadVector3(), r.ReadVector3());

        private static void WritePose(NetworkWriter w, in Pose v)
        {
            w.WriteVector3(v.position);
            w.WriteQuaternion(v.rotation);
        }

        private static Pose ReadPose(NetworkReader r) => new(r.ReadVector3(), r.ReadQuaternion());

        private static void WriteMatrix(NetworkWriter w, in Matrix4x4 v)
        {
            for (int i = 0; i < 16; i++)
            {
                w.WriteSingle(v[i]);
            }
        }

        private static Matrix4x4 ReadMatrix(NetworkReader r)
        {
            var m = new Matrix4x4();
            for (int i = 0; i < 16; i++)
            {
                m[i] = r.ReadSingle();
            }

            return m;
        }

        #endregion
    }

    #region - Generic formatters -

    /// <summary>
    /// Enums go over the wire as their numeric value in a zig-zag varint, so small values cost a byte
    /// regardless of the declared underlying type. Conversion is a reinterpret, not a box.
    /// </summary>
    [NoAutoStaticsCleanup]
    internal sealed class EnumFormatter<T> : NetworkFormatter<T> where T : unmanaged, Enum
    {
        // Reinterpreting through exactly the underlying width keeps sign extension right for signed
        // underlying types and never reads past a narrow enum's storage.
        private static readonly Type s_Underlying = Enum.GetUnderlyingType(typeof(T));
        private static readonly int s_Size = NetworkWriter.BlittableSize<T>.Value;
        private static readonly bool s_Signed = s_Underlying == typeof(sbyte) || s_Underlying == typeof(short) || s_Underlying == typeof(int) || s_Underlying == typeof(long);

        public override void Write(NetworkWriter writer, in T value)
        {
            T copy = value;
            long numeric = s_Size switch
            {
                1 => s_Signed ? UnsafeUtility.As<T, sbyte>(ref copy) : UnsafeUtility.As<T, byte>(ref copy),
                2 => s_Signed ? UnsafeUtility.As<T, short>(ref copy) : UnsafeUtility.As<T, ushort>(ref copy),
                4 => s_Signed ? UnsafeUtility.As<T, int>(ref copy) : UnsafeUtility.As<T, uint>(ref copy),
                _ => UnsafeUtility.As<T, long>(ref copy),
            };
            writer.WriteVarInt64(numeric);
        }

        public override T Read(NetworkReader reader)
        {
            long numeric = reader.ReadVarInt64();
            switch (s_Size)
            {
                case 1:
                {
                    byte b = unchecked((byte)numeric);
                    return UnsafeUtility.As<byte, T>(ref b);
                }
                case 2:
                {
                    ushort s = unchecked((ushort)numeric);
                    return UnsafeUtility.As<ushort, T>(ref s);
                }
                case 4:
                {
                    int i = unchecked((int)numeric);
                    return UnsafeUtility.As<int, T>(ref i);
                }
                default:
                    return UnsafeUtility.As<long, T>(ref numeric);
            }
        }
    }

    /// <summary>Null is a count of 0; a non-null array is <c>count + 1</c> then the elements.</summary>
    internal sealed class ArrayFormatter<T> : NetworkFormatter<T[]>
    {
        public override void Write(NetworkWriter writer, in T[] value)
        {
            if (value == null)
            {
                writer.WriteVarUInt32(0);
                return;
            }

            var element = NetworkFormatters.Get<T>();
            writer.WriteVarUInt32((uint)value.Length + 1u);
            for (int i = 0; i < value.Length; i++)
            {
                element.Write(writer, value[i]);
            }
        }

        public override T[] Read(NetworkReader reader)
        {
            uint prefix = reader.ReadVarUInt32();
            if (prefix == 0)
            {
                return null;
            }

            int count = checked((int)(prefix - 1));
            var element = NetworkFormatters.Get<T>();
            var result = new T[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = element.Read(reader);
            }

            return result;
        }
    }

    internal sealed class ListFormatter<T> : NetworkFormatter<List<T>>
    {
        public override void Write(NetworkWriter writer, in List<T> value)
        {
            if (value == null)
            {
                writer.WriteVarUInt32(0);
                return;
            }

            var element = NetworkFormatters.Get<T>();
            writer.WriteVarUInt32((uint)value.Count + 1u);
            for (int i = 0; i < value.Count; i++)
            {
                element.Write(writer, value[i]);
            }
        }

        public override List<T> Read(NetworkReader reader)
        {
            uint prefix = reader.ReadVarUInt32();
            if (prefix == 0)
            {
                return null;
            }

            int count = checked((int)(prefix - 1));
            var element = NetworkFormatters.Get<T>();
            var result = new List<T>(count);
            for (int i = 0; i < count; i++)
            {
                result.Add(element.Read(reader));
            }

            return result;
        }
    }

    internal sealed class HashSetFormatter<T> : NetworkFormatter<HashSet<T>>
    {
        public override void Write(NetworkWriter writer, in HashSet<T> value)
        {
            if (value == null)
            {
                writer.WriteVarUInt32(0);
                return;
            }

            var element = NetworkFormatters.Get<T>();
            writer.WriteVarUInt32((uint)value.Count + 1u);
            foreach (var item in value)
            {
                element.Write(writer, item);
            }
        }

        public override HashSet<T> Read(NetworkReader reader)
        {
            uint prefix = reader.ReadVarUInt32();
            if (prefix == 0)
            {
                return null;
            }

            int count = checked((int)(prefix - 1));
            var element = NetworkFormatters.Get<T>();
            var result = new HashSet<T>();
            for (int i = 0; i < count; i++)
            {
                result.Add(element.Read(reader));
            }

            return result;
        }
    }

    internal sealed class DictionaryFormatter<TKey, TValue> : NetworkFormatter<Dictionary<TKey, TValue>>
    {
        public override void Write(NetworkWriter writer, in Dictionary<TKey, TValue> value)
        {
            if (value == null)
            {
                writer.WriteVarUInt32(0);
                return;
            }

            var keys = NetworkFormatters.Get<TKey>();
            var values = NetworkFormatters.Get<TValue>();
            writer.WriteVarUInt32((uint)value.Count + 1u);
            foreach (var pair in value)
            {
                keys.Write(writer, pair.Key);
                values.Write(writer, pair.Value);
            }
        }

        public override Dictionary<TKey, TValue> Read(NetworkReader reader)
        {
            uint prefix = reader.ReadVarUInt32();
            if (prefix == 0)
            {
                return null;
            }

            int count = checked((int)(prefix - 1));
            var keys = NetworkFormatters.Get<TKey>();
            var values = NetworkFormatters.Get<TValue>();
            var result = new Dictionary<TKey, TValue>(count);
            for (int i = 0; i < count; i++)
            {
                var key = keys.Read(reader);
                var value = values.Read(reader);
                result[key] = value;
            }

            return result;
        }
    }

    internal sealed class NullableFormatter<T> : NetworkFormatter<T?> where T : struct
    {
        public override void Write(NetworkWriter writer, in T? value)
        {
            writer.WriteBool(value.HasValue);
            if (value.HasValue)
            {
                NetworkFormatters.Get<T>().Write(writer, value.Value);
            }
        }

        public override T? Read(NetworkReader reader)
        {
            return reader.ReadBool() ? NetworkFormatters.Get<T>().Read(reader) : null;
        }
    }

    internal sealed class KeyValuePairFormatter<TKey, TValue> : NetworkFormatter<KeyValuePair<TKey, TValue>>
    {
        public override void Write(NetworkWriter writer, in KeyValuePair<TKey, TValue> value)
        {
            NetworkFormatters.Get<TKey>().Write(writer, value.Key);
            NetworkFormatters.Get<TValue>().Write(writer, value.Value);
        }

        public override KeyValuePair<TKey, TValue> Read(NetworkReader reader)
        {
            var key = NetworkFormatters.Get<TKey>().Read(reader);
            var value = NetworkFormatters.Get<TValue>().Read(reader);
            return new KeyValuePair<TKey, TValue>(key, value);
        }
    }

    #endregion
}
