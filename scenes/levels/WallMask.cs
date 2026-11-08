using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Which floor samples lie inside a solid wall or a closed door, on the exploration grid
/// (<see cref="DungeonDiscovery.SamplesPerTile"/> per tile over <see cref="MapData.WorldBounds"/>), for wall clipping.
/// A sample counts when its center lies inside a wall outline: a standard one-unit wall centered on its tile edge covers
/// exactly the sample on each side, so the cut lands on the wall face. Door leaves are thinner than a sample and can fall
/// between sample centers, so a closed door claims every sample its leaf overlaps. Plain data, independent of rendering.
/// </summary>
public sealed class WallMask
{
	/// <summary>How far a sample center must lie inside a wall, so a face exactly on a sample center stays open.</summary>
	private const float Inset = 0.01f;
	private static readonly float SampleSize = (float)Room.TileSize / DungeonDiscovery.SamplesPerTile;
	/// <summary>A sample center this far outside a door leaf still has its sample overlap the leaf.</summary>
	private static readonly float LeafOverlap = SampleSize / 2f - Inset;

	private readonly byte[] _walls;
	private readonly Vector2 _origin;

	public int Width { get; }
	public int Height { get; }
	/// <summary>One byte per sample, row-major: 255 inside a wall or closed door, 0 elsewhere. Upload as R8.</summary>
	public byte[] Data { get; }

	/// <param name="walls">World XZ outlines (convex) of the floor's solid wall pieces.</param>
	public WallMask(MapData map, IEnumerable<Vector2[]> walls)
	{
		Width = map.Width * DungeonDiscovery.SamplesPerTile;
		Height = map.Height * DungeonDiscovery.SamplesPerTile;
		_origin = map.WorldBounds.Position;
		_walls = new byte[Width * Height];
		foreach (var wall in walls) Stamp(_walls, wall, Inset);
		Data = (byte[])_walls.Clone();
	}

	/// <summary>Replaces the door layer: the walls plus every given closed door's leaf outline (convex, world XZ).</summary>
	public void SetClosedDoors(IEnumerable<Vector2[]> leaves)
	{
		Array.Copy(_walls, Data, _walls.Length);
		foreach (var leaf in leaves) Stamp(Data, leaf, -LeafOverlap);
	}

	public bool IsWall(Vector2 worldXZ)
	{
		var sample = ((worldXZ - _origin) / SampleSize).Floor();
		int x = (int)sample.X, y = (int)sample.Y;
		return x >= 0 && y >= 0 && x < Width && y < Height && Data[y * Width + x] != 0;
	}

	/// <summary>Marks every sample whose center lies at least <paramref name="inset"/> inside the polygon (negative: at
	/// most that far outside).</summary>
	private void Stamp(byte[] target, Vector2[] polygon, float inset)
	{
		float area = 0;
		for (int i = 0; i < polygon.Length; i++) area += polygon[i].Cross(polygon[(i + 1) % polygon.Length]);
		if (Mathf.IsZeroApprox(area)) return;
		var bounds = new Rect2(polygon[0], Vector2.Zero);
		foreach (var point in polygon) bounds = bounds.Expand(point);
		bounds = bounds.Grow(Math.Max(0f, -inset));
		var min = ((bounds.Position - _origin) / SampleSize).Floor();
		var max = ((bounds.End - _origin) / SampleSize).Floor();
		for (int y = Math.Max(0, (int)min.Y); y <= Math.Min(Height - 1, (int)max.Y); y++)
		for (int x = Math.Max(0, (int)min.X); x <= Math.Min(Width - 1, (int)max.X); x++)
			if (IsInside(polygon, Mathf.Sign(area), _origin + new Vector2(x + 0.5f, y + 0.5f) * SampleSize, inset))
				target[y * Width + x] = 255;
	}

	/// <summary>Whether a point lies at least <paramref name="inset"/> inside every edge of a convex polygon whose
	/// <paramref name="winding"/> is the sign of its area.</summary>
	private static bool IsInside(Vector2[] polygon, int winding, Vector2 point, float inset)
	{
		for (int i = 0; i < polygon.Length; i++)
		{
			Vector2 a = polygon[i], edge = polygon[(i + 1) % polygon.Length] - a;
			float length = edge.Length();
			if (length < 0.0001f) continue; // Closed hulls repeat their first point.
			if (winding * edge.Cross(point - a) / length < inset) return false;
		}
		return true;
	}
}