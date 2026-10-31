using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Reusable furnishing operations. They measure assigned pieces, use room-local coordinates and preserve actor passage.</summary>
public static class RoomFurnishingPatterns
{
	private sealed record BedPieces(RoomArrangementDefinition Bed, RoomArrangementDefinition Box,
		RoomArrangementDefinition Sideboard, Vector3 BoxOffset, Vector3 SideboardOffset);

	private static BedPieces SelectBed(RoomFurnishingContext context, BedClusterDefinition cluster, string key)
	{
		bool Roll(string part, float chance) { using var rng = context.Random(key + ":" + part); return rng.Randf() < chance; }
		float side = Roll("mirror", cluster.MirrorChance) ? -1 : 1;
		Vector3 Offset(Vector3 value) => new(value.X * side, value.Y, value.Z);
		return new(context.Variants(cluster.Bed, key + ":bed")[0],
			cluster.Box != null && Roll("box", cluster.BoxChance) ? context.Variants(cluster.Box, key + ":box:variant")[0] : null,
			cluster.Sideboard != null && Roll("sideboard", cluster.SideboardChance) ? context.Variants(cluster.Sideboard, key + ":sideboard:variant")[0] : null,
			Offset(cluster.BoxOffset), Offset(cluster.SideboardOffset));
	}

	/// <summary>Compose an independently varied bed and optional companions. Unsafe optional pieces are omitted.</summary>
	public static bool PlaceBed(RoomFurnishingContext context, BedClusterDefinition cluster, Transform3D transform, string key) =>
		PlaceBed(context, cluster, SelectBed(context, cluster, key), transform, key);

	private static bool PlaceBed(RoomFurnishingContext context, BedClusterDefinition cluster, BedPieces pieces, Transform3D transform, string key)
	{
		RoomFurnishingContext trial = null;
		RoomArrangementPlacement bed = null;
		// Validate a complete cluster before its optional pieces are removed: a companion can fill
		// a narrow pocket left by the bed alone. Try smaller subsets only when the whole group fails.
		foreach (int companions in new[] { 3, 1, 2, 0 })
		{
			var candidate = context.Fork();
			if (!candidate.TryPlace(pieces.Bed, transform, key + ":bed", out bed, checkPassage: false)) return false;
			if (pieces.Box != null && (companions & 1) != 0)
				foreach (var variant in candidate.Variants(cluster.Box, key + ":box:variant"))
					if (candidate.TryPlace(variant, bed.Transform * new Transform3D(Basis.Identity, pieces.BoxOffset), key + ":box", out _, checkPassage: false)) break;
			if (pieces.Sideboard != null && (companions & 2) != 0)
				foreach (var variant in candidate.Variants(cluster.Sideboard, key + ":sideboard:variant"))
					if (candidate.TryPlace(variant, bed.Transform * new Transform3D(Basis.Identity, pieces.SideboardOffset), key + ":sideboard", out _, checkPassage: false)) break;
			if (candidate.IsConnected) { trial = candidate; break; }
		}
		if (trial == null) return false;
		using var rng = context.Random(key + ":decoration");
		if (cluster.WallDecorations.Length > 0 && rng.Randf() < cluster.DecorationChance)
		{
			// Choose among all assigned fixture styles without repeating a used style while another can fit.
			var decorations = cluster.WallDecorations.SelectMany(entry => entry.Variants).DistinctBy(entry => entry.Id).ToArray();
			foreach (var decoration in trial.OrderVariants(decorations, key + ":decoration:variant"))
				if (trial.TryPlace(decoration, bed.Transform, key + ":decoration", out _)) break;
		}
		context.Commit(trial);
		return true;
	}

	/// <summary>Measure each chosen cluster and fill a wall's length, accounting for its orientation and optional companions.</summary>
	public static int PlaceBedsAlongWall(RoomFurnishingContext context, BedClusterDefinition cluster,
		RoomWallSpan wall, float spacing, float endClearance, int maximum, string key)
	{
		int placed = 0, index = 0;
		float offset = endClearance;
		var basis = new Basis(Vector3.Up, Turn(wall.Outward) * Mathf.Pi / 2);
		var tangent = new Vector3(wall.Along.X, 0, wall.Along.Y);
		var origin = context.Center(wall.Start) - tangent * (Room.TileSize / 2f);
		while (placed < maximum)
		{
			string id = $"{key}:{index++}";
			var pieces = SelectBed(context, cluster, id);
			var bounds = context.Bounds(pieces.Bed);
			if (pieces.Box != null) bounds = bounds.Merge(new Aabb(context.Bounds(pieces.Box).Position + pieces.BoxOffset, context.Bounds(pieces.Box).Size));
			if (pieces.Sideboard != null) bounds = bounds.Merge(new Aabb(context.Bounds(pieces.Sideboard).Position + pieces.SideboardOffset, context.Bounds(pieces.Sideboard).Size));
			var rotated = new Transform3D(basis, Vector3.Zero) * bounds;
			float extent = rotated.Size.Dot(tangent);
			if (offset + extent > wall.LengthTiles * Room.TileSize - endClearance + .001f) break;
			var transform = new Transform3D(basis, origin + tangent * (offset - rotated.Position.Dot(tangent)));
			if (PlaceBed(context, cluster, pieces, transform, id)) { placed++; offset += extent + spacing; }
			// Wall trim and corner pillars can interrupt an otherwise continuous backing span.
			// Advance half a geometry cell after rejection instead of losing an entire cluster-width bay.
			else offset += .5f;
		}
		return placed;
	}

