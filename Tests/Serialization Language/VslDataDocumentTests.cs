using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using Vapor.GameplayTags;
using Vapor.Serialization;
using Vapor.Unsafe;

namespace Vapor.Tests.Serialization
{
    /// <summary>
    /// The shape the data registry documents are stored in: a polymorphic <c>List&lt;IData&gt;</c>
    /// whose entries carry their own type tag.
    /// </summary>
    public class VslDataDocumentTests
    {
        #region Localized strings

        [Test]
        public void LocalizedStringSurvivesByName()
        {
            var original = new LocalizedString("UI Text", "tag.fire.burn");
            var copy = Vsl.Deserialize<LocalizedString>(Vsl.Serialize(original));

            Assert.AreEqual(TableReference.Type.Name, copy.TableReference.ReferenceType);
            Assert.AreEqual("UI Text", copy.TableReference.TableCollectionName);
            Assert.AreEqual(TableEntryReference.Type.Name, copy.TableEntryReference.ReferenceType);
            Assert.AreEqual("tag.fire.burn", copy.TableEntryReference.Key);
        }

        [Test]
        public void LocalizedStringSurvivesByGuidAndKeyId()
        {
            var guid = Guid.NewGuid();
            var original = new LocalizedString(guid, 4815162342L);
            var copy = Vsl.Deserialize<LocalizedString>(Vsl.Serialize(original));

            Assert.AreEqual(TableReference.Type.Guid, copy.TableReference.ReferenceType);
            Assert.AreEqual(guid, copy.TableReference.TableCollectionNameGuid);
            Assert.AreEqual(TableEntryReference.Type.Id, copy.TableEntryReference.ReferenceType);
            Assert.AreEqual(4815162342L, copy.TableEntryReference.KeyId);
        }

        [Test]
        public void EmptyLocalizedStringWritesNull()
        {
            var text = Vsl.Serialize(new LocalizedString());

            StringAssert.Contains("null", text);
            Assert.IsNull(Vsl.Deserialize<LocalizedString>(text));
        }

        [Test]
        public void LocalizedStringWrittenThroughItsBackingFieldsStillSerializes()
        {
            // What an inspector editing the [SerializeField] members produces: the backing string is
            // set, but ReferenceType - which Unity only derives in OnAfterDeserialize - is still Empty.
            // Writing that naively would discard the binding.
            var stale = WithRawTableName(new LocalizedString(), "UI Text");
            Assert.AreEqual(TableReference.Type.Empty, stale.TableReference.ReferenceType, "fixture no longer reproduces the stale state");

            var copy = Vsl.Deserialize<LocalizedString>(Vsl.Serialize(stale));

            Assert.IsNotNull(copy);
            Assert.AreEqual("UI Text", copy.TableReference.TableCollectionName);
        }

        private static LocalizedString WithRawTableName(LocalizedString target, string tableName)
        {
            const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;

            var tableField = typeof(LocalizedReference).GetField("m_TableReference", Instance);
            var nameField = typeof(TableReference).GetField("m_TableCollectionName", Instance);
            Assert.IsNotNull(tableField, "LocalizedReference.m_TableReference was renamed");
            Assert.IsNotNull(nameField, "TableReference.m_TableCollectionName was renamed");

            // Boxed so the reflected write lands on the struct that is then assigned back.
            object table = tableField.GetValue(target);
            nameField.SetValue(table, tableName);
            tableField.SetValue(target, table);
            return target;
        }

        #endregion

        #region Documents

        [Test]
        public void GameplayTagDataSurvivesWithItsDerivedKey()
        {
            var original = new GameplayTagData("Ability.Fire.Burn")
                .WithEditorTooltip("Burns the target.")
                .WithLocalization(("UI Text", "ability.fire.burn"), ("UI Text", "ability.fire.burn.desc"))
                .WithAddressableIcon("Icons/Ability/Burn");

            var copy = Vsl.Deserialize<GameplayTagData>(Vsl.Serialize(original));

            Assert.AreEqual(original.Name, copy.Name);
            Assert.AreEqual("Burns the target.", copy.EditorTooltip);
            Assert.AreEqual("ability.fire.burn", copy.LocalizedName.TableEntryReference.Key);
            Assert.AreEqual("ability.fire.burn.desc", copy.LocalizedDescription.TableEntryReference.Key);

            // Key is not stored; it is derived when Name is set. That is what keeps the document
            // legible and makes a hand-edited name produce the right identifier.
            Assert.AreEqual("Ability.Fire.Burn".Hash32(), copy.Key);
            Assert.AreEqual(original.Key, copy.Key);
        }

