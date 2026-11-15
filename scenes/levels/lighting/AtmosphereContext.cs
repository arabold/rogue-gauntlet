using System;
using System.Collections.Generic;
using Godot;

/// <summary>Floor-local spatial fields shared by selected effects. Lazy texture caches avoid rebuilding identical emission volumes.</summary>
public sealed class AtmosphereContext : IDisposable
{
	public sealed record Emission(ImageTexture Points, int Count, uint Seed);
	public Rect2 Bounds => _map.WorldBounds;
	public ulong Seed { get; }
	public float Phase { get; }
	public int TileCount => _tiles.Count;

	private readonly MapData _map;
	private readonly List<Vector2I> _tiles = [];
	private readonly ulong _emissionState;
	private readonly Dictionary<(float, float, int, float), Emission> _emissions = [];
	private ImageTexture _floorMask;

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

	/// <summary>Shares a deterministic spatial field by placement settings, independent of effect ordering or population.</summary>
	public Emission GetEmission(float minHeight, float maxHeight, int samplesPerTile, float tileInset)
	{
		var key = (minHeight, maxHeight, samplesPerTile, tileInset);
		if (_emissions.TryGetValue(key, out var emission)) return emission;
		using var random = GenerationRandom.Create(Seed, GenerationRandom.Cosmetic, "atmosphere");
		random.State = _emissionState;
		using var points = Image.CreateEmpty(TileCount * samplesPerTile, 1, false, Image.Format.Rgbf);
		for (int i = 0; i < points.GetWidth(); i++)
		{
			Vector3 center = _map.TileToWorld(_tiles[i / samplesPerTile]);
			float inset = Room.TileSize * tileInset;
			points.SetPixel(i, 0, new Color(center.X + random.RandfRange(-inset, inset),
				random.RandfRange(minHeight, maxHeight), center.Z + random.RandfRange(-inset, inset)));
		}
		emission = new(ImageTexture.CreateFromImage(points), points.GetWidth(), (uint)random.Randi());
		_emissions.Add(key, emission);
		return emission;
	}

	public void Dispose()
	{
		_floorMask?.Dispose();
		foreach (var emission in _emissions.Values) emission.Points.Dispose();
		_emissions.Clear();
		_floorMask = null;
	}
}