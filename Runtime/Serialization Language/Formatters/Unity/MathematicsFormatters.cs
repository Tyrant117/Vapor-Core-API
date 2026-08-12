using Unity.Mathematics;
using Unity.Scripting.LifecycleManagement;

namespace Vapor.Serialization
{
    // Unity.Mathematics vectors, written exactly like their UnityEngine counterparts next door. They
    // are a separate file because they are a separate package: UnityMathFormatters covers the engine's
    // types, and these cover Burst's.
    //
    // Worth having in the Core API rather than wherever they were first needed. Any code written for
    // Burst — jobs, procedural generation, VFX layers — holds its vectors as float3 rather than
    // Vector3, so a serializer without these silently falls through to the reflection formatter and
    // writes a nested object with three members. That round-trips correctly and reads terribly, which
    // is the failure mode most likely to go unnoticed in a format whose whole purpose is being read.

    /// <summary>Writes a <see cref="float2"/> as a tuple, matching <see cref="Vector2Formatter"/>.</summary>
    [NoAutoStaticsCleanup]
    public sealed class Float2Formatter : VslFormatter<float2>
    {
        public static readonly Float2Formatter Instance = new();

        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in float2 value, VslContext context)
        {
            writer.BeginTuple();
            writer.WriteSingle(value.x);
            writer.WriteSingle(value.y);
            writer.EndTuple();
        }

        public override float2 Read(ref VslReader reader, VslContext context)
        {
            reader.ReadTupleStart();
            var x = reader.ReadSingleOr();
            var y = reader.ReadSingleOr();
            reader.ReadTupleEnd();

            return new float2(x, y);
        }
    }

    /// <summary>Writes a <see cref="float3"/> as a tuple.</summary>
    [NoAutoStaticsCleanup]
    public sealed class Float3Formatter : VslFormatter<float3>
    {
        public static readonly Float3Formatter Instance = new();

        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in float3 value, VslContext context)
        {
            writer.BeginTuple();
            writer.WriteSingle(value.x);
            writer.WriteSingle(value.y);
            writer.WriteSingle(value.z);
            writer.EndTuple();
        }

        public override float3 Read(ref VslReader reader, VslContext context)
        {
            reader.ReadTupleStart();
            var x = reader.ReadSingleOr();
            var y = reader.ReadSingleOr();
            var z = reader.ReadSingleOr();
            reader.ReadTupleEnd();

            return new float3(x, y, z);
        }
    }

    /// <summary>
    /// Writes a <see cref="float4"/> as a tuple.
    /// </summary>
    /// <remarks>
    /// Also the shape an HDR colour takes in Burst code, where <see cref="UnityEngine.Color"/> cannot
    /// go. Written as four plain numbers rather than in any colour notation, because nothing about the
    /// type says which it is.
    /// </remarks>
    [NoAutoStaticsCleanup]
    public sealed class Float4Formatter : VslFormatter<float4>
    {
        public static readonly Float4Formatter Instance = new();

        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in float4 value, VslContext context)
        {
            writer.BeginTuple();
            writer.WriteSingle(value.x);
            writer.WriteSingle(value.y);
            writer.WriteSingle(value.z);
            writer.WriteSingle(value.w);
            writer.EndTuple();
        }

        public override float4 Read(ref VslReader reader, VslContext context)
        {
            reader.ReadTupleStart();
            var x = reader.ReadSingleOr();
            var y = reader.ReadSingleOr();
            var z = reader.ReadSingleOr();
            var w = reader.ReadSingleOr();
            reader.ReadTupleEnd();

            return new float4(x, y, z, w);
        }
    }

    /// <summary>Writes an <see cref="int2"/> as a tuple, matching <see cref="Vector2IntFormatter"/>.</summary>
    [NoAutoStaticsCleanup]
    public sealed class Int2Formatter : VslFormatter<int2>
    {
        public static readonly Int2Formatter Instance = new();

        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in int2 value, VslContext context)
        {
            writer.BeginTuple();
            writer.WriteInt64(value.x);
            writer.WriteInt64(value.y);
            writer.EndTuple();
        }

        public override int2 Read(ref VslReader reader, VslContext context)
        {
            reader.ReadTupleStart();
            var x = reader.ReadInt32Or();
            var y = reader.ReadInt32Or();
            reader.ReadTupleEnd();

            return new int2(x, y);
        }
    }

    /// <summary>Writes an <see cref="int3"/> as a tuple.</summary>
    [NoAutoStaticsCleanup]
    public sealed class Int3Formatter : VslFormatter<int3>
    {
        public static readonly Int3Formatter Instance = new();

        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in int3 value, VslContext context)
        {
            writer.BeginTuple();
            writer.WriteInt64(value.x);
            writer.WriteInt64(value.y);
            writer.WriteInt64(value.z);
            writer.EndTuple();
        }

        public override int3 Read(ref VslReader reader, VslContext context)
        {
            reader.ReadTupleStart();
            var x = reader.ReadInt32Or();
            var y = reader.ReadInt32Or();
            var z = reader.ReadInt32Or();
            reader.ReadTupleEnd();

            return new int3(x, y, z);
        }
    }

    /// <summary>
    /// Writes a <see cref="quaternion"/> as a tuple of its four components.
    /// </summary>
    /// <remarks>
    /// Components rather than Euler angles, matching <see cref="QuaternionFormatter"/>. Euler is more
    /// readable and is not a faithful representation — three angles do not uniquely name a rotation,
    /// so a round trip through them can return a different one than went in.
    /// </remarks>
    [NoAutoStaticsCleanup]
    public sealed class QuaternionMathFormatter : VslFormatter<quaternion>
    {
        public static readonly QuaternionMathFormatter Instance = new();

        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in quaternion value, VslContext context)
        {
            writer.BeginTuple();
            writer.WriteSingle(value.value.x);
            writer.WriteSingle(value.value.y);
            writer.WriteSingle(value.value.z);
            writer.WriteSingle(value.value.w);
            writer.EndTuple();
        }

        public override quaternion Read(ref VslReader reader, VslContext context)
        {
            reader.ReadTupleStart();
            var x = reader.ReadSingleOr();
            var y = reader.ReadSingleOr();
            var z = reader.ReadSingleOr();

            // Defaults to one, matching QuaternionFormatter. A missing w would otherwise read as zero,
            // and the all-zero quaternion is not a rotation at all.
            var w = reader.ReadSingleOr(1f);

            reader.ReadTupleEnd();

            return new quaternion(x, y, z, w);
        }
    }
}
