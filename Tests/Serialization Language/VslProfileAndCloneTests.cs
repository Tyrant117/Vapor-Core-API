using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Vapor.Serialization;

namespace Vapor.Tests.Serialization
{
    #region - Fixtures -

    /// <summary>A template/save split on one class: config in every profile, live state in Save only.</summary>
    [VslSerializable, VslCloneable]
    public partial class ProfiledFixture
    {
        public string Name = "thing";
        public float MaxHealth = 100f;

        [VslProfile(VslProfiles.Save)]
        public float Health = 100f;

        [VslProfile(VslProfiles.Save)]
        public List<int> Visited = new List<int>();

        [VslProfile(VslProfiles.Template)]
        public string DesignerNotes = "";
    }

    [VslSerializable, VslCloneable]
    public partial class CloneBase
    {
        public string Id = "base";
        [SerializeField] private int _secret = 3;
        public List<string> Tags = new List<string>();
        public Vector3 Offset;
        public CloneChild Child;

        public int Secret { get => _secret; set => _secret = value; }
    }

    [VslSerializable, VslCloneable]
    public partial class CloneDerived : CloneBase
    {
        public int Level = 1;
        public List<CloneChild> Children = new List<CloneChild>();
        public Dictionary<string, int> Counts = new Dictionary<string, int>();
        public GoldenChild Plain;   // serializable but not cloneable: runtime deep copy
    }

    [VslSerializable, VslCloneable]
    public partial class CloneChild
    {
        public string Label;
        public int Value;
    }

    [VslCloneable]
    public abstract partial class CloneAbstractRoot
    {
        [VslSerialize] public int Shared;
    }

    [VslSerializable, VslCloneable]
    public partial class CloneConcrete : CloneAbstractRoot
    {
        public string Own = "x";
    }

    #endregion

    /// <summary>Member profiles and generated cloning: the two VSL features the actor model rests on.</summary>
    public class VslProfileAndCloneTests
    {
        private static bool HasGenerated<T>() => typeof(T).GetNestedType("VslGeneratedFormatter") != null;

        #region - Profiles -

        [Test]
        public void ATemplateWriteOmitsSaveOnlyMembersAndASaveWriteOmitsTemplateOnlyOnes()
        {
            var value = new ProfiledFixture { Health = 42f, DesignerNotes = "keep small" };
            value.Visited.Add(7);

            var template = Vsl.Serialize(value, VslContext.For(VslProfiles.Template));
            StringAssert.Contains("maxHealth", template);
            StringAssert.Contains("designerNotes", template);
            StringAssert.DoesNotContain("health:", template.Replace("maxHealth", string.Empty));
            StringAssert.DoesNotContain("visited", template);

            var save = Vsl.Serialize(value, VslContext.For(VslProfiles.Save));
            StringAssert.Contains("health", save);
            StringAssert.Contains("visited", save);
            StringAssert.Contains("maxHealth", save);
            StringAssert.DoesNotContain("designerNotes", save);

            var all = Vsl.Serialize(value);
            StringAssert.Contains("visited", all);
            StringAssert.Contains("designerNotes", all);
        }

        [Test]
        public void ReadingUnderAProfileLeavesOtherMembersAlone()
        {
            var value = new ProfiledFixture { Health = 42f, MaxHealth = 250f, DesignerNotes = "n" };
            var all = Vsl.Serialize(value);

            var target = new ProfiledFixture();
            Vsl.Populate(target, all, VslContext.For(VslProfiles.Save));
            Assert.AreEqual(42f, target.Health);
            Assert.AreEqual(250f, target.MaxHealth, "MaxHealth is in every profile");
            Assert.AreEqual("", target.DesignerNotes, "template-only: skipped");

            var fresh = Vsl.Deserialize<ProfiledFixture>(all, VslContext.For(VslProfiles.Template));
            Assert.AreEqual(100f, fresh.Health, "save-only: skipped");
            Assert.AreEqual("n", fresh.DesignerNotes);
        }

        [Test]
        public void GeneratedAndReflectionAgreeUnderEveryProfile()
        {
            if (!HasGenerated<ProfiledFixture>())
            {
                Assert.Inconclusive("The VSL analyzer did not generate a formatter for ProfiledFixture in this assembly.");
                return;
            }

            var generated = VslFormatterRegistry.Get<ProfiledFixture>();
            var reflection = new ReflectionFormatter<ProfiledFixture>();
            var value = new ProfiledFixture { Health = 9f, DesignerNotes = "d" };
            value.Visited.Add(1);

            foreach (var profiles in new[] { VslProfiles.All, VslProfiles.Template, VslProfiles.Save })
            {
                Assert.AreEqual(Write(reflection, value, profiles), Write(generated, value, profiles), $"profile {profiles}");
            }
        }

        private static string Write<T>(IVslFormatter<T> formatter, T value, VslProfiles profiles)
        {
            var context = VslContext.For(profiles);
            var writer = new VslWriter(context);
            try
            {
                writer.WriteHeader();
                formatter.Write(ref writer, value, context);
                return writer.ToString();
            }
            finally
            {
                writer.Dispose();
            }
        }

