using System;
using System.Collections.Generic;
using Godot;
using System.Linq;

/// <summary>Floor-local spatial fields shared by selected effects. Lazy texture caches avoid rebuilding identical emission volumes.</summary>
public sealed class AtmosphereContext : IDisposable
{
	/// <summary>CPU consumers share positions directly; only particle consumers request an uploaded texture.</summary>
	public sealed class Emission(Vector3[] positions, uint seed) : IDisposable
	{
		public int Count => positions.Length;
		public uint Seed => seed;
		public ReadOnlySpan<Vector3> Positions => positions;
		private ImageTexture _points;
		public ImageTexture Points
		{
			get
			{
				if (_points != null) return _points;
				using var image = Image.CreateEmpty(Mathf.Max(1, Count), 1, false, Image.Format.Rgbf);
				for (int i = 0; i < Count; i++)
					image.SetPixel(i, 0, new Color(positions[i].X, positions[i].Y, positions[i].Z));
				return _points = ImageTexture.CreateFromImage(image);
			}
		}
		public void Dispose() { _points?.Dispose(); _points = null; }
	}
	/// <summary>The foot of a wall beside walkable floor: the middle of its tile edge at floor level, and the direction it faces into the floor.</summary>
	public readonly record struct WallFace(Vector3 Position, Vector3 Normal);

	public Rect2 Bounds => _map.WorldBounds;
	public ulong Seed { get; }
	public float Phase { get; }
	public int TileCount => _tiles.Count;

	private readonly MapData _map;
	private readonly GeneratedFloor _floor;
	private readonly List<Vector2I> _tiles = [];
	private readonly ulong _emissionState;
	private readonly Dictionary<(float, float, int, float, bool), Emission> _emissions = [];
	private HashSet<Vector2I> _secretConnectors;
	private ImageTexture _floorMask;
	private WallFace[] _wallFaces;

	public AtmosphereContext(GeneratedFloor floor, ulong seed) : this(floor.Map, seed) => _floor = floor;

	public AtmosphereContext(MapData map, ulong seed)
	{
		_map = map;
		Seed = seed;
		for (int z = 0; z < map.Height; z++)
		for (int x = 0; x < map.Width; x++)
			if (map.IsExplorable(x, z)) _tiles.Add(new(x, z));
		using var random = GenerationRandom.Create(seed, GenerationRandom.Cosmetic, "atmosphere");
		Phase = random.RandfRange(0, 100);
		_emissionState = random.State;
	}

	/// <summary>Returns the placed room containing the point, excluding corridor and connector tiles.</summary>
	internal RoomRegion RoomAt(Vector3 point)
	{
		int id = _floor?.RoomIdAt(point) ?? -1;
		return id >= 0 ? _floor.Rooms[id] : null;
	}

	/// <summary>Points an interior mark toward its doorway or aligns a corridor mark with the open route.</summary>
	internal Vector2I PassageDirection(Vector3 point, RoomRegion room)
	{
		_secretConnectors ??= _floor?.Doors.Where(entry => entry.Door.Secret?.IsSecret == true)
			.Select(entry => entry.Connector).ToHashSet() ?? [];
		Vector2I tile = _map.WorldToTile(point);
		if (room != null)
		{
			foreach (var direction in MapData.CardinalOffsets)
			{
				var doorway = tile + direction;
				if (room.ConnectorTiles.Contains(doorway) && !_secretConnectors.Contains(doorway)
					&& _map.IsConnectorOpenToward(doorway.X, doorway.Y, direction)) return direction;
			}
			return Vector2I.Zero;
		}

		if (!_map.IsCorridor(tile.X, tile.Y)) return Vector2I.Zero;
		int horizontal = OpenPassages(tile, Vector2I.Right);
		int vertical = OpenPassages(tile, Vector2I.Down);
		if (horizontal == 0 && vertical == 0) return Vector2I.Zero;
		return horizontal >= vertical ? Vector2I.Right : Vector2I.Down;
	}

	private int OpenPassages(Vector2I tile, Vector2I axis) =>
		(OpenPassage(tile, tile + axis) ? 1 : 0) + (OpenPassage(tile, tile - axis) ? 1 : 0);

	private bool OpenPassage(Vector2I from, Vector2I to) => !_secretConnectors.Contains(to) && _map.CanSeeAcross(from, to);

	public ImageTexture FloorMask
	{
		get
		{
			if (_floorMask != null) return _floorMask;
			using var mask = Image.CreateEmpty(_map.Width, _map.Height, false, Image.Format.R8);
			mask.Fill(Colors.Black);
			foreach (var tile in _tiles) mask.SetPixel(tile.X, tile.Y, Colors.White);
			return _floorMask = ImageTexture.CreateFromImage(mask);
		}
	}

	/// <summary>Every wall edge of walkable floor, one face per tile edge, built once per floor.</summary>
	public IReadOnlyList<WallFace> WallFaces => _wallFaces ??= FindWallFaces();

	private WallFace[] FindWallFaces()
	{
		var faces = new List<WallFace>();
		foreach (var tile in _tiles)
		{
			if (!_map.IsWalkable(tile.X, tile.Y)) continue;
			Vector3 center = _map.TileToWorld(tile);
			foreach (var direction in MapData.CardinalOffsets)
			{
				if (!_map.HasWallToward(tile.X, tile.Y, direction)) continue;
				var outward = new Vector3(direction.X, 0, direction.Y);
				faces.Add(new(center + outward * (Room.TileSize / 2f), -outward));
			}
		}
		return [.. faces];
	}

	/// <summary>Shares a deterministic spatial field by placement settings, independent of effect ordering or population.</summary>
	public Emission GetEmission(float minHeight, float maxHeight, int samplesPerTile, float tileInset, bool dryFloorOnly = false)
	{
		var key = (minHeight, maxHeight, samplesPerTile, tileInset, dryFloorOnly);
		if (_emissions.TryGetValue(key, out var emission)) return emission;
		using var random = GenerationRandom.Create(Seed, GenerationRandom.Cosmetic, "atmosphere");
		random.State = _emissionState;
		var tiles = dryFloorOnly ? _tiles.FindAll(tile => _map.IsWalkable(tile.X, tile.Y)) : _tiles;
		int count = tiles.Count * samplesPerTile;
		var points = new Vector3[count];
		float inset = Room.TileSize * tileInset;
		int i = 0;
		foreach (var tile in tiles)
		{
			Vector3 center = _map.TileToWorld(tile);
			for (int sample = 0; sample < samplesPerTile; sample++)
				points[i++] = new(center.X + random.RandfRange(-inset, inset),
					random.RandfRange(minHeight, maxHeight), center.Z + random.RandfRange(-inset, inset));
		}
		emission = new(points, (uint)random.Randi());
		_emissions.Add(key, emission);
		return emission;
	}

	public void Dispose()
	{
		_floorMask?.Dispose();
		foreach (var emission in _emissions.Values) emission.Dispose();
		_emissions.Clear();
		_floorMask = null;
	}
}