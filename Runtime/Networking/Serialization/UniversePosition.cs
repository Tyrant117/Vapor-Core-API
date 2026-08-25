using System;
using UnityEngine;

namespace Vapor.Networking
{
    /// <summary>
    /// A position anywhere, at constant precision: which kilometre, and where inside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A <see cref="float"/> has twenty-four bits of mantissa.</b> At eighty kilometres its step is
    /// 1.2 cm, which is visible jitter in physics and rendering — and PhysX degrades well before it
    /// becomes visible to a person. Nothing about a hundred-kilometre play space is wrong; the number
    /// format is.
    /// </para>
    /// <para>
    /// A <c>double</c> triple would fix it too, and is strictly worse. It fixes precision by having more
    /// bits, so the degradation is still there and merely pushed out; it costs twenty-four bytes on the
    /// wire before quantization; and every quantizer, buffer and interpolator in the transform stack
    /// speaks <see cref="Vector3"/>. Splitting the number instead makes precision <b>independent of
    /// distance</b> — six hundredths of a millimetre inside any sector, at eighty kilometres or eighty
    /// thousand — and the sector doubles as the interest cell at no cost.
    /// </para>
    /// <para>
    /// The failure mode is also better. Get this wrong and something is a whole sector out, which is a
    /// kilometre and is obvious. Get a float world wrong and centimetres accumulate, which looks like a
    /// physics bug for a week.
    /// </para>
    /// <para>
    /// <b>The invariant is normalization:</b> after any operation, <see cref="Local"/> is inside
    /// <c>[0, SectorSize)</c> on every axis. Two positions are therefore equal exactly when both halves
    /// are, so equality is exact rather than a tolerance nobody agrees on.
    /// </para>
    /// </remarks>
    [Serializable]
    public readonly struct UniversePosition : IEquatable<UniversePosition>
    {
        /// <summary>
        /// Metres per sector.
        /// </summary>
        /// <remarks>
        /// A kilometre, near enough. Large enough that rebasing is rare and sector arithmetic almost
        /// never comes up; small enough that a float inside one is precise to a twentieth of a
        /// millimetre, which is far below anything the game can express.
        /// </remarks>
        public const float SectorSize = 1024f;

        public static readonly UniversePosition Zero = new(Vector3Int.zero, Vector3.zero);

        public readonly Vector3Int Sector;

        /// <summary>Metres within the sector, always in <c>[0, SectorSize)</c>.</summary>
        public readonly Vector3 Local;

        private UniversePosition(Vector3Int sector, Vector3 local)
        {
            Sector = sector;
            Local = local;
        }

        /// <summary>Builds a position from a sector and an offset that may be anywhere, normalizing it.</summary>
        public static UniversePosition Create(Vector3Int sector, Vector3 local)
        {
            Normalize(ref sector, ref local);
            return new UniversePosition(sector, local);
        }

        /// <summary>A position from plain metres, for tests, authoring, and anything near the origin.</summary>
        public static UniversePosition FromMetres(double x, double y, double z)
        {
            var sector = new Vector3Int(
                (int)Math.Floor(x / SectorSize),
                (int)Math.Floor(y / SectorSize),
                (int)Math.Floor(z / SectorSize));

            return new UniversePosition(sector, new Vector3(
                (float)(x - (double)sector.x * SectorSize),
                (float)(y - (double)sector.y * SectorSize),
                (float)(z - (double)sector.z * SectorSize)));
        }

        public static UniversePosition FromMetres(Vector3 metres) => FromMetres(metres.x, metres.y, metres.z);

        /// <summary>Where a render-space point actually is, given the origin that render space is measured from.</summary>
        public static UniversePosition FromRender(in UniversePosition origin, Vector3 render) => origin + render;

        /// <summary>
        /// This position in a peer's render space.
        /// </summary>
        /// <remarks>
        /// Meaningful only for positions near the origin — which is exactly when it is asked, because a
        /// peer renders what is near it. Anything asking about somewhere distant asks
        /// <see cref="DistanceTo"/>, which works in doubles and does not quietly lose the answer.
        /// </remarks>
        public Vector3 ToRender(in UniversePosition origin) => new(
            (Sector.x - origin.Sector.x) * SectorSize + (Local.x - origin.Local.x),
            (Sector.y - origin.Sector.y) * SectorSize + (Local.y - origin.Local.y),
            (Sector.z - origin.Sector.z) * SectorSize + (Local.z - origin.Local.z));

        /// <summary>Metres between two positions, exactly, however far apart they are.</summary>
        public double DistanceTo(in UniversePosition other)
        {
            double dx = ((double)Sector.x - other.Sector.x) * SectorSize + ((double)Local.x - other.Local.x);
            double dy = ((double)Sector.y - other.Sector.y) * SectorSize + ((double)Local.y - other.Local.y);
            double dz = ((double)Sector.z - other.Sector.z) * SectorSize + ((double)Local.z - other.Local.z);
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public static UniversePosition operator +(in UniversePosition position, Vector3 offset)
        {
            var sector = position.Sector;
            var local = position.Local + offset;
            Normalize(ref sector, ref local);
            return new UniversePosition(sector, local);
        }

        public static UniversePosition operator -(in UniversePosition position, Vector3 offset) => position + -offset;

        /// <summary>The offset from one position to another, in metres. Only meaningful when they are near.</summary>
        public static Vector3 operator -(in UniversePosition to, in UniversePosition from) => to.ToRender(from);

        /// <summary>
        /// Puts the offset back inside its sector, carrying whole sectors into the integer half.
        /// </summary>
        /// <remarks>
        /// The one place the invariant is established, so it is the one place it can be broken. Written
        /// with <c>floor</c> rather than a truncation so that negative offsets carry the way they should:
        /// one metre below sector zero is the last metre of sector minus one, not the first metre of
        /// sector zero going backwards.
        /// </remarks>
        private static void Normalize(ref Vector3Int sector, ref Vector3 local)
        {
            int cx = Mathf.FloorToInt(local.x / SectorSize);
            int cy = Mathf.FloorToInt(local.y / SectorSize);
            int cz = Mathf.FloorToInt(local.z / SectorSize);

            if ((cx | cy | cz) == 0)
            {
                return;
            }

            sector = new Vector3Int(sector.x + cx, sector.y + cy, sector.z + cz);
            local = new Vector3(
                local.x - cx * SectorSize,
                local.y - cy * SectorSize,
                local.z - cz * SectorSize);
        }

        public bool Equals(UniversePosition other) => Sector == other.Sector && Local == other.Local;

        public override bool Equals(object obj) => obj is UniversePosition other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Sector, Local);

        public static bool operator ==(in UniversePosition a, in UniversePosition b) => a.Equals(b);

        public static bool operator !=(in UniversePosition a, in UniversePosition b) => !a.Equals(b);

        public override string ToString() => $"[{Sector.x},{Sector.y},{Sector.z}]+({Local.x:F2},{Local.y:F2},{Local.z:F2})";

        #region - Wire -

        /// <summary>
        /// Sector as three varints, offset at the caller's precision.
        /// </summary>
        /// <remarks>
        /// Near the origin the sector costs one byte an axis and often less; far from it, it grows a byte
        /// at a time. The offset is bounded by a kilometre by construction, so its precision means the
        /// same thing everywhere — which is the entire point of splitting the number.
        /// </remarks>
        public void Write(NetworkWriter writer, float precision)
        {
            writer.WriteVarInt32(Sector.x);
            writer.WriteVarInt32(Sector.y);
            writer.WriteVarInt32(Sector.z);
            NetworkQuantization.WriteFixedPointVector3(writer, Local, precision);
        }

        public static UniversePosition Read(NetworkReader reader, float precision)
        {
            var sector = new Vector3Int(reader.ReadVarInt32(), reader.ReadVarInt32(), reader.ReadVarInt32());
            var local = NetworkQuantization.ReadFixedPointVector3(reader, precision);

            // Quantization can push an offset a hair outside its sector; normalizing on read keeps the
            // invariant true for a position that arrived rather than one that was built.
            return Create(sector, local);
        }

        #endregion
    }
}
