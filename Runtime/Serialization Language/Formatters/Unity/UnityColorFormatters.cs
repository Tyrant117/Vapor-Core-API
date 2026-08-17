using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace Vapor.Serialization
{
    /// <summary>
    /// Writes a color as <c>0xRRGGBBAA</c> when that is exact, and as a float tuple otherwise.
    /// </summary>
    /// <remarks>
    /// Hex is the legible form and matches what the Unity colour picker produces, but it only holds
    /// eight bits per channel. A colour that is HDR, or simply not on an 8-bit boundary (0.5 is
    /// 127.5/255), falls back to the tuple so the value survives the round trip exactly. Both forms
    /// are accepted on read.
    /// </remarks>
    [NoAutoStaticsCleanup]
    public sealed class ColorFormatter : VslFormatter<Color>
    {
        private const float Epsilon = 1e-4f;

        public static readonly ColorFormatter Instance = new ColorFormatter();

        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in Color value, VslContext context)
        {
            if (TryToRgba32(value, out var packed))
            {
                writer.WriteHex(packed, 8);
                return;
            }

            writer.BeginTuple();
            writer.WriteSingle(value.r);
            writer.WriteSingle(value.g);
            writer.WriteSingle(value.b);
            writer.WriteSingle(value.a);
            writer.EndTuple();
        }

        public override Color Read(ref VslReader reader, VslContext context)
        {
            if (reader.PeekKind() == VslValueKind.Tuple)
            {
                reader.ReadTupleStart();
                var r = reader.ReadSingleOr();
                var g = reader.ReadSingleOr();
                var b = reader.ReadSingleOr();
                var a = reader.ReadSingleOr(1f);
                reader.ReadTupleEnd();
                return new Color(r, g, b, a);
            }

            var value = reader.ReadUInt64();
            return new Color(
                ((value >> 24) & 0xFF) / 255f,
                ((value >> 16) & 0xFF) / 255f,
                ((value >> 8) & 0xFF) / 255f,
                (value & 0xFF) / 255f);
        }

        private static bool TryToRgba32(Color color, out ulong packed)
        {
            packed = 0;
            if (!TryChannel(color.r, out var r) ||
                !TryChannel(color.g, out var g) ||
                !TryChannel(color.b, out var b) ||
                !TryChannel(color.a, out var a))
            {
                return false;
            }

            packed = ((ulong)r << 24) | ((ulong)g << 16) | ((ulong)b << 8) | a;
            return true;
        }

        private static bool TryChannel(float value, out uint quantized)
        {
            quantized = 0;
            if (value < 0f || value > 1f)
            {
                return false;
            }

            var scaled = value * 255f;
            var rounded = Mathf.Round(scaled);
            if (Mathf.Abs(scaled - rounded) > Epsilon)
            {
                return false;
            }

            quantized = (uint)rounded;
            return true;
        }
    }

    [NoAutoStaticsCleanup]
    public sealed class Color32Formatter : VslFormatter<Color32>
    {
        public static readonly Color32Formatter Instance = new Color32Formatter();

        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in Color32 value, VslContext context) =>
            writer.WriteHex(((ulong)value.r << 24) | ((ulong)value.g << 16) | ((ulong)value.b << 8) | value.a, 8);

        public override Color32 Read(ref VslReader reader, VslContext context)
        {
            if (reader.PeekKind() == VslValueKind.Tuple)
            {
                reader.ReadTupleStart();
                var r = reader.ReadInt32Or();
                var g = reader.ReadInt32Or();
                var b = reader.ReadInt32Or();
                var a = reader.ReadInt32Or(255);
                reader.ReadTupleEnd();
                return new Color32((byte)r, (byte)g, (byte)b, (byte)a);
            }

            var value = reader.ReadUInt64();
            return new Color32(
                (byte)((value >> 24) & 0xFF),
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)(value & 0xFF));
        }
    }

    [NoAutoStaticsCleanup]
    public sealed class GradientColorKeyFormatter : VslFormatter<GradientColorKey>
    {
        public static readonly GradientColorKeyFormatter Instance = new GradientColorKeyFormatter();

        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in GradientColorKey value, VslContext context)
        {
            writer.BeginTuple();
            ColorFormatter.Instance.Write(ref writer, value.color, context);
            writer.WriteSingle(value.time);
            writer.EndTuple();
        }

        public override GradientColorKey Read(ref VslReader reader, VslContext context)
        {
            reader.ReadTupleStart();
            var color = reader.AtEnd() ? Color.white : ColorFormatter.Instance.Read(ref reader, context);
            var time = reader.ReadSingleOr();
            reader.ReadTupleEnd();
            return new GradientColorKey(color, time);
        }
    }

    [NoAutoStaticsCleanup]
    public sealed class GradientAlphaKeyFormatter : VslFormatter<GradientAlphaKey>
    {
        public static readonly GradientAlphaKeyFormatter Instance = new GradientAlphaKeyFormatter();

        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in GradientAlphaKey value, VslContext context)
        {
            writer.BeginTuple();
            writer.WriteSingle(value.alpha);
            writer.WriteSingle(value.time);
            writer.EndTuple();
        }

        public override GradientAlphaKey Read(ref VslReader reader, VslContext context)
        {
            reader.ReadTupleStart();
            var alpha = reader.ReadSingleOr();
            var time = reader.ReadSingleOr();
            reader.ReadTupleEnd();
            return new GradientAlphaKey(alpha, time);
        }
    }

    [NoAutoStaticsCleanup]
    public sealed class GradientFormatter : VslFormatter<Gradient>
    {
        public static readonly GradientFormatter Instance = new GradientFormatter();

        public override void Write(ref VslWriter writer, in Gradient value, VslContext context)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            writer.BeginObject();

            writer.WriteMember("mode");
            writer.WriteIdentifier(value.mode.ToString());

            writer.WriteMember("colors");
            var colorKeys = value.colorKeys;
            writer.BeginSequence(colorKeys.Length <= context.Options.InlineSequenceLimit);
            foreach (var key in colorKeys)
            {
                GradientColorKeyFormatter.Instance.Write(ref writer, key, context);
            }

            writer.EndSequence();

            writer.WriteMember("alphas");
            var alphaKeys = value.alphaKeys;
            writer.BeginSequence(alphaKeys.Length <= context.Options.InlineSequenceLimit);
            foreach (var key in alphaKeys)
            {
                GradientAlphaKeyFormatter.Instance.Write(ref writer, key, context);
            }

            writer.EndSequence();

            writer.EndObject();
        }

        public override Gradient Read(ref VslReader reader, VslContext context)
        {
            if (reader.TryReadNull())
            {
                return null;
            }

            var gradient = new Gradient();
            var colors = new List<GradientColorKey>();
            var alphas = new List<GradientAlphaKey>();
            var mode = GradientMode.Blend;
            var sawMode = false;
            var sawColors = false;
            var sawAlphas = false;

            reader.ReadObjectStart();
            while (reader.TryReadMemberName(out var name))
            {
                if (VslNames.Matches(name, "mode"))
                {
                    sawMode = true;
                    mode = EnumFormatter<GradientMode>.Instance.Read(ref reader, context);
                }
                else if (VslNames.Matches(name, "colors") || VslNames.Matches(name, "colorKeys"))
                {
                    sawColors = true;
                    reader.ReadSequenceStart();
                    while (reader.TryReadSequenceItem())
                    {
                        colors.Add(GradientColorKeyFormatter.Instance.Read(ref reader, context));
                    }
                }
                else if (VslNames.Matches(name, "alphas") || VslNames.Matches(name, "alphaKeys"))
                {
                    sawAlphas = true;
                    reader.ReadSequenceStart();
                    while (reader.TryReadSequenceItem())
                    {
                        alphas.Add(GradientAlphaKeyFormatter.Instance.Read(ref reader, context));
                    }
                }
                else
                {
                    if (context.Options.Strict)
                    {
                        throw new VslException($"'{name.ToString()}' is not a Gradient member.");
                    }

                    reader.SkipValue();
                }
            }

            if (context.Options.Strict)
            {
                if (!sawMode) throw new VslException("'mode' is missing from the Gradient object.");
                if (!sawColors) throw new VslException("'colors' is missing from the Gradient object.");
                if (!sawAlphas) throw new VslException("'alphas' is missing from the Gradient object.");
            }

            // SetKeys before mode: assigning keys resets some gradient state.
            gradient.SetKeys(colors.ToArray(), alphas.ToArray());
            gradient.mode = mode;
            return gradient;
        }
    }
}
