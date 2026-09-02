using System;
using System.Collections.Generic;
using NUnit.Framework;
using Vapor.GameplayTags;
using Vapor.Serialization;

namespace Vapor.Tests.Registry
{
    /// <summary>
    /// What a save costs, expressed as behaviour: one document's worth of files rewritten, one
    /// document's worth of keys exchanged, and nothing else touched.
    /// </summary>
    /// <remarks>
    /// These are the invariants the incremental save path rests on. Break any of them and the symptom
    /// is not a failure but a slow creep back to rebuilding everything, which is exactly what is hard
    /// to notice.
    /// </remarks>
    public class DataRegistrySaveTests
    {
        /// <summary>
        /// A stand-in owner, so these tests never replace a real document.
        /// </summary>
        /// <remarks>
        /// <see cref="GlobalDataRegistry.ReplaceDocument"/> keys on whatever type it is handed, and
        /// naming a real one here would drop that document's entries out of the shared registry for
        /// every test that runs afterwards.
        /// </remarks>
        private sealed class TestDocument
        {
        }

        private static GameplayTagData Tag(string name) => new GameplayTagData(name);

        [TearDown]
        public void ClearTestDocument() =>
            GlobalDataRegistry.ReplaceDocument(typeof(TestDocument), Array.Empty<IData>());

        #region Replacing a document

        [Test]
        public void ReplacingADocumentDropsTheKeysItUsedToContribute()
        {
            GlobalDataRegistry.ReplaceDocument(typeof(TestDocument), new List<IData> { Tag("Vapor.Test.Save.First") });
            Assert.IsNotNull(GlobalDataRegistry.Get("Vapor.Test.Save.First"));

            GlobalDataRegistry.ReplaceDocument(typeof(TestDocument), new List<IData> { Tag("Vapor.Test.Save.Second") });

            // The deleted entry has to leave. Without the per-document key record there is no way to
            // tell it from a key that was never in this file, and the only safe answer is a full rebuild.
            Assert.IsNull(GlobalDataRegistry.Get("Vapor.Test.Save.First"),
                "a key the document no longer contains was left in the registry");
            Assert.IsNotNull(GlobalDataRegistry.Get("Vapor.Test.Save.Second"));
        }

        [Test]
        public void ReplacingADocumentLeavesEveryOtherSourceAlone()
        {
            // Stands in for a code registry or an addressable asset: registered outside any document.
            var external = Tag("Vapor.Test.Save.External");
            GlobalDataRegistry.Register(external);

            try
            {
                GlobalDataRegistry.ReplaceDocument(typeof(TestDocument), new List<IData> { Tag("Vapor.Test.Save.Owned") });

                Assert.AreSame(external, GlobalDataRegistry.Get("Vapor.Test.Save.External"),
                    "replacing one document disturbed a key that did not come from a document at all");
            }
            finally
            {
                GlobalDataRegistry.Unregister(external.Key);
            }
        }

        [Test]
        public void ReplacingADocumentDoesNotTriggerAFullRebuild()
        {
            var built = 0;
            var changed = 0;
            void OnBuilt() => built++;
            void OnChanged() => changed++;

            GlobalDataRegistry.OnRegistriesBuilt += OnBuilt;
            GlobalDataRegistry.OnRegistryChanged += OnChanged;
            try
            {
                GlobalDataRegistry.ReplaceDocument(typeof(TestDocument), new List<IData> { Tag("Vapor.Test.Save.Signal") });

                // The whole point of the split: a save must not ask every closed DataRegistry<T> to
                // rescan the entire map, but it must tell the lazy caches to drop what they have.
                Assert.AreEqual(0, built, "saving one document asked every typed registry to rescan the whole map");
                Assert.AreEqual(1, changed, "the lazily built caches were not told the registry moved");
            }
            finally
            {
                GlobalDataRegistry.OnRegistriesBuilt -= OnBuilt;
                GlobalDataRegistry.OnRegistryChanged -= OnChanged;
            }
        }

        [Test]
        public void ATypedRegistryFollowsADocumentBeingReplaced()
        {
            var first = Tag("Vapor.Test.Save.Typed");
            GlobalDataRegistry.ReplaceDocument(typeof(TestDocument), new List<IData> { first });

            Assert.AreSame(first, DataRegistry<GameplayTagData>.Get("Vapor.Test.Save.Typed"));

            GlobalDataRegistry.ReplaceDocument(typeof(TestDocument), Array.Empty<IData>());

            // Nothing rescans on this path, so the typed map is only correct if it follows the
            // unregister event. This is the test that fails if OnDataUnregistered stops being raised.
            Assert.IsNull(DataRegistry<GameplayTagData>.Get("Vapor.Test.Save.Typed"),
                "the typed registry answered with a key the global registry had already dropped");
        }

        [Test]
        public void RenamingAnEntryRetiresTheOldKeyAndPublishesTheNew()
        {
            GlobalDataRegistry.ReplaceDocument(typeof(TestDocument), new List<IData> { Tag("Vapor.Test.Save.OldName") });
            GlobalDataRegistry.ReplaceDocument(typeof(TestDocument), new List<IData> { Tag("Vapor.Test.Save.NewName") });

            // A rename is a key change, which through this path is a drop and an add rather than an
            // edit - so the stale key must not still resolve.
            Assert.IsNull(GlobalDataRegistry.Get("Vapor.Test.Save.OldName"));
            Assert.IsNotNull(GlobalDataRegistry.Get("Vapor.Test.Save.NewName"));
        }

