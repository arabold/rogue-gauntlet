using System;
using System.Collections.Generic;
using Godot;

public partial class MapGenerator
{
	/// <summary>Omnidirectional exploration reach in world units; independent of lighting and camera zoom.</summary>
	[Export(PropertyHint.Range, "4,64,0.5")] public float SightRadius { get; set; } = 24f;
	/// <summary>Authored fog presentation; each generated floor owns a private runtime material and texture.</summary>
	[Export] public ShaderMaterial DiscoveryMaterial { get; set; }
	private DungeonDiscovery _discovery;
	private Image _discoveryImage;
	private ImageTexture _discoveryTexture;
	private MeshInstance3D _discoverySurface;
	private ShaderMaterial _discoveryMaterial;
	private float _discoveryTime = 1f;
	private bool _discoveryDirty;

	private void InitializeDiscovery()
	{
		if (DiscoveryMaterial == null) throw new InvalidOperationException("MapGenerator requires an assigned DiscoveryMaterial.");
		_discovery = new DungeonDiscovery(Map.Width, Map.Height);
		_discoveryTime = 1f;
		_discoveryImage = Image.CreateEmpty(_discovery.Width, _discovery.Height, false, Image.Format.Rgf);
		_discoveryImage.Fill(new Color(0, 0, 0, 0));
		_discoveryTexture = ImageTexture.CreateFromImage(_discoveryImage);
		_discoveryMaterial = (ShaderMaterial)DiscoveryMaterial.Duplicate();
		_discoveryMaterial.RenderPriority = 120;
		_discoveryMaterial.SetShaderParameter("exploration", _discoveryTexture);
		Vector3I firstTile = TileToWorld(0, 0, 0);
		_discoveryMaterial.SetShaderParameter("map_origin", new Vector2(firstTile.X, firstTile.Z) - Vector2.One * (TileSize * 0.5f));
		_discoveryMaterial.SetShaderParameter("map_extent", new Vector2(Map.Width * TileSize, Map.Height * TileSize));
		_discoverySurface = new MeshInstance3D
		{
			Name = "DiscoverySurface", Mesh = new QuadMesh { Size = Vector2.One * 2 },
			MaterialOverride = _discoveryMaterial, ExtraCullMargin = 16384,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, IgnoreOcclusionCulling = true,
		};
		AddChild(_discoverySurface);
	}

	public override void _Process(double delta)
	{
		if (_discoveryMaterial == null) return;
		_discoveryTime += (float)delta;
		_discoveryMaterial.SetShaderParameter("elapsed", _discoveryTime);
		if (!_discoveryDirty) return;
		_discoveryTexture.Update(_discoveryImage);
		_discoveryDirty = false;
	}

	/// <summary>Reveals only visible sample centers within reach. Closed door thresholds and sanctioned room edges
	/// are authoritative blockers; furnishing does not block exploration. No RNG or room traversal is involved.</summary>
	public void RevealFrom(Vector3 worldPosition)
	{
		if (_discovery == null) return;
		Vector2I tile = WorldToTile(worldPosition);
		if (!IsVisibleInterior(tile.X, tile.Y)) return;
		Vector2 origin = new(worldPosition.X / TileSize + Map.Width / 2 + 0.5f,
			worldPosition.Z / TileSize + Map.Height / 2 + 0.5f);
		RecordSamples(_discovery.Reveal(origin, SightRadius / TileSize, CanSeeAcross), false);
		UpdateDoorIndicators();
	}

	private bool CanSeeAcross(Vector2I from, Vector2I to)
	{
		if (!IsVisibleInterior(from.X, from.Y) || !IsVisibleInterior(to.X, to.Y)) return false;
		Vector2I direction = to - from;
		if (Map.RequiresInteriorWall(from.X, from.Y, direction)) return false;
		// Every closed door seals its connector's authored outward edge, including concealed passages.
		return !(_dooredConnectors.Contains(from) && ConnectorOpensToward(from, direction))
			&& !(_dooredConnectors.Contains(to) && ConnectorOpensToward(to, -direction));
	}

	private void RecordSamples(IEnumerable<int> samples, bool immediate)
	{
		foreach (int sample in samples)
		{
			_discovery.Explored.Add(sample);
			_revealedTiles.Add(_discovery.TileAt(sample));
			_discoveryImage.SetPixel(sample % _discovery.Width, sample / _discovery.Width,
				new Color(1, immediate ? -1 : _discoveryTime, 0, 1));
			_discoveryDirty = true;
		}
	}

	/// <summary>Explicit full-tile reveal for debug tools and scene acceptance tests; gameplay uses RevealFrom.</summary>
	internal void RevealTile(Vector2I tile)
	{
		_revealedTiles.Add(tile);
		if (_discovery == null) return;
		var samples = new List<int>();
		for (int y = 0; y < DungeonDiscovery.SamplesPerTile; y++)
		for (int x = 0; x < DungeonDiscovery.SamplesPerTile; x++)
			samples.Add((tile.Y * DungeonDiscovery.SamplesPerTile + y) * _discovery.Width + tile.X * DungeonDiscovery.SamplesPerTile + x);
		RecordSamples(samples, true);
	}

	private void ClearDiscovery()
	{
		if (IsInstanceValid(_discoverySurface))
		{
			RemoveChild(_discoverySurface);
			_discoverySurface.QueueFree();
		}
		_discoverySurface = null;
		_discoveryMaterial = null;
		_discoveryTexture = null;
		_discoveryImage = null;
		_discovery = null;
		_discoveryDirty = false;
	}
}