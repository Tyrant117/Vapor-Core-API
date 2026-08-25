using NUnit.Framework;
using UnityEngine;
using Vapor.Networking;

namespace Vapor.Tests.Networking
{
    /// <summary>
    /// Positions that keep their precision however far out they are, and an origin that moves under them.
    /// </summary>
    /// <remarks>
    /// The arithmetic underneath a hundred-kilometre play space. Most of these are small, and the two
    /// that matter most are the ones about <i>precision at range</i> and <i>agreement between peers whose
    /// origins differ</i> — everything else in the milestone rests on those two being true.
    /// </remarks>
    public class UniversePositionTests
    {
        [TearDown]
        public void TearDown() => UniverseOrigin.Reset();

        #region - Normalization -

        [Test]
        public void AnOffsetIsAlwaysInsideItsSector()
        {
            var position = UniversePosition.Create(Vector3Int.zero, new Vector3(2500f, -30f, 1024f));

            Assert.AreEqual(new Vector3Int(2, -1, 1), position.Sector);
            Assert.That(position.Local.x, Is.EqualTo(452f).Within(1e-3f));
            Assert.That(position.Local.y, Is.EqualTo(994f).Within(1e-3f), "a metre below zero is the last metre of the sector below");
            Assert.That(position.Local.z, Is.EqualTo(0f).Within(1e-3f), "exactly a sector along is the start of the next one");
        }

        [Test]
        public void NegativeOffsetsCarryDownwardsRatherThanTowardsZero()
        {
            // The one arithmetic mistake that would be invisible near the origin and wrong everywhere
            // else: truncation instead of floor puts the sector below zero one metre in the wrong place.
            var position = UniversePosition.FromMetres(-1f, -1025f, -0.5);

            Assert.AreEqual(new Vector3Int(-1, -2, -1), position.Sector);
            Assert.That(position.Local.x, Is.EqualTo(1023f).Within(1e-3f));
            Assert.That(position.Local.y, Is.EqualTo(1023f).Within(1e-3f));
            Assert.That(position.Local.z, Is.EqualTo(1023.5f).Within(1e-3f));
        }

        [Test]
        public void AddingAnOffsetCarriesAcrossSectors()
        {
            var position = UniversePosition.FromMetres(1000f, 0f, 0f);
            var moved = position + new Vector3(100f, 0f, 0f);

            Assert.AreEqual(new Vector3Int(1, 0, 0), moved.Sector);
            Assert.That(moved.Local.x, Is.EqualTo(76f).Within(1e-3f));
            Assert.That(moved.DistanceTo(position), Is.EqualTo(100.0).Within(1e-3));
        }

