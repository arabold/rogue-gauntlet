#pragma warning disable GdUnit0501 // Pure MapData/algorithm invariants do not boot Godot.
namespace RogueGauntlet.Tests;

using System;
using System.Collections.Generic;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Sub-second plain-C# invariants for maze carving and isolated linear branch edges.</summary>
[TestSuite]
public class FloorAlgorithmTest
{
	private sealed class SplitMix
	{
		private ulong _state;
		public SplitMix(ulong seed) => _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

		public ulong Next()
		{
			ulong z = _state += 0x9E3779B97F4A7C15UL;
			z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
			z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
			return z ^ (z >> 31);
		}

		public int NextInt(int count) => count <= 1 ? 0 : (int)(Next() % (ulong)count);
		public float NextFloat() => (Next() >> 40) / (float)(1UL << 24);
	}

	[TestCase]
	public void AllPlayableMazeConnectsAllPlayableTilesHasDeadEndsAndPreservesProtectedCorridors()
	{
		var map = new MapData(20, 20);
		map.ResetToBorderedEmpty();

		// Stamp two spaced 3x3 rooms with single external doorways.
		StampBox(map, 2, 2, 3, 3, new Vector2I(4, 3), Vector2I.Right);
		StampBox(map, 14, 14, 3, 3, new Vector2I(14, 15), Vector2I.Left);

		// Also include a pre-built protected bridge tile between two tiles.
		var bridge = new Vector2I(10, 10);
		map.SetTile(bridge.X, bridge.Y, MapTile.Corridor);
		var protectedCorridors = new HashSet<Vector2I> { bridge };

		var prng = new SplitMix(42);
		MazeCorridorConnector.CarveAllPlayable(map, protectedCorridors, 0.7f, 0.05f, 0f, 6, prng.NextInt, prng.NextFloat);

		int emptyInterior = 0;
		int corridorCount = 0;
		int deadEnds = 0;
		for (int x = 1; x < map.Width - 1; x++)
		{
			for (int z = 1; z < map.Height - 1; z++)
			{
				if (map.IsEmpty(x, z)) emptyInterior++;
				if (map.IsCorridor(x, z))
				{
					corridorCount++;
					if (map.OpenNeighborCount(x, z) == 1) deadEnds++;
				}
			}
		}

		AssertInt(emptyInterior).IsEqual(0);
		AssertInt(deadEnds).IsGreater(0);
		foreach (var dir in MapData.CardinalOffsets)
		{
			AssertBool(map.IsCorridorEdgeSealed(bridge.X, bridge.Y, dir)).IsTrue();
			AssertBool(map.HasWallToward(bridge.X, bridge.Y, dir)).IsTrue();
		}

		var reached = AccessSolutionValidator.Reachable(map, new Vector2I(3, 3), new HashSet<Vector2I>());
		AssertBool(reached.Contains(new Vector2I(15, 15))).IsTrue();
		int reachableMazeTiles = 0;
		foreach (var t in reached)
		{
			if (map.IsCorridor(t.X, t.Y)) reachableMazeTiles++;
		}
		// Every non-protected carved tile should form one connected maze reachable from the rooms.
		AssertInt(reachableMazeTiles).IsEqual(corridorCount - 1);
	}

	[TestCase]
	public void AllPlayableMazeNeverCreates2x2OpenCorridors()
	{
		for (ulong seed = 1; seed <= 5; seed++)
		{
			var map = new MapData(16, 16);
			map.ResetToBorderedEmpty();
			var prng = new SplitMix(seed);
			MazeCorridorConnector.CarveAllPlayable(map, new HashSet<Vector2I>(), 0.7f, loopChance: 1.0f, 0f, 0, prng.NextInt, prng.NextFloat);

			for (int x = 1; x < map.Width - 2; x++)
			{
				for (int z = 1; z < map.Height - 2; z++)
				{
					bool allOpen = !map.IsCorridorEdgeSealed(x, z, Vector2I.Right)
						&& !map.IsCorridorEdgeSealed(x, z + 1, Vector2I.Right)
						&& !map.IsCorridorEdgeSealed(x, z, Vector2I.Down)
						&& !map.IsCorridorEdgeSealed(x + 1, z, Vector2I.Down);
					AssertBool(allOpen).OverrideFailureMessage($"seed {seed}: 2x2 open corridor quad at ({x},{z})").IsFalse();
				}
			}
		}
	}