        [Test]
        public void AnIconRoundTripsAsItsLocatorAndNothingElse()
        {
            var original = new GameplayTagData("Ability.Fire.Burn")
                .WithAddressableIcon("Icons/Ability/Burn");

            var copy = Vsl.Deserialize<GameplayTagData>(Vsl.Serialize(original));

            // What is stored is the locator, not the sprite: the document round trips in a project
            // where that address resolves to nothing, and reading it loads no artwork either way.
            Assert.AreEqual(VslAssetSource.Addressable, copy.IconRef.Source);
            Assert.AreEqual("Icons/Ability/Burn", copy.IconRef.Key);
            Assert.AreEqual(original.IconRef, copy.IconRef);
        }

        [Test]
        public void DocumentRoundTripsThroughTheStore()
        {
            var entries = new List<IData>
            {
                new GameplayTagData("Vapor.State.Dead"),
                new GameplayTagData("Vapor.Class.Elite").WithEditorTooltip("Tougher than normal."),
            };

            var copy = VslDataStore.Read(VslDataStore.Write(entries));

            Assert.AreEqual(2, copy.Count);
            Assert.AreEqual("Vapor.State.Dead", copy[0].Name);
            Assert.AreEqual("Vapor.Class.Elite", copy[1].Name);
            Assert.AreEqual("Tougher than normal.", ((GameplayTagData)copy[1]).EditorTooltip);
        }

        [Test]
        public void DocumentEntriesCarryTheirTypeTag()
        {
            var text = VslDataStore.Write(new List<IData> { new GameplayTagData("Vapor.State.Dead") });

            // Every entry naming its own type is what lets one document hold a base type and its
            // subclasses, and what lets the loader rebuild them without being told what to expect.
            StringAssert.Contains($"!{nameof(GameplayTagData)}", text);
        }

        [Test]
        public void EmptyDocumentReadsAsNoEntries()
        {
            Assert.IsEmpty(VslDataStore.Read(null));
            Assert.IsEmpty(VslDataStore.Read(string.Empty));
            Assert.IsEmpty(VslDataStore.Read("@vsl 1\n\n[]"));
        }

        [Test]
        public void CommentsFromTheTypeAreWrittenIntoTheDocument()
        {
            var text = VslDataStore.Write(new List<IData> { new GameplayTagData("Vapor.State.Dead") });

            // [VslComment] is how a document documents itself, which is what makes an exported file a
            // usable template for writing more of them.
            StringAssert.Contains("#", text);
        }

        #endregion

        #region Sub-asset keys

        [Test]
        public void SubAssetKeysSplitAndCombine()
        {
            Assert.IsTrue(VslAssetLocator.TrySplitSubKey("Characters/Hero[Run]", out var main, out var sub));
            Assert.AreEqual("Characters/Hero", main);
            Assert.AreEqual("Run", sub);

            Assert.AreEqual("Characters/Hero[Run]", VslAssetLocator.CombineSubKey("Characters/Hero", "Run"));
            Assert.AreEqual("Characters/Hero", VslAssetLocator.CombineSubKey("Characters/Hero", null));
        }

        [Test]
        public void PlainKeysAreNotMistakenForSubAssets()
        {
            // A key with no brackets, an unclosed one, or empty brackets is a plain address. Reading
            // any of them as a sub-asset would send the load looking for something that is not there.
            foreach (var key in new[] { "Characters/Hero", "Characters/Hero[", "Characters/Hero[]", "[Run]", string.Empty })
            {
                Assert.IsFalse(VslAssetLocator.TrySplitSubKey(key, out var main, out _), $"'{key}' should not split");
                Assert.AreEqual(key, main);
            }
        }