        [Test]
        public void EqualityIsExactRatherThanApproximate()
        {
            var a = UniversePosition.FromMetres(2048f, 0f, 0f);
            var b = UniversePosition.Create(new Vector3Int(1, 0, 0), new Vector3(1024f, 0f, 0f));

            Assert.AreEqual(a, b, "the same place, spelled two ways, normalizes to one");
            Assert.IsTrue(a == b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        #endregion

        #region - Precision -

        [Test]
        public void PrecisionDoesNotDegradeWithDistance()
        {
            // The reason the type exists, and the claim is specifically that the error is *flat*, not
            // that it is zero. A float inside a 1024 m sector has a step of about 6 × 10⁻⁵ m, so a
            // millimetre of movement is recorded to within that — at the origin, at eighty kilometres,
            // and at eighty thousand. A plain float world's error grows with the range instead: 1.2 cm
            // at 80 km, and it never comes back.
            const double sectorUlp = 1024.0 * 1.2e-7;   // a float's step at the top of a sector

            double worst = 0;
            foreach (double range in new[] { 0.0, 80_000.0, 8_000_000.0 })
            {
                var at = UniversePosition.FromMetres(range, 0f, 0f);
                var nudged = at + new Vector3(0.001f, 0f, 0f);

                double error = System.Math.Abs(nudged.DistanceTo(at) - 0.001);
                worst = System.Math.Max(worst, error);

                Assert.That(error, Is.LessThan(sectorUlp),
                    $"a millimetre {range / 1000:F0} km out is off by {error:E2} m, more than a float inside a sector can explain");
            }

            Assert.That(worst, Is.LessThan(1e-4), $"and the worst case anywhere is {worst:E2} m");
        }

        [Test]
        public void AFloatWorldLosesTheSameMillimetreThatThisKeeps()
        {
            // Not a test of our code — a test of the premise, so that the premise is written down
            // somewhere that fails if it ever stops being true.
            const float atRange = 80_000f;
            float nudged = atRange + 0.001f;

            Assert.AreEqual(atRange, nudged,
                "a float at 80 km cannot represent a millimetre of movement at all, which is why positions are split");
        }

        [Test]
        public void DistanceIsExactAcrossTheWholePlaySpace()
        {
            var here = UniversePosition.FromMetres(0f, 0f, 0f);
            var farBelt = UniversePosition.FromMetres(80_000f, 0f, 0f);

            Assert.That(here.DistanceTo(farBelt), Is.EqualTo(80_000.0).Within(1e-3), "the gate's distance");
            Assert.That(farBelt.DistanceTo(here), Is.EqualTo(80_000.0).Within(1e-3), "and it is symmetric");
        }

        #endregion

        #region - Render space -

        [Test]
        public void RenderSpaceIsMeasuredFromWhicheverOriginYouAsk()
        {
            var thing = UniversePosition.FromMetres(80_000f, 0f, 0f);
            var nearIt = UniversePosition.FromMetres(79_000f, 0f, 0f);

            Assert.That(thing.ToRender(UniversePosition.Zero).x, Is.EqualTo(80_000f).Within(1f));
            Assert.That(thing.ToRender(nearIt).x, Is.EqualTo(1000f).Within(1e-2f),
                "and a peer standing near it renders it a kilometre away, in full float precision");
        }

        [Test]
        public void TwoPeersWithDifferentOriginsAgreeOnWhereThingsAre()
        {
            // The claim per-client rebasing rests on. The wire carries universe positions, so two peers
            // that have never agreed on where zero is still agree on where everything is relative to
            // everything else.
            var alice = UniversePosition.FromMetres(0f, 0f, 0f);
            var bob = UniversePosition.FromMetres(80_000f, 0f, 0f);

            var ship = UniversePosition.FromMetres(40_000f, 100f, 0f);
            var passenger = ship + new Vector3(0f, 0f, 3f);

            var shipForAlice = ship.ToRender(alice);
            var shipForBob = ship.ToRender(bob);
            Assert.AreNotEqual(shipForAlice, shipForBob, "they render it in different places, as they should");

            var passengerForAlice = passenger.ToRender(alice);
            var passengerForBob = passenger.ToRender(bob);

            Assert.That(Vector3.Distance(passengerForAlice - shipForAlice, passengerForBob - shipForBob),
                Is.LessThan(0.01f), "and both put the passenger in the same place on the deck");
        }

        [Test]
        public void RoundTrippingThroughRenderSpaceReturnsTheSamePlace()
        {
            var origin = UniversePosition.FromMetres(12_345f, -678f, 90_123f);
            var thing = origin + new Vector3(37.5f, -2.25f, 400f);

            var render = thing.ToRender(origin);
            var back = UniversePosition.FromRender(origin, render);

            Assert.That(back.DistanceTo(thing), Is.LessThan(1e-3), "there and back is where you started");
        }

        #endregion

        #region - The origin -

        [Test]
        public void ShiftingTheOriginSaysHowFarRenderSpaceMoved()
        {
            var thing = UniversePosition.FromMetres(5000f, 0f, 0f);
            var before = thing.ToRender(UniverseOrigin.Current);

            var delta = UniverseOrigin.ShiftTo(UniversePosition.FromMetres(4096f, 0f, 0f));

            var after = thing.ToRender(UniverseOrigin.Current);
            Assert.That(Vector3.Distance(before + delta, after), Is.LessThan(1e-2f),
                "adding the delta to a held render position keeps it where it was — which is the whole contract");
        }

        [Test]
        public void ShiftingAnnouncesItselfExactlyOnce()
        {
            int shifts = 0;
            Vector3 announced = Vector3.zero;
            UniverseOrigin.Shifted += d => { shifts++; announced = d; };

            var delta = UniverseOrigin.ShiftTo(UniversePosition.FromMetres(2048f, 0f, 0f));

            Assert.AreEqual(1, shifts);
            Assert.AreEqual(delta, announced);
            Assert.That(announced.x, Is.EqualTo(-2048f).Within(1e-2f), "everything held in render space moves the other way");
        }

        [Test]
        public void ShiftingToWhereItAlreadyIsAnnouncesNothing()
        {
            int shifts = 0;
            UniverseOrigin.Shifted += _ => shifts++;

            var delta = UniverseOrigin.ShiftTo(UniverseOrigin.Current);

            Assert.AreEqual(0, shifts, "a rebase that moves nothing is not a rebase");
            Assert.AreEqual(Vector3.zero, delta);
        }

        [Test]
        public void TheOriginIsAlwaysSnappedToASectorCorner()
        {
            // So origins come from a fixed lattice: two peers near each other usually share one exactly,
            // and a rebase is reproducible rather than depending on where a ship happened to be when it
            // crossed the threshold.
            var target = UniverseOrigin.RebaseTargetFor(new Vector3(1600f, 30f, -2500f));

            Assert.AreEqual(Vector3.zero, target.Local);
            Assert.AreEqual(new Vector3Int(1, 0, -3), target.Sector);
        }

        [Test]
        public void RebasingWaitsUntilTheFocusHasActuallyDriftedAway()
        {
            Assert.IsFalse(UniverseOrigin.ShouldRebase(new Vector3(1000f, 0f, 0f), 1500f),
                "hovering near a sector edge must not rebase every frame");
            Assert.IsTrue(UniverseOrigin.ShouldRebase(new Vector3(1600f, 0f, 0f), 1500f));
        }

        [Test]
        public void FollowingAFocusAcrossTheGatesDistanceKeepsItNearZero()
        {
            // The soak, as arithmetic. Eighty kilometres in three-hundred-metre steps, rebasing when the
            // focus drifts — the focus must never be far enough from the origin for a float to care.
            var focus = Vector3.zero;
            float worst = 0f;
            int rebases = 0;

            for (int step = 0; step < 400; step++)
            {
                focus += new Vector3(200f, 0f, 0f);

                if (UniverseOrigin.ShouldRebase(focus, 1500f))
                {
                    var delta = UniverseOrigin.ShiftTo(UniverseOrigin.RebaseTargetFor(focus));
                    focus += delta;
                    rebases++;
                }

                worst = Mathf.Max(worst, focus.magnitude);
            }

            var travelled = UniverseOrigin.Current + focus;

            Assert.That(rebases, Is.GreaterThan(50), $"eighty kilometres is a lot of rebases ({rebases})");
            Assert.That(worst, Is.LessThan(2000f),
                $"and the focus never got further than {worst:F0} m from the origin, so float precision never mattered");
            Assert.That(travelled.DistanceTo(UniversePosition.Zero), Is.EqualTo(80_000.0).Within(1.0),
                "while actually having gone eighty kilometres");
        }

        #endregion

        #region - Wire -

        [Test]
        public void APositionSurvivesTheWireAtAnyRange()
        {
            foreach (double range in new[] { 0.0, 1500.0, 80_000.0, 1_000_000.0 })
            {
                var sent = UniversePosition.FromMetres(range, -range * 0.5, range * 0.25) + new Vector3(0.37f, 1.5f, -2.25f);

                var writer = new NetworkWriter();
                sent.Write(writer, 0.01f);
                var received = UniversePosition.Read(new NetworkReader(writer.ToArray()), 0.01f);

                Assert.That(received.DistanceTo(sent), Is.LessThan(0.02),
                    $"a centimetre of precision is a centimetre at {range / 1000:F0} km");
            }
        }

        [Test]
        public void NearTheOriginAPositionCostsAlmostNothing()
        {
            // The bandwidth argument. Three varints that are one byte each while the sectors are small,
            // plus an offset the transform was already paying for.
            var writer = new NetworkWriter();
            UniversePosition.FromMetres(12f, 3f, 40f).Write(writer, 0.01f);

            Assert.That(writer.ToArray().Length, Is.LessThanOrEqualTo(10),
                $"a nearby position is {writer.ToArray().Length} bytes");
        }

        #endregion
    }
}
