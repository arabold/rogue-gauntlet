using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// A floor's persistent exploration and its fog surface. It polls the player at 10 Hz and reveals the samples the
/// player can see. Sight crosses a doored connector only while every door on it is open, read live from the doors, so
/// door state is never copied here. Exploration reaches shaders as the <c>fog_*</c> global uniforms
/// (<c>fog_of_war.gdshaderinc</c>), so the fog and the silhouettes drawn above it read one source. The fade clock is
/// pushed only while a reveal is fading in. Gameplay reads exploration through <see cref="ExplorationVisibility"/>.
/// </summary>
[GlobalClass]
public partial class FloorDiscovery : Node3D
{
	/// <summary>Omnidirectional exploration reach in world units; independent of lighting and camera zoom.</summary>
	[Export(PropertyHint.Range, "4,64,0.5")] public float SightRadius { get; set; } = 24f;
	/// <summary>How long a newly explored sample takes to fade in. Drives the shader and when this node goes idle.</summary>
	[Export(PropertyHint.Range, "0.01,2,0.01")] public float FadeSeconds { get; set; } = 0.35f;
	/// <summary>How far, in world units, the fog feathers inward from explored edges. Presentation only.</summary>
	[Export(PropertyHint.Range, "0,3,0.05")] public float FeatherWidth { get; set; } = 1f;
	/// <summary>Authored fog surface; each floor gets a private runtime copy. Unset disables the fog surface.</summary>
	[Export] public ShaderMaterial DiscoveryMaterial { get; set; }

	/// <summary>Transparent draw order of the fog surface.</summary>
	public const int FogRenderPriority = 120;
	/// <summary>Draw order for door and chest x-rays, which stay readable over fogged walls. Their own discovery gates
	/// keep them off until their tile (or, for doors, an adjacent tile) is explored, so drawing above fog leaks nothing.</summary>
	public const int AboveFogRenderPriority = FogRenderPriority + 1;

	private const double PollSeconds = 0.1;
	private static readonly StringName ExplorationGlobal = "fog_exploration";
	private static readonly StringName MapOriginGlobal = "fog_map_origin";
	private static readonly StringName MapExtentGlobal = "fog_map_extent";
	private static readonly StringName ElapsedGlobal = "fog_elapsed";
	private static readonly StringName FadeSecondsGlobal = "fog_fade_seconds";
	private static readonly StringName FeatherWidthGlobal = "fog_feather_width";
	private static readonly StringName PlayerGroup = "player";

	private GeneratedFloor _floor;
	private DungeonDiscovery _model;
	private Func<Vector2I, Vector2I, bool> _canSeeAcross;
	private readonly Dictionary<Vector2I, List<Door>> _doorsByConnector = new();
	private readonly Dictionary<Vector2I, List<Door>> _indicatorDoors = new();
	private readonly HashSet<Door> _indicatorsShown = new();
	private Image _image;
	private ImageTexture _texture;
	private MeshInstance3D _surface;
	private bool _textureDirty;
	private ulong _clockStart;
	private float _fadeUntil;
	private Player _player;
	private double _untilPoll;
	private Vector3 _lastPosition = Vector3.Inf;
	private float _lastRadius;
	private bool _refresh = true;

	/// <summary>Seconds since this floor's exploration began, offset so immediate reveals (stored as -1) are always faded in.</summary>
	private float Clock => 1f + (Time.GetTicksUsec() - _clockStart) / 1_000_000f;

	public override void _Ready()
	{
		SetProcess(false);
		this.SubscribeUntilExit(SignalBus.Instance,
			bus => bus.PlayerSpawned += OnPlayerSpawned, bus => bus.PlayerSpawned -= OnPlayerSpawned);
		this.SubscribeUntilExit(SignalBus.Instance,
			bus => bus.DoorOpened += OnDoorChanged, bus => bus.DoorOpened -= OnDoorChanged);
		this.SubscribeUntilExit(SignalBus.Instance,
			bus => bus.DoorClosed += OnDoorChanged, bus => bus.DoorClosed -= OnDoorChanged);
	}

	/// <summary>Leaves no stale fog for silhouettes in scenes without a floor.</summary>
	public override void _ExitTree() => RenderingServer.GlobalShaderParameterSet(MapExtentGlobal, Vector2.Zero);

	/// <summary>Drops the previous floor, so nothing reads its exploration while the next one generates.</summary>
	public void Clear()
	{
		RenderingServer.GlobalShaderParameterSet(MapExtentGlobal, Vector2.Zero);
		if (IsInstanceValid(_surface))
		{
			RemoveChild(_surface);
			_surface.QueueFree();
		}
		_surface = null;
		_texture = null;
		_image = null;
		_model = null;
		_floor = null;
		_player = null;
		_doorsByConnector.Clear();
		_indicatorDoors.Clear();
		_indicatorsShown.Clear();
		_textureDirty = false;
		SetProcess(false);
	}

