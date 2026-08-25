using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Vapor.Networking;

namespace Vapor.Tests.Networking
{
    /// <summary>
    /// The interest grid in three dimensions, and the plane it used to be still behaving.
    /// </summary>
    /// <remarks>
    /// It was an XZ grid, which is right for a game on a surface and wrong for one in a belt: something
    /// a kilometre directly above a player was as relevant as something in front of them. The change is
    /// small — a third axis in the cell key — and these are the two things that have to stay true:
    /// height is now culled, and a world that never leaves a plane is unaffected.
    /// </remarks>
    public class SpatialInterestGrid3DTests
    {
        private const float Cell = 32f;

        private static SpatialInterestGrid Grid(int radius = 2, int hysteresis = 0) =>
            new(Cell, radius, hysteresis);

        [Test]
        public void HeightIsCulledLikeAnyOtherAxis()
        {
            var grid = Grid();
            grid.SetFocus(clientId: 1, Vector3.zero);

            grid.SetPosition(10, new Vector3(0f, 40f, 0f));      // two cells up: inside the radius
            grid.SetPosition(11, new Vector3(0f, 4000f, 0f));    // a hundred and twenty cells up

            var near = new List<ulong>();
            grid.CollectNear(1, radiusCells: 2, near);

            CollectionAssert.Contains(near, 10UL, "just overhead is near");
            CollectionAssert.DoesNotContain(near, 11UL, "four kilometres straight up is not, which an XZ grid could not tell you");
        }

        [Test]
        public void AWorldOnAPlaneBehavesExactlyAsItDid()
        {
            // The compatibility claim. Everything at the same height lands in the same Y cell, so the Y
            // comparison is between two identical numbers and can never exclude anything.
            var grid = Grid();
            grid.SetFocus(clientId: 1, new Vector3(0f, 1.5f, 0f));

            for (int i = 0; i < 20; i++)
            {
                grid.SetPosition((ulong)i, new Vector3(i * 8f, 1.5f, i * 8f));
            }

            var near = new List<ulong>();
            grid.CollectNear(1, radiusCells: 2, near);

            // Cells are 32 m and the radius is two, so everything out to about 80 m diagonally.
            Assert.That(near.Count, Is.GreaterThan(5), "the plane is still populated");
            CollectionAssert.Contains(near, 0UL);
            CollectionAssert.DoesNotContain(near, 19UL, "and still culled at range");
        }

        [Test]
        public void CellsBelowTheOriginAreDistinctFromCellsAboveIt()
        {
            // The packing gives each axis twenty-one bits, so each has to be sign-extended back out of
            // them. Get that wrong and a cell at -1 collides with one at +2,097,151 — which is a bug that
            // only ever appears on the negative side of the world.
            var grid = Grid(radius: 0);
            grid.SetFocus(clientId: 1, new Vector3(0f, -40f, 0f));

            grid.SetPosition(10, new Vector3(0f, -40f, 0f));
            grid.SetPosition(11, new Vector3(0f, 40f, 0f));

            var near = new List<ulong>();
            grid.CollectNear(1, radiusCells: 0, near);

            CollectionAssert.Contains(near, 10UL, "the object below the origin is in the focus's own cell");
            CollectionAssert.DoesNotContain(near, 11UL, "and the one above it is not");
        }

        [Test]
        public void TheGridReachesFurtherThanThePlaySpaceNeeds()
        {
            // ±1,048,576 cells at 32 m is ±33,000 km, four hundred times the milestone's hundred.
            var grid = Grid(radius: 1);
            var faraway = new Vector3(1_000_000f, -500_000f, 2_000_000f);

            grid.SetFocus(clientId: 1, faraway);
            grid.SetPosition(10, faraway);
            grid.SetPosition(11, Vector3.zero);

            var near = new List<ulong>();
            grid.CollectNear(1, radiusCells: 1, near);

            CollectionAssert.Contains(near, 10UL, "two thousand kilometres out, an object beside the focus is still beside it");
            CollectionAssert.DoesNotContain(near, 11UL, "and the origin is not");
        }

        [Test]
        public void DistanceIsStillEuclideanAndIncludesHeight()
        {
            var grid = Grid();
            grid.SetFocus(clientId: 1, Vector3.zero);
            grid.SetPosition(10, new Vector3(3f, 4f, 0f));

            Assert.That(grid.Distance(10, 1), Is.EqualTo(5f).Within(1e-3f), "LOD distance was never planar");
        }
    }
}
