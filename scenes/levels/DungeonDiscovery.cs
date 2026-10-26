using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Persistent sub-tile exploration, independent of rendering and room entry. Sight crosses only explicitly
/// permitted cardinal edges; exact corner crossings require both routes to be open, preventing diagonal wall leaks.</summary>
public sealed class DungeonDiscovery
{
	public const int SamplesPerTile = 8;
	public int Width { get; }
	public int Height { get; }
	public HashSet<int> Explored { get; } = new();
	/// <summary>Tiles holding at least one explored sample: the granularity gameplay asks about.</summary>
	public HashSet<Vector2I> RevealedTiles { get; } = new();

	public DungeonDiscovery(int tileWidth, int tileHeight)
	{
		Width = tileWidth * SamplesPerTile;
		Height = tileHeight * SamplesPerTile;
	}

	public Vector2I TileAt(int index) => new(index % Width / SamplesPerTile, index / Width / SamplesPerTile);

	/// <summary>Throws unless every sample is unique and lies on an explorable tile of <paramref name="map"/>.</summary>
	public static void ValidateSamples(MapData map, IReadOnlyList<int> samples)
	{
		int width = map.Width * SamplesPerTile;
		int count = width * map.Height * SamplesPerTile;
		if (samples == null || samples.Distinct().Count() != samples.Count
			|| samples.Any(i => i < 0 || i >= count || !map.IsExplorable(i % width / SamplesPerTile, i / width / SamplesPerTile)))
			throw new InvalidOperationException("Invalid explored samples; a new run is required.");
	}

	/// <summary>Explores one sample and its tile; false when the sample already was explored.</summary>
	public bool Explore(int index)
	{
		if (!Explored.Add(index)) return false;
		RevealedTiles.Add(TileAt(index));
		return true;
	}

	public IEnumerable<int> TileSamples(Vector2I tile)
	{
		for (int y = 0; y < SamplesPerTile; y++)
		for (int x = 0; x < SamplesPerTile; x++)
			yield return (tile.Y * SamplesPerTile + y) * Width + tile.X * SamplesPerTile + x;
	}

	/// <summary>Discovers sample centers within a circular radius. Previously explored samples never become hidden.</summary>
	public List<int> Reveal(Vector2 origin, float radius, Func<Vector2I, Vector2I, bool> canCross)
	{
		var added = new List<int>();
		int minX = Math.Max(0, (int)MathF.Floor((origin.X - radius) * SamplesPerTile));
		int maxX = Math.Min(Width - 1, (int)MathF.Ceiling((origin.X + radius) * SamplesPerTile));
		int minY = Math.Max(0, (int)MathF.Floor((origin.Y - radius) * SamplesPerTile));
		int maxY = Math.Min(Height - 1, (int)MathF.Ceiling((origin.Y + radius) * SamplesPerTile));
		for (int y = minY; y <= maxY; y++)
		for (int x = minX; x <= maxX; x++)
		{
			int index = y * Width + x;
			if (Explored.Contains(index)) continue;
			var target = new Vector2((x + 0.5f) / SamplesPerTile, (y + 0.5f) / SamplesPerTile);
			if (origin.DistanceSquaredTo(target) > radius * radius || !HasSight(origin, target, canCross)) continue;
			Explore(index);
			added.Add(index);
		}
		return added;
	}

	/// <summary>Walks tile boundaries rather than sampling along a ray, so even thin separating walls block sight.</summary>
	internal static bool HasSight(Vector2 origin, Vector2 target, Func<Vector2I, Vector2I, bool> canCross)
	{
		var cell = new Vector2I((int)MathF.Floor(origin.X), (int)MathF.Floor(origin.Y));
		var end = new Vector2I((int)MathF.Floor(target.X), (int)MathF.Floor(target.Y));
		Vector2 delta = target - origin;
		int sx = Math.Sign(delta.X), sy = Math.Sign(delta.Y);
		float dx = sx == 0 ? float.PositiveInfinity : 1 / MathF.Abs(delta.X);
		float dy = sy == 0 ? float.PositiveInfinity : 1 / MathF.Abs(delta.Y);
		float tx = sx == 0 ? float.PositiveInfinity : ((sx > 0 ? cell.X + 1 : cell.X) - origin.X) / delta.X;
		float ty = sy == 0 ? float.PositiveInfinity : ((sy > 0 ? cell.Y + 1 : cell.Y) - origin.Y) / delta.Y;
		while (cell != end)
		{
			var horizontal = cell + new Vector2I(sx, 0);
			var vertical = cell + new Vector2I(0, sy);
			if (MathF.Abs(tx - ty) < 0.00001f)
			{
				var diagonal = cell + new Vector2I(sx, sy);
				if (!canCross(cell, horizontal) || !canCross(cell, vertical)
					|| !canCross(horizontal, diagonal) || !canCross(vertical, diagonal)) return false;
				cell = diagonal;
				tx += dx; ty += dy;
			}
			else if (tx < ty)
			{
				if (!canCross(cell, horizontal)) return false;
				cell = horizontal; tx += dx;
			}
			else
			{
				if (!canCross(cell, vertical)) return false;
				cell = vertical; ty += dy;
			}
		}
		return true;
	}
}