	/// <summary>Starts an unexplored floor. Saved exploration is restored afterwards through <see cref="RestoreSamples"/>.</summary>
	public void Initialize(GeneratedFloor floor)
	{
		Clear();
		_floor = floor;
		_model = new DungeonDiscovery(floor.Map.Width, floor.Map.Height);
		_canSeeAcross = CanSeeAcross;
		foreach (var (door, connector) in floor.Doors)
		{
			if (!_doorsByConnector.TryGetValue(connector, out var doors)) _doorsByConnector[connector] = doors = new();
			doors.Add(door);
			if (!_indicatorDoors.TryGetValue(connector, out var indicators)) _indicatorDoors[connector] = indicators = new();
			indicators.Add(door);
		}
		foreach (var pair in floor.DoorBaselines.Values.Where(pair => pair.Door.Interior != null))
		{
			var tile = floor.Map.WorldToTile(pair.Door.GlobalPosition);
			if (!_indicatorDoors.TryGetValue(tile, out var indicators)) _indicatorDoors[tile] = indicators = new();
			indicators.Add(pair.Door);
		}
		_clockStart = Time.GetTicksUsec();
		_fadeUntil = 0;
		_refresh = true;
		_untilPoll = 0;
		_player = GetTree().GetFirstNodeInGroup(PlayerGroup) as Player;
		CreateSurface();
	}

	private void CreateSurface()
	{
		if (DiscoveryMaterial == null)
		{
			GD.PushError($"{Name}: assign a DiscoveryMaterial; the fog surface is disabled.");
			return;
		}
		_image = Image.CreateEmpty(_model.Width, _model.Height, false, Image.Format.Rgf);
		_image.Fill(new Color(0, 0, 0, 0));
		_texture = ImageTexture.CreateFromImage(_image);
		var material = (ShaderMaterial)DiscoveryMaterial.Duplicate();
		material.RenderPriority = FogRenderPriority;
		Vector3I firstTile = _floor.Map.TileToWorld(0, 0);
		RenderingServer.GlobalShaderParameterSet(ExplorationGlobal, _texture);
		RenderingServer.GlobalShaderParameterSet(FadeSecondsGlobal, FadeSeconds);
		RenderingServer.GlobalShaderParameterSet(FeatherWidthGlobal, FeatherWidth);
		RenderingServer.GlobalShaderParameterSet(MapOriginGlobal, new Vector2(firstTile.X, firstTile.Z) - Vector2.One * (Room.TileSize * 0.5f));
		RenderingServer.GlobalShaderParameterSet(MapExtentGlobal, new Vector2(_floor.Map.Width * Room.TileSize, _floor.Map.Height * Room.TileSize));
		_surface = new MeshInstance3D
		{
			Name = "DiscoverySurface", Mesh = new QuadMesh { Size = Vector2.One * 2 },
			MaterialOverride = material, ExtraCullMargin = 16384,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, IgnoreOcclusionCulling = true,
		};
		AddChild(_surface);
		SetProcess(true);
	}

	private void OnPlayerSpawned(Player player)
	{
		_player = player;
		_untilPoll = 0;
		_refresh = true;
	}

	private void OnDoorChanged(Node3D door)
	{
		_untilPoll = 0;
		_refresh = true;
	}

	public override void _PhysicsProcess(double delta)
	{
		_untilPoll -= delta;
		if (_untilPoll > 0 || _model == null || !IsInstanceValid(_player) || !_player.IsInsideTree()) return;
		_untilPoll = PollSeconds;
		if (!_refresh && _lastRadius == SightRadius && _lastPosition.DistanceSquaredTo(_player.GlobalPosition) < 0.0225f) return;
		_refresh = false;
		_lastPosition = _player.GlobalPosition;
		_lastRadius = SightRadius;
		RevealFrom(_lastPosition);
	}

	public override void _Process(double delta)
	{
		if (_textureDirty)
		{
			_texture.Update(_image);
			_textureDirty = false;
		}
		float clock = Clock;
		RenderingServer.GlobalShaderParameterSet(ElapsedGlobal, clock);
		if (clock > _fadeUntil) SetProcess(false);
	}

	public bool IsRevealedAt(Vector3 worldPosition) => _model != null && _model.RevealedTiles.Contains(_floor.Map.WorldToTile(worldPosition));

