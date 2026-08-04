using UnityEngine;

namespace Vapor.Serialization
{
    public sealed class Vector2Formatter : VslFormatter<Vector2>
    {
        public static readonly Vector2Formatter Instance = new Vector2Formatter();
        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in Vector2 value, VslContext context)
        {
            writer.BeginTuple();
            writer.WriteSingle(value.x);
            writer.WriteSingle(value.y);
            writer.EndTuple();
        }

        public override Vector2 Read(ref VslReader reader, VslContext context)
        {
            reader.ReadTupleStart();
            var x = reader.ReadSingleOr();
            var y = reader.ReadSingleOr();
            reader.ReadTupleEnd();
            return new Vector2(x, y);
        }
    }

    public sealed class Vector3Formatter : VslFormatter<Vector3>
    {
        public static readonly Vector3Formatter Instance = new Vector3Formatter();
        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in Vector3 value, VslContext context)
        {
            writer.BeginTuple();
            writer.WriteSingle(value.x);
            writer.WriteSingle(value.y);
            writer.WriteSingle(value.z);
            writer.EndTuple();
        }

        public override Vector3 Read(ref VslReader reader, VslContext context)
        {
            reader.ReadTupleStart();
            var x = reader.ReadSingleOr();
            var y = reader.ReadSingleOr();
            var z = reader.ReadSingleOr();
            reader.ReadTupleEnd();
            return new Vector3(x, y, z);
        }
    }

    public sealed class Vector4Formatter : VslFormatter<Vector4>
    {
        public static readonly Vector4Formatter Instance = new Vector4Formatter();
        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in Vector4 value, VslContext context)
        {
            writer.BeginTuple();
            writer.WriteSingle(value.x);
            writer.WriteSingle(value.y);
            writer.WriteSingle(value.z);
            writer.WriteSingle(value.w);
            writer.EndTuple();
        }

        public override Vector4 Read(ref VslReader reader, VslContext context)
        {
            reader.ReadTupleStart();
            var x = reader.ReadSingleOr();
            var y = reader.ReadSingleOr();
            var z = reader.ReadSingleOr();
            var w = reader.ReadSingleOr();
            reader.ReadTupleEnd();
            return new Vector4(x, y, z, w);
        }
    }

    public sealed class Vector2IntFormatter : VslFormatter<Vector2Int>
    {
        public static readonly Vector2IntFormatter Instance = new Vector2IntFormatter();
        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in Vector2Int value, VslContext context)
        {
            writer.BeginTuple();
            writer.WriteInt64(value.x);
            writer.WriteInt64(value.y);
            writer.EndTuple();
        }

        public override Vector2Int Read(ref VslReader reader, VslContext context)
        {
            reader.ReadTupleStart();
            var x = reader.ReadInt32Or();
            var y = reader.ReadInt32Or();
            reader.ReadTupleEnd();
            return new Vector2Int(x, y);
        }
    }

    public sealed class Vector3IntFormatter : VslFormatter<Vector3Int>
    {
        public static readonly Vector3IntFormatter Instance = new Vector3IntFormatter();
        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in Vector3Int value, VslContext context)
        {
            writer.BeginTuple();
            writer.WriteInt64(value.x);
            writer.WriteInt64(value.y);
            writer.WriteInt64(value.z);
            writer.EndTuple();
        }

        public override Vector3Int Read(ref VslReader reader, VslContext context)
        {
            reader.ReadTupleStart();
            var x = reader.ReadInt32Or();
            var y = reader.ReadInt32Or();
            var z = reader.ReadInt32Or();
            reader.ReadTupleEnd();
            return new Vector3Int(x, y, z);
        }
    }

    public sealed class QuaternionFormatter : VslFormatter<Quaternion>
    {
        public static readonly QuaternionFormatter Instance = new QuaternionFormatter();
        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in Quaternion value, VslContext context)
        {
            writer.BeginTuple();
            writer.WriteSingle(value.x);
            writer.WriteSingle(value.y);
            writer.WriteSingle(value.z);
            writer.WriteSingle(value.w);
            writer.EndTuple();
        }

        public override Quaternion Read(ref VslReader reader, VslContext context)
        {
            reader.ReadTupleStart();
            var x = reader.ReadSingleOr();
            var y = reader.ReadSingleOr();
            var z = reader.ReadSingleOr();
            var w = reader.ReadSingleOr(1f);
            reader.ReadTupleEnd();
            return new Quaternion(x, y, z, w);
        }
    }

    /// <summary>
    /// Sixteen floats in row-major order, written as one flat sequence.
    /// </summary>
    /// <remarks>
    /// Flat rather than nested rows: a matrix is rarely hand-edited, and a single row of numbers is
    /// both shorter and less error-prone to emit than four nested tuples.
    /// </remarks>
    public sealed class Matrix4x4Formatter : VslFormatter<Matrix4x4>
    {
        public static readonly Matrix4x4Formatter Instance = new Matrix4x4Formatter();

        public override void Write(ref VslWriter writer, in Matrix4x4 value, VslContext context)
        {
            // Copied out of the 'in' parameter once: the indexer is a property, so indexing the
            // parameter directly would copy all 64 bytes on every access.
            var m = value;

            writer.BeginSequence(inline: true);
            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 4; column++)
                {
                    writer.WriteSingle(m[row, column]);
                }
            }

            writer.EndSequence();
        }

        public override Matrix4x4 Read(ref VslReader reader, VslContext context)
        {
            var m = Matrix4x4.identity;
            reader.ReadSequenceStart();

            for (var i = 0; i < 16; i++)
            {
                if (!reader.TryReadSequenceItem())
                {
                    return m;
                }

                m[i / 4, i % 4] = reader.ReadSingle();
            }

            reader.ReadSequenceEnd();
            return m;
        }
    }
}