	/// <summary>Compose an independently varied primary and local companions; validate the complete group before committing slots.</summary>
	public static bool PlaceAssembly(RoomFurnishingContext context, RoomAssemblyDefinition assembly, Transform3D transform, string key)
	{
		using var mirror = context.Random(key + ":mirror");
		float side = mirror.Randf() < assembly.MirrorChance ? -1 : 1;
		foreach (var primary in context.Variants(assembly.Primary, key + ":primary"))
		{
			var trial = context.Fork();
			if (!trial.TryPlace(primary, transform, key + ":primary", out var anchor, checkPassage: false)) continue;
			void AddCompanions(RoomFurnishingContext candidate, Transform3D primaryTransform, bool checkPassage)
			{
				foreach (var part in assembly.Parts)
				{
					using var chance = context.Random(key + ":" + part.Id);
					if (chance.Randf() >= part.Chance) continue;
					var offset = new Vector3(part.Offset.X * side, part.Offset.Y, part.Offset.Z);
					foreach (var variant in candidate.Variants(part.Arrangement, key + ":" + part.Id + ":variant"))
						if (candidate.TryPlace(variant, primaryTransform * new Transform3D(new Basis(Vector3.Up, part.QuarterTurns * side * Mathf.Pi / 2), offset),
							key + ":" + part.Id, out _, checkPassage)) break;
				}
			}
			AddCompanions(trial, anchor.Transform, false);
			if (!trial.IsConnected)
			{
				// Keep a safe primary when an optional chair/supply would seal a narrow approach.
				trial = context.Fork();
				if (!trial.TryPlace(primary, transform, key + ":primary", out anchor)) continue;
				AddCompanions(trial, anchor.Transform, true);
			}
			context.Commit(trial);
			return true;
		}
		return false;
	}

	public static int PlaceShelvesAlongWall(RoomFurnishingContext context, RoomArrangementDefinition shelf,
		RoomWallSpan wall, float spacing, float endClearance, int maximum, string key)
	{
		int placed = 0, index = 0;
		foreach (var transform in AlongWall(context, shelf.Variants.Select(context.Bounds).Aggregate((a, b) => a.Merge(b)), wall, spacing, endClearance))
		{
			if (placed >= maximum) break;
			string id = $"{key}:{index++}";
			foreach (var variant in context.Variants(shelf, id))
				if (context.TryPlace(variant, transform, id, out _)) { placed++; break; }
		}
		return placed;
	}

	private static IEnumerable<Transform3D> AlongWall(RoomFurnishingContext context, Aabb bounds,
		RoomWallSpan wall, float spacing, float endClearance)
	{
		var basis = new Basis(Vector3.Up, Turn(wall.Outward) * Mathf.Pi / 2);
		var tangent = new Vector3(wall.Along.X, 0, wall.Along.Y);
		var rotated = new Transform3D(basis, Vector3.Zero) * bounds;
		float start = rotated.Position.Dot(tangent), extent = rotated.Size.Dot(tangent);
		var origin = context.Center(wall.Start) - tangent * (Room.TileSize / 2f);
		for (float offset = endClearance; offset + extent <= wall.LengthTiles * Room.TileSize - endClearance + .001f; offset += extent + spacing)
			yield return new(basis, origin + tangent * (offset - start));
	}

	/// <summary>Place any arrangement in evenly spaced rows/columns; counts and clear circulation remain the caller's policy.</summary>
	public static int PlaceRows(RoomFurnishingContext context, RoomArrangementDefinition definition, Vector3 origin,
		int columns, int rows, float columnSpacing, float rowSpacing, int turn, string key)
	{
		int placed = 0;
		var trial = context.Fork();
		var basis = new Basis(Vector3.Up, turn * Mathf.Pi / 2);
		for (int row = 0; row < rows; row++)
		for (int column = 0; column < columns; column++)
		{
			string id = $"{key}:{column}:{row}";
			foreach (var variant in trial.Variants(definition, id))
				if (trial.TryPlace(variant, new(basis, origin + basis * new Vector3(column * columnSpacing, 0, row * rowSpacing)),
					id, out _, checkPassage: false)) { placed++; break; }
		}
		if (!trial.IsConnected) return 0;
		context.Commit(trial);
		return placed;
	}

	public static int PlaceTableRows(RoomFurnishingContext context, RoomArrangementDefinition table, Vector3 origin,
		int rows, float rowSpacing, int turn, string key) => PlaceRows(context, table, origin, 1, rows, 0, rowSpacing, turn, key);

	public static int PlaceBookshelfRows(RoomFurnishingContext context, RoomArrangementDefinition shelf, Vector3 origin,
		int columns, int rows, float rowSpacing, int turn, string key) =>
		PlaceRows(context, shelf, origin, columns, rows, context.Bounds(shelf).Size.X, rowSpacing, turn, key);

	public static int Turn(Vector2I outward) => outward == Vector2I.Up ? 0 : outward == Vector2I.Left ? 1 : outward == Vector2I.Down ? 2 : 3;
}