        [Test]
        public void SubAssetKeyKeepsTheLastBracketedSegment()
        {
            // A path may legitimately contain brackets, so the sub-asset is the final group.
            Assert.IsTrue(VslAssetLocator.TrySplitSubKey("Models/[WIP]/Hero[Idle]", out var main, out var sub));
            Assert.AreEqual("Models/[WIP]/Hero", main);
            Assert.AreEqual("Idle", sub);
        }

        #endregion

        #region Store

        [Test]
        public void AuthoredTypesIncludeGameplayTagData()
        {
            CollectionAssert.Contains(new List<Type>(VslDataStore.GetAuthoredTypes()), typeof(GameplayTagData));
        }

        [Test]
        public void DocumentPathIsDerivedFromTheTypeName()
        {
            Assert.AreEqual("Assets/Vapor/Data/GameplayTagData.vsl", VslDataStore.GetAssetPath(typeof(GameplayTagData)));
            Assert.AreEqual(nameof(GameplayTagData), VslDataStore.GetFileName(typeof(GameplayTagData)));
        }

        [Test]
        public void EveryTypeInAFamilyResolvesToOneDocument()
        {
            // A family shares its root's file, so two concrete types of it must never name two paths -
            // that would split the family and leave half of it unregistered.
            foreach (var root in VslDataStore.GetAuthoredTypes())
            {
                foreach (var concrete in VslDataStore.GetConcreteTypes(root))
                {
                    Assert.AreEqual(root, VslDataStore.GetDocumentOwner(concrete),
                        $"{concrete.Name} does not resolve to {root.Name}'s document");
                }
            }
        }

        [Test]
        public void ATypeWithNoAuthoredAncestorOwnsItsOwnDocument()
        {
            Assert.AreEqual(typeof(GameplayTagData), VslDataStore.GetDocumentOwner(typeof(GameplayTagData)));
            CollectionAssert.Contains(VslDataStore.GetConcreteTypes(typeof(GameplayTagData)), typeof(GameplayTagData));
        }

        [Test]
        public void DepthIsMeasuredFromTheRoot()
        {
            Assert.AreEqual(0, VslDataStore.GetDepth(typeof(GameplayTagData), typeof(GameplayTagData)));
        }

        [Test]
        public void OnlyRootsAreListedAsAuthoredTypes()
        {
            // Every listed type owns its own document. A subclass sharing its family's file must not
            // also appear, or it would claim a second folder and split the family in two.
            foreach (var type in VslDataStore.GetAuthoredTypes())
            {
                Assert.AreEqual(type, VslDataStore.GetDocumentOwner(type), $"{type.Name} is not a document owner");
            }
        }

        [Test]
        public void NamePrefixFallsBackToTheTypeName()
        {
            // GameplayTagData declares no prefix - tags span many roots - so a new entry is named
            // after the type, which is the behaviour every unannotated type gets.
            Assert.AreEqual(nameof(GameplayTagData), VslDataStore.GetNamePrefix(typeof(GameplayTagData)));
        }

        [Test]
        public void OwningTypeComesFromTheEntriesWhateverTheSetIsCalled()
        {
            var entries = new List<IData> { new GameplayTagData("Vapor.State.Dead") };

            // A set is named freely - WeaponTags, CraftingTags - so the entries are the only reliable
            // answer at runtime, where a loaded document knows its file name but not its folder.
            Assert.AreEqual(typeof(GameplayTagData), VslDataStore.ResolveOwningType("WeaponTags", entries));
            Assert.AreEqual(typeof(GameplayTagData), VslDataStore.ResolveOwningType(null, entries));
        }

        [Test]
        public void EmptyDocumentFallsBackToASetNamedAfterTheType()
        {
            Assert.AreEqual(typeof(GameplayTagData), VslDataStore.ResolveOwningType("GameplayTagData.vsl", null));
            Assert.IsNull(VslDataStore.ResolveOwningType("WeaponTags", null));
        }

        #endregion
    }
}