	[TestCase]
	public void AllPlayableMazeDeadEndPruningPreservesDoorwaysAndMinDeadEnds()
	{
		var map = new MapData(16, 16);
		map.ResetToBorderedEmpty();
		StampBox(map, 2, 2, 3, 3, new Vector2I(4, 3), Vector2I.Right);
		StampBox(map, 11, 11, 3, 3, new Vector2I(11, 12), Vector2I.Left);

		var prng = new SplitMix(7);
		MazeCorridorConnector.CarveAllPlayable(map, new HashSet<Vector2I>(), 0.8f, 0.0f, deadEndPruneChance: 1.0f, minDeadEnds: 6, prng.NextInt, prng.NextFloat);

		AssertBool(map.IsCorridor(5, 3)).IsTrue();
		AssertBool(map.IsCorridor(10, 12)).IsTrue();

		int deadEnds = 0;
		for (int x = 1; x < map.Width - 1; x++)
		{
			for (int z = 1; z < map.Height - 1; z++)
			{
				if (map.IsCorridor(x, z) && map.OpenNeighborCount(x, z) == 1) deadEnds++;
			}
		}
		AssertInt(deadEnds).IsGreaterEqual(6);

		var reached = AccessSolutionValidator.Reachable(map, new Vector2I(3, 3), new HashSet<Vector2I>());
		AssertBool(reached.Contains(new Vector2I(12, 12))).IsTrue();
	}

	[TestCase]
	public void SealAdjacentForeignCorridorsIsolatesParallelBranches()
	{
		var map = new MapData(8, 8);
		map.ResetToBorderedEmpty();
		var edgeOwner = new int[8, 8];
		for (int x = 0; x < 8; x++)
		for (int z = 0; z < 8; z++)
			edgeOwner[x, z] = -1;

		// Protected bridge at (3, 1).
		map.SetTile(3, 1, MapTile.Corridor);
		edgeOwner[3, 1] = -2;

		// Edge 0 along z = 2.
		for (int x = 1; x <= 5; x++)
		{
			var t = new Vector2I(x, 2);
			map.SetTile(t.X, t.Y, MapTile.Corridor);
			edgeOwner[t.X, t.Y] = 0;
			AStarCorridorConnector.SealAdjacentForeignCorridors(map, edgeOwner, t, 0);
		}

		// Edge 1 parallel along z = 3.
		for (int x = 1; x <= 5; x++)
		{
			var t = new Vector2I(x, 3);
			map.SetTile(t.X, t.Y, MapTile.Corridor);
			edgeOwner[t.X, t.Y] = 1;
			AStarCorridorConnector.SealAdjacentForeignCorridors(map, edgeOwner, t, 1);
		}

		for (int x = 1; x <= 5; x++)
		{
			AssertBool(map.RequiresInteriorWall(x, 2, Vector2I.Down)).IsTrue();
			AssertBool(map.HasWallToward(x, 2, Vector2I.Down)).IsTrue();
		}
		AssertBool(map.IsCorridorEdgeSealed(3, 2, Vector2I.Up)).IsTrue();

		var reachedFromBranch0 = AccessSolutionValidator.Reachable(map, new Vector2I(1, 2), new HashSet<Vector2I>());
		AssertInt(reachedFromBranch0.Count).IsEqual(5);
		AssertBool(reachedFromBranch0.Contains(new Vector2I(1, 3))).IsFalse();
		AssertBool(reachedFromBranch0.Contains(new Vector2I(3, 1))).IsFalse();
	}

	[TestCase]
	public void AllPlayableMazeLeavesBorderTrappedEmptyNotchUntouchedWhenNoDoorwayReachesIt()
	{
		var map = new MapData(12, 12);
		map.ResetToBorderedEmpty();

		// Room seals off a border-trapped empty notch at (1, 1) while its sole doorway opens into (6, 3).
		for (int x = 1; x <= 5; x++)
		for (int z = 1; z <= 4; z++)
		{
			if (x <= 2 && z <= 2) continue; // Trapped empty pocket against (0, *) and (*, 0) border.
			map.SetTile(x, z, MapTile.Room);
		}
		map.SetConnector(5, 3, [Vector2I.Right]);

		var prng = new SplitMix(77);
		MazeCorridorConnector.CarveAllPlayable(map, new HashSet<Vector2I>(), 0.7f, 0.1f, 0f, 0, prng.NextInt, prng.NextFloat);

		AssertBool(map.IsEmpty(1, 1)).IsTrue();
		AssertBool(map.IsEmpty(2, 2)).IsTrue();
		AssertBool(map.IsCorridor(6, 3)).IsTrue();
		var reached = AccessSolutionValidator.Reachable(map, new Vector2I(4, 3), new HashSet<Vector2I>());
		AssertBool(reached.Contains(new Vector2I(6, 3))).IsTrue();
	}

	private static void StampBox(MapData map, int x0, int z0, int w, int h, Vector2I connector, Vector2I connDir)
	{
		for (int x = x0; x < x0 + w; x++)
		for (int z = z0; z < z0 + h; z++)
			map.SetTile(x, z, MapTile.Room);

		map.SetConnector(connector.X, connector.Y, [connDir]);
	}
}
