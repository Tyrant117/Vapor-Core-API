using System;
using System.Collections.Generic;
using UnityEngine;
using Vapor.Serialization;

namespace Vapor.Tests.Serialization
{
    public enum Element
    {
        None,
        Fire,
        Ice,
    }

    [Flags]
    public enum Resist
    {
        None = 0,
        Fire = 1,
        Ice = 2,
        Shock = 4,
    }

    /// <summary>
    /// The canonical fixture. Partial so the source generator produces a formatter for it when the
    /// analyzer is in scope for this assembly — see <see cref="VslGeneratorTests"/>.
    /// </summary>
    [VslSerializable]
    public partial class GoldenFixture
    {
        [VslComment("0-1, drives the HUD bar")]
        public float HealthFraction;

        public string Label;

        [SerializeField] private int _count;

        public Vector3 Position;
        public Color32 Tint;
        public Element Mode;
        public Resist Wards;
        public List<int> Values = new List<int>();
        public Dictionary<string, int> Counts = new Dictionary<string, int>();
        public GoldenChild Child;
        public string Notes;

        public int Count => _count;

        public static GoldenFixture Seeded()
        {
            var fixture = new GoldenFixture
            {
                HealthFraction = 0.62f,
                Label = "Aria \"the Bold\"",
                _count = 7,
                Position = new Vector3(0f, 1.5f, -3f),
                Tint = new Color32(0xFF, 0x88, 0x00, 0xFF),
                Mode = Element.Fire,
                Wards = Resist.Fire | Resist.Shock,
                Child = new GoldenChild { Id = "sword", Count = 1 },
                Notes = "First line.\nSecond line.",
            };

            fixture.Values.Add(1);
            fixture.Values.Add(2);
            fixture.Values.Add(3);
            fixture.Counts["kills"] = 42;
            return fixture;
        }
    }

    [VslSerializable]
    public partial class GoldenChild
    {
        public string Id;
        public int Count;
    }

    /// <summary>Covers every Unity value type VSL claims to support.</summary>
    [VslSerializable]
    public partial class UnityTypesFixture
    {
        public Vector2 Vector2;
        public Vector3 Vector3;
        public Vector4 Vector4;
        public Vector2Int Vector2Int;
        public Vector3Int Vector3Int;
        public Quaternion Quaternion;
        public Matrix4x4 Matrix;
        public Color Color;
        public Color Color8Bit;
        public Color32 Color32;
        public Rect Rect;
        public RectInt RectInt;
        public Bounds Bounds;
        public BoundsInt BoundsInt;
        public LayerMask LayerMask;
        public RenderingLayerMask RenderingLayerMask;
        public AnimationCurve Curve;
        public Gradient Gradient;
        public Hash128 Hash;

