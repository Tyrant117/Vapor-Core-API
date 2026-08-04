using UnityEngine;

namespace Vapor.Serialization
{
    public sealed class RectFormatter : VslFormatter<Rect>
    {
        public static readonly RectFormatter Instance = new RectFormatter();
        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in Rect value, VslContext context)
        {
            writer.BeginTuple();
            writer.WriteSingle(value.x);
            writer.WriteSingle(value.y);
            writer.WriteSingle(value.width);
            writer.WriteSingle(value.height);
            writer.EndTuple();
        }

        public override Rect Read(ref VslReader reader, VslContext context)
        {
            reader.ReadTupleStart();
            var x = reader.ReadSingleOr();
            var y = reader.ReadSingleOr();
            var width = reader.ReadSingleOr();
            var height = reader.ReadSingleOr();
            reader.ReadTupleEnd();
            return new Rect(x, y, width, height);
        }
    }

    public sealed class RectIntFormatter : VslFormatter<RectInt>
    {
        public static readonly RectIntFormatter Instance = new RectIntFormatter();
        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in RectInt value, VslContext context)
        {
            writer.BeginTuple();
            writer.WriteInt64(value.x);
            writer.WriteInt64(value.y);
            writer.WriteInt64(value.width);
            writer.WriteInt64(value.height);
            writer.EndTuple();
        }

        public override RectInt Read(ref VslReader reader, VslContext context)
        {
            reader.ReadTupleStart();
            var x = reader.ReadInt32Or();
            var y = reader.ReadInt32Or();
            var width = reader.ReadInt32Or();
            var height = reader.ReadInt32Or();
            reader.ReadTupleEnd();
            return new RectInt(x, y, width, height);
        }
    }

    /// <summary>Centre followed by extents — the struct's own storage, so nothing is derived.</summary>
    public sealed class BoundsFormatter : VslFormatter<Bounds>
    {
        public static readonly BoundsFormatter Instance = new BoundsFormatter();
        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in Bounds value, VslContext context)
        {
            var center = value.center;
            var extents = value.extents;

            writer.BeginTuple();
            writer.WriteSingle(center.x);
            writer.WriteSingle(center.y);
            writer.WriteSingle(center.z);
            writer.WriteSingle(extents.x);
            writer.WriteSingle(extents.y);
            writer.WriteSingle(extents.z);
            writer.EndTuple();
        }

        public override Bounds Read(ref VslReader reader, VslContext context)
        {
            reader.ReadTupleStart();
            var cx = reader.ReadSingleOr();
            var cy = reader.ReadSingleOr();
            var cz = reader.ReadSingleOr();
            var ex = reader.ReadSingleOr();
            var ey = reader.ReadSingleOr();
            var ez = reader.ReadSingleOr();
            reader.ReadTupleEnd();

            return new Bounds(new Vector3(cx, cy, cz), new Vector3(ex * 2f, ey * 2f, ez * 2f));
        }
    }

    /// <summary>Position followed by size, matching how <see cref="BoundsInt"/> actually stores itself.</summary>
    public sealed class BoundsIntFormatter : VslFormatter<BoundsInt>
    {
        public static readonly BoundsIntFormatter Instance = new BoundsIntFormatter();
        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in BoundsInt value, VslContext context)
        {
            var position = value.position;
            var size = value.size;

            writer.BeginTuple();
            writer.WriteInt64(position.x);
            writer.WriteInt64(position.y);
            writer.WriteInt64(position.z);
            writer.WriteInt64(size.x);
            writer.WriteInt64(size.y);
            writer.WriteInt64(size.z);
            writer.EndTuple();
        }

        public override BoundsInt Read(ref VslReader reader, VslContext context)
        {
            reader.ReadTupleStart();
            var px = reader.ReadInt32Or();
            var py = reader.ReadInt32Or();
            var pz = reader.ReadInt32Or();
            var sx = reader.ReadInt32Or();
            var sy = reader.ReadInt32Or();
            var sz = reader.ReadInt32Or();
            reader.ReadTupleEnd();

            return new BoundsInt(new Vector3Int(px, py, pz), new Vector3Int(sx, sy, sz));
        }
    }
}