        [Test]
        public void TheSchemaExposesProfiles()
        {
            var schema = VslTypeSchema.Get(typeof(ProfiledFixture));
            Assert.AreEqual(VslProfiles.All, schema.Find("maxHealth").Profiles);
            Assert.AreEqual(VslProfiles.Save, schema.Find("health").Profiles);
            Assert.AreEqual(VslProfiles.Template, schema.Find("designerNotes").Profiles);
            Assert.IsTrue(schema.Find("health").IsIn(VslProfiles.Save | VslProfiles.Template));
            Assert.IsFalse(schema.Find("health").IsIn(VslProfiles.Template));
        }

        #endregion

        #region - Clone -

        [Test]
        public void CloneIsDeepAndTypedThroughTheHierarchy()
        {
            var original = new CloneDerived
            {
                Id = "hero",
                Secret = 11,
                Offset = new Vector3(1, 2, 3),
                Child = new CloneChild { Label = "c", Value = 1 },
                Level = 5,
                Plain = new GoldenChild { Id = "g", Count = 2 },
            };
            original.Tags.Add("a");
            original.Children.Add(new CloneChild { Label = "k", Value = 9 });
            original.Counts["x"] = 3;

            CloneDerived copy = original.Clone();
            Assert.AreNotSame(original, copy);
            Assert.AreEqual("hero", copy.Id);
            Assert.AreEqual(11, copy.Secret, "a private base member is copied by the base's own CopyFrom");
            Assert.AreEqual(new Vector3(1, 2, 3), copy.Offset);
            Assert.AreEqual(5, copy.Level);

            Assert.AreNotSame(original.Tags, copy.Tags);
            CollectionAssert.AreEqual(original.Tags, copy.Tags);
            Assert.AreNotSame(original.Child, copy.Child);
            Assert.AreEqual("c", copy.Child.Label);
            Assert.AreNotSame(original.Children, copy.Children);
            Assert.AreNotSame(original.Children[0], copy.Children[0]);
            Assert.AreEqual(9, copy.Children[0].Value);
            Assert.AreNotSame(original.Counts, copy.Counts);
            Assert.AreEqual(3, copy.Counts["x"]);
            Assert.AreNotSame(original.Plain, copy.Plain);
            Assert.AreEqual("g", copy.Plain.Id);

            // Mutating the copy never reaches the original.
            copy.Children[0].Value = 100;
            copy.Tags.Add("b");
            Assert.AreEqual(9, original.Children[0].Value);
            Assert.AreEqual(1, original.Tags.Count);
        }

        [Test]
        public void CloningThroughABaseReferenceYieldsTheRuntimeType()
        {
            CloneBase asBase = new CloneDerived { Id = "d", Level = 7 };
            var copy = asBase.Clone();
            Assert.IsInstanceOf<CloneDerived>(copy);
            Assert.AreEqual(7, ((CloneDerived)copy).Level);
            Assert.IsInstanceOf<IVslCloneable>(asBase);
            Assert.IsInstanceOf<CloneDerived>(((IVslCloneable)asBase).VslCloneObject());
        }

        [Test]
        public void AnAbstractRootContributesToTheChain()
        {
            var original = new CloneConcrete { Shared = 4, Own = "y" };
            CloneAbstractRoot asRoot = original;
            var copy = (CloneConcrete)asRoot.Clone();
            Assert.AreEqual(4, copy.Shared);
            Assert.AreEqual("y", copy.Own);
        }

        [Test]
        public void CopyFromReplacesStateInPlace()
        {
            var target = new CloneDerived { Id = "old", Level = 1 };
            var source = new CloneDerived { Id = "new", Level = 3 };
            source.Tags.Add("t");
            target.CopyFrom(source);
            Assert.AreEqual("new", target.Id);
            Assert.AreEqual(3, target.Level);
            CollectionAssert.AreEqual(new[] { "t" }, target.Tags);
            Assert.AreNotSame(source.Tags, target.Tags);
        }

        [Test]
        public void TheRuntimeDeepCopyPrefersGeneratedClones()
        {
            var child = new CloneChild { Label = "l", Value = 2 };
            var copy = VslClone.DeepCopy(child);
            Assert.AreNotSame(child, copy);
            Assert.AreEqual("l", copy.Label);

            var list = new List<CloneChild> { child };
            var listCopy = VslClone.DeepCopy(list);
            Assert.AreNotSame(list, listCopy);
            Assert.AreNotSame(list[0], listCopy[0]);
            Assert.AreEqual(2, listCopy[0].Value);

            var plain = new GoldenChild { Id = "p", Count = 5 };
            var plainCopy = VslClone.DeepCopy(plain);
            Assert.AreNotSame(plain, plainCopy);
            Assert.AreEqual(5, plainCopy.Count);
        }

        #endregion
    }
}