        public static UnityTypesFixture Seeded()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.red, 0f),
                    new GradientColorKey(Color.blue, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0f, 1f),
                });

            return new UnityTypesFixture
            {
                Vector2 = new Vector2(1f, 2f),
                Vector3 = new Vector3(1f, 2f, 3f),
                Vector4 = new Vector4(1f, 2f, 3f, 4f),
                Vector2Int = new Vector2Int(5, 6),
                Vector3Int = new Vector3Int(7, 8, 9),
                Quaternion = Quaternion.Euler(30f, 45f, 60f),
                Matrix = Matrix4x4.TRS(new Vector3(1f, 2f, 3f), Quaternion.identity, Vector3.one * 2f),
                // Not on an 8-bit boundary, so it must take the tuple form to survive exactly.
                Color = new Color(0.5f, 0.25f, 0.125f, 1f),
                Color8Bit = new Color(1f, 0x88 / 255f, 0f, 1f),
                Color32 = new Color32(0xFF, 0x88, 0x00, 0xFF),
                Rect = new Rect(1f, 2f, 3f, 4f),
                RectInt = new RectInt(1, 2, 3, 4),
                Bounds = new Bounds(new Vector3(1f, 2f, 3f), new Vector3(2f, 4f, 6f)),
                BoundsInt = new BoundsInt(new Vector3Int(1, 2, 3), new Vector3Int(4, 5, 6)),
                LayerMask = (LayerMask)0b1010,
                RenderingLayerMask = new RenderingLayerMask { value = 0b0110 },
                Curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f)),
                Gradient = gradient,
                Hash = Hash128.Compute("vsl"),
            };
        }
    }

    /// <summary>Covers the .NET collection shapes.</summary>
    [VslSerializable]
    public partial class CollectionsFixture
    {
        public int[] Array;
        public List<string> List;
        public HashSet<int> Set;
        public Queue<int> Queue;
        public Stack<int> Stack;
        public LinkedList<int> Linked;
        public Dictionary<string, int> StringKeyed;
        public Dictionary<Element, float> EnumKeyed;
        public Dictionary<Vector2Int, string> StructKeyed;
        public KeyValuePair<string, int> Pair;
        public (int, string) Tuple;
        public int? NullableSet;
        public int? NullableUnset;
        public List<GoldenChild> Nested;

        public static CollectionsFixture Seeded() => new CollectionsFixture
        {
            Array = new[] { 1, 2, 3 },
            List = new List<string> { "a", "b" },
            Set = new HashSet<int> { 1, 2, 3 },
            Queue = new Queue<int>(new[] { 1, 2, 3 }),
            Stack = new Stack<int>(new[] { 1, 2, 3 }),
            Linked = new LinkedList<int>(new[] { 1, 2, 3 }),
            StringKeyed = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 },
            EnumKeyed = new Dictionary<Element, float> { [Element.Fire] = 1.5f },
            StructKeyed = new Dictionary<Vector2Int, string> { [new Vector2Int(1, 2)] = "here" },
            Pair = new KeyValuePair<string, int>("k", 9),
            Tuple = (42, "answer"),
            NullableSet = 5,
            NullableUnset = null,
            Nested = new List<GoldenChild> { new GoldenChild { Id = "x", Count = 1 } },
        };
    }

    /// <summary>Opt-in policy: nothing serializes without <see cref="VslSerializeAttribute"/>.</summary>
    public partial class OptInFixture
    {
        [VslSerialize] public int Included;
        public int Excluded;
        [VslSerialize] public int Property { get; set; }
        public int NotAProperty { get; set; }
    }

    /// <summary>Unity-rules policy, with the per-member escape hatches.</summary>
    [VslSerializable]
    public partial class UnityRulesFixture
    {
        public int PublicField;
        [SerializeField] private int _serializeField;
        private int _plainPrivate = 11;
        [VslIgnore] public int Ignored;
        [VslSerialize] private int _forcedIn;
        [NonSerialized] public int NotSerialized;
        [VslName("renamed")] public int OriginalName;

        public int SerializeField => _serializeField;
        public int PlainPrivate => _plainPrivate;
        public int ForcedIn => _forcedIn;

        public static UnityRulesFixture Seeded() => new UnityRulesFixture
        {
            PublicField = 1,
            _serializeField = 2,
            _plainPrivate = 3,
            Ignored = 4,
            _forcedIn = 5,
            NotSerialized = 6,
            OriginalName = 7,
        };
    }

    [VslSerializable]
    public abstract partial class Ability
    {
        public float Cooldown;
    }

    [VslSerializable]
    public partial class FireballAbility : Ability
    {
        public int Damage;
    }

    [VslSerializable]
    [VslType("Heal")]
    public partial class HealingAbility : Ability
    {
        public int Amount;
    }

    [VslSerializable]
    public partial class PolymorphicFixture
    {
        public Ability Power;
        public List<Ability> Rotation = new List<Ability>();
    }

    [VslSerializable]
    public abstract partial class StatusEffect
    {
        public float Duration;
    }

    /// <summary>
    /// Deliberately carries the same <c>!Heal</c> tag as <see cref="HealingAbility"/>. Tags are short,
    /// so collisions across unrelated hierarchies are expected; resolution has to pick the candidate
    /// that fits the slot.
    /// </summary>
    [VslSerializable]
    [VslType("Heal")]
    public partial class HealingEffect : StatusEffect
    {
        public int PerTick;
    }

    [VslSerializable]
    public partial class TagCollisionFixture
    {
        public Ability Power;
        public StatusEffect Effect;
    }

    /// <summary>VSL has no back-reference syntax, so a cycle has to be reported rather than encoded.</summary>
    [VslSerializable]
    public partial class CycleNode
    {
        public string Id;
        public CycleNode Next;
        public List<CycleNode> Children = new List<CycleNode>();
    }

    [VslSerializable]
    public partial class ObjectReferenceFixture
    {
        public ScriptableObject Asset;
        public ScriptableObject Missing;
        public List<ScriptableObject> Assets = new List<ScriptableObject>();
    }
}