	/// <summary>Reveals only visible sample centers within reach. Closed door thresholds and sanctioned room edges
	/// are authoritative blockers; furnishing does not block exploration. No RNG or room traversal is involved.</summary>
	public void RevealFrom(Vector3 worldPosition)
	{
		if (_model == null) return;
		MapData map = _floor.Map;
		Vector2I tile = map.WorldToTile(worldPosition);
		if (!map.IsExplorable(tile.X, tile.Y)) return;
		Vector2 origin = new(worldPosition.X / Room.TileSize + map.Width / 2 + 0.5f,
			worldPosition.Z / Room.TileSize + map.Height / 2 + 0.5f);
		Present(_model.Reveal(origin, SightRadius / Room.TileSize, _canSeeAcross), false);
	}

	/// <summary>Explicit debug/test reveal of one room, without revealing connected corridors or rooms.</summary>
	public void RevealRoom(int roomId)
	{
		if (_model == null || roomId < 0 || roomId >= _floor.Rooms.Count) return;
		RevealTiles(_floor.Rooms[roomId].Tiles);
	}

	/// <summary>Debug: explores every playable tile, so exploration-gated loot, traps and effects appear with the fog.</summary>
	public void RevealAll()
	{
		if (_model == null) return;
		MapData map = _floor.Map;
		var tiles = new List<Vector2I>();
		for (int x = 0; x < map.Width; x++)
		for (int z = 0; z < map.Height; z++)
			if (map.IsExplorable(x, z)) tiles.Add(new Vector2I(x, z));
		RevealTiles(tiles);
	}

	/// <summary>Explicit full-tile reveal for debug tools and scene acceptance tests; gameplay uses RevealFrom.</summary>
	internal void RevealTiles(IEnumerable<Vector2I> tiles)
	{
		if (_model == null) return;
		var added = new List<int>();
		foreach (var tile in tiles)
		{
			if (!_floor.Map.IsWithinBounds(tile.X, tile.Y)) continue;
			foreach (int sample in _model.TileSamples(tile))
				if (_model.Explore(sample)) added.Add(sample);
		}
		Present(added, true);
	}

	/// <summary>The explored samples a save records, in ascending order.</summary>
	public List<int> CaptureSamples() => _model?.Explored.OrderBy(i => i).ToList() ?? [];

	/// <summary>Restores saved exploration immediately, without a fade. Validates before changing anything.</summary>
	public void RestoreSamples(IReadOnlyList<int> samples)
	{
		if (_model == null) return;
		DungeonDiscovery.ValidateSamples(_floor.Map, samples);
		var added = new List<int>();
		foreach (int sample in samples)
			if (_model.Explore(sample)) added.Add(sample);
		Present(added, true);
	}

	private bool CanSeeAcross(Vector2I from, Vector2I to)
	{
		MapData map = _floor.Map;
		if (!map.IsExplorable(from.X, from.Y) || !map.IsExplorable(to.X, to.Y)) return false;
		Vector2I direction = to - from;
		if (map.RequiresInteriorWall(from.X, from.Y, direction)) return false;
		// Every closed door seals its connector's authored outward edge, including concealed passages.
		return !(IsSealed(from) && map.GetConnectorDirections(from.X, from.Y).Contains(direction))
			&& !(IsSealed(to) && map.GetConnectorDirections(to.X, to.Y).Contains(-direction));
	}

	/// <summary>A doored connector stays sealed while any of its doors is closed.</summary>
	private bool IsSealed(Vector2I connector)
	{
		if (!_doorsByConnector.TryGetValue(connector, out var doors)) return false;
		foreach (var door in doors)
		{
			if (IsInstanceValid(door) && !door.IsOpen) return true;
		}
		return false;
	}

	/// <summary>Paints newly explored samples and allows the x-ray indicator of each door next to a newly revealed tile,
	/// so doors still buried in the fog never leak their location.</summary>
	private void Present(List<int> added, bool immediate)
	{
		if (added.Count == 0) return;
		float time = immediate ? -1 : Clock;
		var tiles = new HashSet<Vector2I>();
		foreach (int sample in added)
		{
			_image?.SetPixel(sample % _model.Width, sample / _model.Width, new Color(1, time, 0, 1));
			tiles.Add(_model.TileAt(sample));
		}
		foreach (var tile in tiles)
		foreach (var offset in MapData.CardinalOffsets)
		{
			if (!_indicatorDoors.TryGetValue(tile + offset, out var doors)) continue;
			foreach (var door in doors)
				if (_indicatorsShown.Add(door) && IsInstanceValid(door)) door.SetIndicatorVisible(true);
		}
		if (_surface == null) return;
		_textureDirty = true;
		if (!immediate) _fadeUntil = time + FadeSeconds;
		SetProcess(true);
	}
}