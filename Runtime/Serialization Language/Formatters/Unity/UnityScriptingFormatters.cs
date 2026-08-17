using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace Vapor.Serialization
{
    /// <summary>
    /// Writes a layer mask as hex, and reads either hex or a list of layer names.
    /// </summary>
    /// <remarks>
    /// Names are accepted but not written: <c>[ Water Player ]</c> is the friendlier thing to author,
    /// while hex is the only form that survives a project whose layer names have changed.
    /// </remarks>
    [NoAutoStaticsCleanup]
    public sealed class LayerMaskFormatter : VslFormatter<LayerMask>
    {
        public static readonly LayerMaskFormatter Instance = new LayerMaskFormatter();

        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in LayerMask value, VslContext context) =>
            writer.WriteHex(unchecked((uint)value.value), 8);

        public override LayerMask Read(ref VslReader reader, VslContext context)
        {
            if (reader.PeekKind() != VslValueKind.Sequence)
            {
                return (LayerMask)unchecked((int)reader.ReadUInt64());
            }

            var mask = 0;
            reader.ReadSequenceStart();
            while (reader.TryReadSequenceItem())
            {
                var layer = LayerMask.NameToLayer(reader.ReadString());
                if (layer >= 0)
                {
                    mask |= 1 << layer;
                }
            }

            return (LayerMask)mask;
        }
    }

    [NoAutoStaticsCleanup]
    public sealed class RenderingLayerMaskFormatter : VslFormatter<RenderingLayerMask>
    {
        public static readonly RenderingLayerMaskFormatter Instance = new RenderingLayerMaskFormatter();

        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in RenderingLayerMask value, VslContext context) =>
            writer.WriteHex(value.value, 8);

        public override RenderingLayerMask Read(ref VslReader reader, VslContext context)
        {
            if (reader.PeekKind() != VslValueKind.Sequence)
            {
                return new RenderingLayerMask { value = unchecked((uint)reader.ReadUInt64()) };
            }

            var mask = 0u;
            reader.ReadSequenceStart();
            while (reader.TryReadSequenceItem())
            {
                var layer = RenderingLayerMask.NameToRenderingLayer(reader.ReadString());
                if (layer >= 0)
                {
                    mask |= 1u << layer;
                }
            }

            return new RenderingLayerMask { value = mask };
        }
    }

    /// <summary>
    /// A keyframe as <c>(time, value, inTangent, outTangent)</c>, extended to
    /// <c>(..., inWeight, outWeight, weightedMode)</c> only when the key actually carries weights.
    /// </summary>
    [NoAutoStaticsCleanup]
    public sealed class KeyframeFormatter : VslFormatter<Keyframe>
    {
        public static readonly KeyframeFormatter Instance = new KeyframeFormatter();

        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in Keyframe value, VslContext context)
        {
            writer.BeginTuple();
            writer.WriteSingle(value.time);
            writer.WriteSingle(value.value);
            writer.WriteSingle(value.inTangent);
            writer.WriteSingle(value.outTangent);

            if (value.weightedMode != WeightedMode.None)
            {
                writer.WriteSingle(value.inWeight);
                writer.WriteSingle(value.outWeight);
                writer.WriteIdentifier(value.weightedMode.ToString());
            }

            writer.EndTuple();
        }

        public override Keyframe Read(ref VslReader reader, VslContext context)
        {
            reader.ReadTupleStart();
            var time = reader.ReadSingleOr();
            var value = reader.ReadSingleOr();
            var inTangent = reader.ReadSingleOr();
            var outTangent = reader.ReadSingleOr();

            var keyframe = new Keyframe(time, value, inTangent, outTangent);

            if (!reader.AtEnd())
            {
                keyframe.inWeight = reader.ReadSingleOr();
                keyframe.outWeight = reader.ReadSingleOr();
                if (!reader.AtEnd())
                {
                    keyframe.weightedMode = EnumFormatter<WeightedMode>.Instance.Read(ref reader, context);
                }
            }

            reader.ReadTupleEnd();
            return keyframe;
        }
    }

    [NoAutoStaticsCleanup]
    public sealed class AnimationCurveFormatter : VslFormatter<AnimationCurve>
    {
        public static readonly AnimationCurveFormatter Instance = new AnimationCurveFormatter();

        public override void Write(ref VslWriter writer, in AnimationCurve value, VslContext context)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            writer.BeginObject();

            writer.WriteMember("preWrap");
            writer.WriteIdentifier(value.preWrapMode.ToString());

            writer.WriteMember("postWrap");
            writer.WriteIdentifier(value.postWrapMode.ToString());

            writer.WriteMember("keys");
            var keys = value.keys;
            writer.BeginSequence(keys.Length <= context.Options.InlineSequenceLimit);
            foreach (var key in keys)
            {
                KeyframeFormatter.Instance.Write(ref writer, key, context);
            }

            writer.EndSequence();

            writer.EndObject();
        }

        public override AnimationCurve Read(ref VslReader reader, VslContext context)
        {
            if (reader.TryReadNull())
            {
                return null;
            }

            var keys = new List<Keyframe>();
            var preWrap = WrapMode.ClampForever;
            var postWrap = WrapMode.ClampForever;
            var sawPreWrap = false;
            var sawPostWrap = false;
            var sawKeys = false;

            reader.ReadObjectStart();
            while (reader.TryReadMemberName(out var name))
            {
                if (VslNames.Matches(name, "preWrap") || VslNames.Matches(name, "preWrapMode"))
                {
                    sawPreWrap = true;
                    preWrap = EnumFormatter<WrapMode>.Instance.Read(ref reader, context);
                }
                else if (VslNames.Matches(name, "postWrap") || VslNames.Matches(name, "postWrapMode"))
                {
                    sawPostWrap = true;
                    postWrap = EnumFormatter<WrapMode>.Instance.Read(ref reader, context);
                }
                else if (VslNames.Matches(name, "keys"))
                {
                    sawKeys = true;
                    reader.ReadSequenceStart();
                    while (reader.TryReadSequenceItem())
                    {
                        keys.Add(KeyframeFormatter.Instance.Read(ref reader, context));
                    }
                }
                else
                {
                    if (context.Options.Strict)
                    {
                        throw new VslException($"'{name.ToString()}' is not an AnimationCurve member.");
                    }

                    reader.SkipValue();
                }
            }

            if (context.Options.Strict)
            {
                if (!sawPreWrap) throw new VslException("'preWrap' is missing from the AnimationCurve object.");
                if (!sawPostWrap) throw new VslException("'postWrap' is missing from the AnimationCurve object.");
                if (!sawKeys) throw new VslException("'keys' is missing from the AnimationCurve object.");
            }

            return new AnimationCurve(keys.ToArray())
            {
                preWrapMode = preWrap,
                postWrapMode = postWrap,
            };
        }
    }

    [NoAutoStaticsCleanup]
    public sealed class Hash128Formatter : VslFormatter<Hash128>
    {
        public static readonly Hash128Formatter Instance = new Hash128Formatter();

        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in Hash128 value, VslContext context) =>
            writer.WriteString(value.ToString());

        public override Hash128 Read(ref VslReader reader, VslContext context)
        {
            var text = reader.ReadString();
            return string.IsNullOrEmpty(text) ? default : Hash128.Parse(text);
        }
    }
}