        [Test]
        public void DocumentKeysNameOnlyWhatCameFromADocument()
        {
            var external = Tag("Vapor.Test.Save.NotADocument");
            GlobalDataRegistry.Register(external);

            try
            {
                var owned = Tag("Vapor.Test.Save.FromADocument");
                GlobalDataRegistry.ReplaceDocument(typeof(TestDocument), new List<IData> { owned });

                var documentKeys = new HashSet<uint>(GlobalDataRegistry.GetDocumentKeys());

                // This is what the windows subtract to find the entries they cannot edit. Getting it
                // from the registry is what let the save stop re-reading every file to work it out.
                Assert.IsTrue(documentKeys.Contains(owned.Key));
                Assert.IsFalse(documentKeys.Contains(external.Key),
                    "a key registered outside any document was reported as belonging to one");
            }
            finally
            {
                GlobalDataRegistry.Unregister(external.Key);
            }
        }

        #endregion

        #region Type tags

        /// <summary>A throwaway type to register a tag against, so no real type's tag is disturbed.</summary>
        private sealed class ProbeTagType
        {
        }

        [Test]
        public void ADataTypeWithNoTagAttributeEarnsItsOwnShortName()
        {
            // Tag resolution is backed by a short-name index rather than a scan of every type in every
            // loaded assembly; that the index gets built at all is asserted in the VSL package, whose
            // tests can see the counter. What matters here is that a Core data type resolves through
            // it - no [VslType] on GameplayTagData, so the short name has to be earned.
            Assert.AreEqual(nameof(GameplayTagData), VslTypeRegistry.GetTag(typeof(GameplayTagData), typeof(IData)));
        }

        [Test]
        public void RegisteringATagInvalidatesWhatTheWriterWorkedOut()
        {
            // Writing a '!tag' for a type with no [VslType] means proving its short name is unique in
            // the slot, which walks every type in every loaded assembly. That answer is cached, and the
            // cache has to be dropped when a registration changes what the answer should be - the read
            // side has always done this, and the write side silently did not.
            Assert.AreEqual(nameof(ProbeTagType), VslTypeRegistry.GetTag(typeof(ProbeTagType), null));

            VslTypeRegistry.Register("ProbeTagAlias", typeof(ProbeTagType));

            Assert.AreEqual("ProbeTagAlias", VslTypeRegistry.GetTag(typeof(ProbeTagType), null),
                "the writer answered from a cache that registering a tag should have emptied");
        }

        #endregion

        #region Shards

        [Test]
        public void TheFirstShardIsNamedExactlyAsTheDocument()
        {
            // Shard zero keeps the plain name, which is what makes sharding invisible to a project that
            // never outgrows one file - same path, same addressable address, same diff.
            Assert.AreEqual(nameof(GameplayTagData), VslDataStore.GetShardName(typeof(GameplayTagData), 0));
            Assert.AreEqual(VslDataStore.GetAssetPath(typeof(GameplayTagData)),
                VslDataStore.GetShardAssetPath(VslDataStore.GetShardName(typeof(GameplayTagData), 0)));
        }

        [Test]
        public void LaterShardsAreNumberedAfterTheDocument()
        {
            Assert.AreEqual("GameplayTagData.1", VslDataStore.GetShardName(typeof(GameplayTagData), 1));
            Assert.AreEqual("Assets/Vapor/Data/GameplayTagData.1.vsl",
                VslDataStore.GetShardAssetPath("GameplayTagData.1"));
        }

        [Test]
        public void ANumberedShardResolvesToTheTypeThatOwnsIt()
        {
            // The runtime path reads documents by label and has only the file name to go on when one is
            // empty, so a numbered shard has to be recognisable as part of its document.
            Assert.AreEqual(typeof(GameplayTagData), VslDataStore.ResolveOwningType("GameplayTagData.1.vsl", null));
            Assert.AreEqual(typeof(GameplayTagData), VslDataStore.ResolveOwningType("GameplayTagData.12", null));
        }

        [Test]
        public void ASuffixThatIsNotAShardNumberClaimsNothing()
        {
            // Shard numbers start at one, and a trailing word is just part of a freely chosen name.
            Assert.IsNull(VslDataStore.ResolveOwningType("GameplayTagData.0", null));
            Assert.IsNull(VslDataStore.ResolveOwningType("GameplayTagData.Weapons", null));
        }

        [Test]
        public void RebalancePacksInDocumentOrderAndRecordsWhereEachEntryLanded()
        {
            var entries = new List<IData>();
            for (var i = 0; i < 32; i++)
            {
                entries.Add(Tag($"Vapor.Test.Shard.{i}"));
            }

            var shardOf = new Dictionary<IData, string>();
            var plan = VslDataStore.RebalanceShards(typeof(GameplayTagData), entries, shardOf);

            // Well under the size limit, so one file holds them all and the order is the document's.
            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual(VslDataStore.GetShardName(typeof(GameplayTagData), 0), plan[0].Shard);
            CollectionAssert.AreEqual(entries, plan[0].Entries);

            Assert.AreEqual(entries.Count, shardOf.Count,
                "an entry was packed without its assignment being recorded, so the next save would move it");
        }

        [Test]
        public void AnEntryIsNeverSplitAcrossTwoShards()
        {
            // The size limit is soft by design: one entry, one file, whatever it weighs. A hard limit
            // would have to cut an entry in half, which no reader could put back together.
            var entries = new List<IData> { Tag(new string('x', VslDataStore.ShardSizeLimit + 1024)) };

            var plan = VslDataStore.RebalanceShards(typeof(GameplayTagData), entries, null);

            var placements = 0;
            foreach (var (_, members) in plan)
            {
                placements += members.Count;
            }

            Assert.AreEqual(1, placements);
        }

        #endregion
    }
}
