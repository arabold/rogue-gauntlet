using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Mining tunnels: corridors wander between the rooms over a seeded field of hard rock, so routes bend in broad curves
/// instead of running straight, and dead-end shafts are dug off them. Paired with a layout of few, far-apart rooms the
/// floor becomes long walks through winding tunnels.
/// </summary>
[Tool]
[GlobalClass]
public partial class MineCorridorConnector : CorridorConnectorStrategy
{
	/// <summary>How far tunnels wander: the hardest rock costs this many plain steps more than open ground.</summary>
	[Export(PropertyHint.Range, "0,10,0.1")] public float Wander { get; set; } = 3f;
	/// <summary>Tiles between the rock field's control points: larger spacing makes broader bends.</summary>
	[Export(PropertyHint.Range, "2,16")] public int BendSpacing { get; set; } = 5;
	/// <summary>Dead-end shafts dug off the tunnels.</summary>
	[Export(PropertyHint.Range, "0,20")] public int Shafts { get; set; } = 6;
	/// <summary>Shortest and longest shaft, in tiles; a shaft that cannot reach two tiles is filled back in.</summary>
	[Export] public Vector2I ShaftLength { get; set; } = new(3, 6);

	/// <summary>The router's cost of a step onto open ground (see <see cref="AStarCorridorConnector"/>).</summary>
	private const float PlainStepCost = 5f;

	public override void ConnectRooms(MapData map, FloorPlan plan, IReadOnlyList<RoomRegion> rooms, RandomNumberGenerator rng)
	{
		if (plan?.ApprovedEdges.Count > 0)
		{
			throw new InvalidOperationException("MineCorridorConnector does not support FloorPlan.ApprovedEdges.");
		}

		var rock = RockField(map.Width, map.Height, rng);
		AStarCorridorConnector.Connect(map, plan, rooms, tile => PlainStepCost * (1f + Wander * rock[tile.X, tile.Y]));
		DigShafts(map, plan, rng);
	}

	/// <summary>Seeded value noise in [0, 1]: random control points every <see cref="BendSpacing"/> tiles, smoothly blended.</summary>
	private float[,] RockField(int width, int height, RandomNumberGenerator rng)
	{
		int spacing = Math.Max(2, BendSpacing);
		var points = new float[width / spacing + 2, height / spacing + 2];
		for (int x = 0; x < points.GetLength(0); x++)
		for (int y = 0; y < points.GetLength(1); y++)
			points[x, y] = rng.Randf();

		var field = new float[width, height];
		for (int x = 0; x < width; x++)
		for (int y = 0; y < height; y++)
		{
			int cx = x / spacing, cy = y / spacing;
			float tx = Smooth((x % spacing) / (float)spacing), ty = Smooth((y % spacing) / (float)spacing);
			float near = Mathf.Lerp(points[cx, cy], points[cx + 1, cy], tx);
			float far = Mathf.Lerp(points[cx, cy + 1], points[cx + 1, cy + 1], tx);
			field[x, y] = Mathf.Lerp(near, far, ty);
		}
		return field;
	}

	private static float Smooth(float t) => t * t * (3 - 2 * t);

	/// <summary>Digs up to <see cref="Shafts"/> straight dead ends from random tunnel tiles into untouched rock.</summary>
	private void DigShafts(MapData map, FloorPlan plan, RandomNumberGenerator rng)
	{
		if (Shafts <= 0) return;
		var tunnels = new List<Vector2I>();
		for (int x = 0; x < map.Width; x++)
		for (int y = 0; y < map.Height; y++)
			if (map.IsCorridor(x, y) && plan?.ProtectedCorridors.Contains(new Vector2I(x, y)) != true) tunnels.Add(new Vector2I(x, y));
		for (int i = tunnels.Count - 1; i > 0; i--)
		{
			int j = rng.RandiRange(0, i);
			(tunnels[i], tunnels[j]) = (tunnels[j], tunnels[i]);
		}

		int dug = 0;
		var shaft = new List<Vector2I>();
		foreach (var start in tunnels)
		{
			if (dug >= Shafts) break;
			var direction = MapData.CardinalOffsets[rng.RandiRange(0, MapData.CardinalOffsets.Count - 1)];
			int length = rng.RandiRange(Math.Max(1, ShaftLength.X), Math.Max(ShaftLength.X, ShaftLength.Y));
			shaft.Clear();
			for (var from = start; shaft.Count < length; from += direction)
			{
				var tile = from + direction;
				if (!CanDig(map, tile, from)) break;
				map.SetTile(tile.X, tile.Y, MapTile.Corridor);
				shaft.Add(tile);
			}
			if (shaft.Count >= 2)
			{
				dug++;
				continue;
			}
			foreach (var tile in shaft) map.SetTile(tile.X, tile.Y, MapTile.Empty);
		}
	}

	/// <summary>A shaft tile is untouched rock clear of the border whose only open neighbor is the tile it is dug from.</summary>
	private static bool CanDig(MapData map, Vector2I tile, Vector2I from)
	{
		if (tile.X < 2 || tile.Y < 2 || tile.X > map.Width - 3 || tile.Y > map.Height - 3 || !map.IsEmpty(tile.X, tile.Y)) return false;
		foreach (var offset in MapData.CardinalOffsets)
		{
			var neighbor = tile + offset;
			if (neighbor != from && !map.IsEmpty(neighbor.X, neighbor.Y)) return false;
		}
		return true;
	}
}