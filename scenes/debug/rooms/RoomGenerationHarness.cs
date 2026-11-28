using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Plays any room the generator builds, trap gauntlets included. The panel picks a realm, a room type or room group, a
/// seed and any axis override (shape, walls, floor, columns, motifs, furnishing, rotation); the production
/// <see cref="RoomAssembler"/> builds it and the harness stands it in its <see cref="Arena"/> with a short corridor
/// outside every doorway, the hero waiting in the one nearest the camera. The arena is the game's own hero, HUD, debug
/// menu, camera and lighting, so hazards, doors and monsters play as they do on a floor. A walkability overlay colours
/// standable floor reachable from the doorways (green), reserved circulation and stair landings (blue) and inaccessible
/// pockets (red), and the overview camera frames the whole room from above. Overrides never change production Resources.
/// </summary>
public partial class RoomGenerationHarness : CanvasLayer
{
	private const string RoomGroupsDir = "res://scenes/levels/rooms/room_groups";
	/// <summary>Every room type, so types outside the pool's ordinary slots, such as gauntlets, play while they are designed.</summary>
	private const string RoomTypesDir = "res://scenes/levels/rooms/room_types";
	/// <summary>Corridor tiles laid outside each doorway: the hero waits in the first.</summary>
	private const int CorridorLength = 2;
	/// <summary>Tiles around the room in the staged site: the corridors and a ring of void for their walls.</summary>
	private const int Margin = CorridorLength + 1;

	/// <summary>The world the room stands in; its <see cref="DebugArena.Structure"/> holds the room.</summary>
	[Export] public DebugArena Arena { get; set; }
	[Export] public Player Player { get; set; }
	/// <summary>Restores the hero whenever the hero returns to the entrance.</summary>
	[Export] public HeroTool Hero { get; set; }
	[Export] public ScreenEffectsLayer ScreenEffects { get; set; }
	/// <summary>The world's draft, bound to every staged room's ambient motion as a floor binds its props.</summary>
	[Export] public Airflow Airflow { get; set; }
	/// <summary>The production room pool: each realm's room types and the gauntlets.</summary>
	[Export] public RoomPool Rooms { get; set; }
	/// <summary>Folder whose realms the realm picker lists, shallowest first.</summary>
	[Export] public string RealmDirectory { get; set; } = "res://scenes/levels/realms";
	/// <summary>The dungeon floor tiles corridors are laid with, as the map generator lays them.</summary>
	[Export] public MeshLibrary CorridorFloors { get; set; }
	/// <summary>The dungeon wall pieces corridor walls are built from.</summary>
	[Export] public MeshLibrary CorridorWalls { get; set; }
	/// <summary>A top-down camera that frames the whole room while <see cref="Overview"/> is on.</summary>
	[Export] public Camera3D OverviewCamera { get; set; }
	[Export] public Container Controls { get; set; }
	[Export] public Label Status { get; set; }
	[Export] public int SelectedRealm { get; set; }
	[Export] public int SelectedRecipe { get; set; }
	[Export] public uint Seed { get; set; } = 23;
	[Export] public bool Furnishings { get; set; } = true;
	[Export] public bool ShowWalkability { get; set; }
	/// <summary>Frames the room from above instead of following the hero.</summary>
	[Export]
	public bool Overview
	{
		get;
		set
		{
			field = value;
			if (IsNodeReady()) ApplyCamera();
		}
	}
	/// <summary>Room-type overrides: an index into the type's palette, or <see cref="RoomAssemblyOptions.Seeded"/>.</summary>
	[Export] public int ShapeOverride { get; set; } = RoomAssemblyOptions.Seeded;
	[Export] public int WallsOverride { get; set; } = RoomAssemblyOptions.Seeded;
	[Export] public int FloorOverride { get; set; } = RoomAssemblyOptions.Seeded;
	/// <summary>Column pattern index, <see cref="RoomAssemblyOptions.Seeded"/> (type's chance) or <see cref="RoomAssemblyOptions.Off"/>.</summary>
	[Export] public int ColumnsOverride { get; set; } = RoomAssemblyOptions.Seeded;
	/// <summary>The floor motifs a room assembles with: 0 every motif step, 1 none, then the realm's motifs one by one.</summary>
	[Export] public int SelectedMotif { get; set; }

	/// <summary>The previewed room; a group's primary room.</summary>
	public Room PreviewRoom { get; private set; }
	/// <summary>The previewed room type's blueprint; null for a group.</summary>
	public RoomBlueprint PreviewBlueprint { get; private set; }
	public string LastError { get; private set; } = "";
	public IReadOnlyList<Realm> Realms { get; private set; } = [];
	public Realm Realm => Realms[SelectedRealm];
	/// <summary>The room pool narrowed to the selected realm.</summary>
	public RoomPool Pool { get; private set; }
	/// <summary>The realm's rooms, entrances, exits and gauntlets, the rooms it schedules, its room groups and every other room type for it.</summary>
	public IReadOnlyList<RoomDefinition> Recipes => _recipes;
	/// <summary>The realm's motifs, in the order the motif picker lists them after "every" and "none".</summary>
	public IReadOnlyList<string> MotifIds => _motifIds;
	/// <summary>The motifs a room assembles with: null runs every motif step (<see cref="SelectedMotif"/> 0).</summary>
	public IReadOnlySet<string> PreviewMotifs => SelectedMotif switch
	{
		0 => null,
		1 => new HashSet<string>(),
		_ => new HashSet<string> { _motifIds[Mathf.Clamp(SelectedMotif - 2, 0, _motifIds.Count - 1)] },
	};
	/// <summary>Where the hero waits: the first corridor tile outside the doorway nearest the camera.</summary>
	public Vector3 Entrance { get; private set; }
	/// <summary>From the entrance into the room.</summary>
	public Vector3 Inward { get; private set; } = Vector3.Forward;

	private readonly List<RoomDefinition> _recipes = [];
	private readonly List<string> _motifIds = [];
	private Node3D _site;
	private int _quarterTurns;
	private OptionButton _realmPicker, _recipePicker, _shapePicker, _wallsPicker, _floorPicker, _columnPicker, _motifPicker;
	private HBoxContainer _shapeRow, _wallsRow, _floorRow, _columnRow;
	private SpinBox _seedInput;
	private CheckButton _furnishingsToggle, _walkabilityToggle, _overviewToggle;
	private bool _syncing;
	private bool _regenerationPending;

	public override void _Ready()
	{
		Realms = DebugCatalog.Resources<Realm>(RealmDirectory).Select(entry => entry.Resource).OrderBy(realm => realm.FirstDepth).ToList();
		if (Arena?.Structure == null || Player == null || Hero == null || Rooms == null || Realms.Count == 0
			|| CorridorFloors == null || CorridorWalls == null || OverviewCamera == null || Controls == null || Status == null)
		{
			LastError = "Assign the arena (with a structure node), hero, hero tool, room pool, realm folder, corridor libraries, overview camera, controls and status label.";
			GD.PushError(LastError);
			return;
		}
		ScreenEffects?.Bind(Player);
		BuildControls();
		ApplyRealm(Mathf.Clamp(SelectedRealm, 0, Realms.Count - 1));
		Regenerate();
		ApplyCamera();
	}

	private void BuildControls()
	{
		_realmPicker = Picker("Realm", out _, SelectRealm);
		foreach (var realm in Realms) _realmPicker.AddItem($"{realm.Id.Capitalize()} ({realm.FirstDepth}–{realm.LastDepth})");
		_recipePicker = Picker("Room", out _, SelectRecipe);
		_shapePicker = Picker("Shape", out _shapeRow, index => ShapeOverride = index - 1);
		_wallsPicker = Picker("Walls", out _wallsRow, index => WallsOverride = index - 1);
		_floorPicker = Picker("Floor", out _floorRow, index => FloorOverride = index - 1);
		_columnPicker = Picker("Columns", out _columnRow,
			index => ColumnsOverride = index == 0 ? RoomAssemblyOptions.Seeded : index == 1 ? RoomAssemblyOptions.Off : index - 2);
		_motifPicker = Picker("Motifs", out _, index => SelectedMotif = index);

		_seedInput = new SpinBox { MinValue = 0, MaxValue = uint.MaxValue, Step = 1 };
		_seedInput.ValueChanged += value => { if (!_syncing) { Seed = (uint)value; RequestRegeneration(); } };
		AddControlRow("Seed", _seedInput);

		_furnishingsToggle = Toggle("Furnishings", enabled => { Furnishings = enabled; RequestRegeneration(); });
		_walkabilityToggle = Toggle("Walkability overlay", enabled => { ShowWalkability = enabled; RequestRegeneration(); });
		_overviewToggle = Toggle("Overview camera", enabled => Overview = enabled);
		var buttons = new HBoxContainer();
		Controls.AddChild(buttons);
		AddButton(buttons, "Next seed", NextSeed);
		AddButton(buttons, "Rotate 90°", () => RotatePreview(1));
		AddButton(Controls, "Back to entrance", ReturnToEntrance);
		// Mouse-operated controls must not take the keys that move the hero; the seed field still takes typing.
		DisableKeyboardFocus(Controls);
	}

	private OptionButton Picker(string title, out HBoxContainer row, Action<int> select)
	{
		var picker = new OptionButton { FitToLongestItem = false };
		picker.ItemSelected += index =>
		{
			if (_syncing) return;
			select((int)index);
			RequestRegeneration();
		};
		row = AddControlRow(title, picker);
		return picker;
	}

	private CheckButton Toggle(string title, Action<bool> set)
	{
		var toggle = new CheckButton { Text = title };
		toggle.Toggled += enabled => { if (!_syncing) set(enabled); };
		Controls.AddChild(toggle);
		return toggle;
	}

	private HBoxContainer AddControlRow(string title, Control control)
	{
		var row = new HBoxContainer();
		row.AddChild(new Label { Text = title, CustomMinimumSize = new(72, 0) });
		control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		row.AddChild(control);
		Controls.AddChild(row);
		return row;
	}

	private static void AddButton(Container parent, string title, Action action)
	{
		var button = new Button { Text = title, CustomMinimumSize = new(0, 30), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		button.Pressed += action;
		parent.AddChild(button);
	}

	private static void DisableKeyboardFocus(Node node)
	{
		if (node is Control control and not LineEdit) control.FocusMode = Control.FocusModeEnum.None;
		foreach (Node child in node.GetChildren(true)) DisableKeyboardFocus(child);
	}

	private static string RecipeName(RoomDefinition definition) =>
		definition == null ? "Missing room type" : $"{Local(definition.Id).Replace('_', ' ').Capitalize()}{(definition is RoomGroup ? " (group)" : "")}";

	/// <summary>An ID without its realm prefix (garrison.hall → hall).</summary>
	private static string Local(string id) => id.Split('.').Last();

	private static string ShapeName(RoomShape shape) =>
		$"{shape.GetType().Name.Replace("Shape", "")} {shape.MinimumSize.X}–{shape.MaximumSize.X} × {shape.MinimumSize.Y}–{shape.MaximumSize.Y}";

	/// <summary>Plays another realm's rooms, in its look, from its first room.</summary>
	public void SelectRealm(int index)
	{
		ApplyRealm(Mathf.Clamp(index, 0, Realms.Count - 1));
		SelectRecipe(0);
	}

	private void ApplyRealm(int index)
	{
		SelectedRealm = index;
		Pool = Rooms.ForRealm(Realm.Theme);
		_recipes.Clear();
		_recipes.AddRange(Pool.AllDefinitions);
		// Rooms the realm schedules beyond the pool, such as a floor plan's own rooms it turns into headlines.
		_recipes.AddRange(Realm.Rooms.Select(entry => entry?.Room).Where(room => room != null && !_recipes.Contains(room)));
		_recipes.AddRange(DebugCatalog.Resources<RoomGroup>(RoomGroupsDir).Select(entry => entry.Resource)
			.Where(group => group.SupportsRealm(Realm.Theme) && !_recipes.Contains(group)));
		_recipes.AddRange(DebugCatalog.Resources<RoomType>(RoomTypesDir).Select(entry => entry.Resource)
			.Where(type => type.SupportsRealm(Realm.Theme) && !_recipes.Contains(type)));
		_motifIds.Clear();
		_motifIds.AddRange(Realm.Motifs.Where(motif => motif != null).Select(motif => motif.Id).Distinct());
		SelectedMotif = Mathf.Clamp(SelectedMotif, 0, _motifIds.Count + 1);
		_recipePicker.Clear();
		foreach (var recipe in Recipes) _recipePicker.AddItem(RecipeName(recipe));
	}

	/// <summary>Plays another room: resets the overrides and rotation and keeps the seed.</summary>
	public void SelectRecipe(int index)
	{
		SelectedRecipe = index;
		ShapeOverride = WallsOverride = FloorOverride = ColumnsOverride = RoomAssemblyOptions.Seeded;
		_quarterTurns = 0;
		RequestRegeneration();
	}

	/// <summary>Plays <paramref name="definition"/>, as <see cref="SelectRecipe"/> does; false when the realm does not list it.</summary>
	public bool SelectRoom(RoomDefinition definition)
	{
		int index = _recipes.IndexOf(definition);
		if (index < 0) return false;
		SelectRecipe(index);
		return true;
	}

	public void NextSeed()
	{
		Seed = unchecked(Seed + 1);
		RequestRegeneration();
	}

	public void RotatePreview(int steps)
	{
		_quarterTurns = ((_quarterTurns + steps) % 4 + 4) % 4;
		RequestRegeneration();
	}

	/// <summary>Coalesce UI events so the room is replaced at most once per process frame.</summary>
	private void RequestRegeneration() => _regenerationPending = true;

	public override void _Process(double delta)
	{
		if (_regenerationPending) Regenerate();
	}

	/// <summary>Builds the selected room, replaces the staged one with it and puts the hero at its entrance.</summary>
	public void Regenerate()
	{
		_regenerationPending = false;
		LastError = "";
		try
		{
			if (SelectedRecipe < 0 || SelectedRecipe >= Recipes.Count || Recipes[SelectedRecipe] == null)
				throw new InvalidOperationException("Select an assigned room type.");
			var definition = Recipes[SelectedRecipe];
			if (definition is RoomGroup group) StageGroup(group);
			else StageRoomType(definition);
			ReturnToEntrance();
		}
		catch (InvalidOperationException error)
		{
			LastError = error.Message;
			Status.Text = $"Generation failed: {LastError}";
			GD.PushError(LastError);
		}
		SyncControls();
	}

	private void StageRoomType(RoomDefinition definition)
	{
		if (definition is not RoomType type)
			throw new InvalidOperationException($"The harness plays room types and groups, not {definition?.Id ?? "null"}.");
		var options = new RoomAssemblyOptions
		{
			Shape = ShapeOverride, Walls = WallsOverride, Floor = FloorOverride, Columns = ColumnsOverride, Furnish = Furnishings,
			Realm = Realm.Theme, Motifs = PreviewMotifs,
		};
		var blueprint = RoomAssembler.Build(type, Seed, options);
		var room = blueprint.Room;
		float pockets;
		try
		{
			pockets = AddWalkabilityOverlay(blueprint);
			room.Rotate(_quarterTurns);
			Stage(type.Id, room.Map, [(room, Vector2I.Zero)]);
		}
		catch
		{
			if (IsInstanceValid(room) && room.GetParent() == null) room.Free();
			throw;
		}
		PreviewBlueprint = blueprint;
		PreviewRoom = room;
		Status.Text = $"{type.Id} · Seed {Seed} · {_quarterTurns * 90}°\n"
			+ $"{blueprint.Footprint.Width} × {blueprint.Footprint.Length} tile bounds · {blueprint.Footprint.Count} footprint tiles\n"
			+ $"{ShapeName(blueprint.Shape)}\n"
			+ $"walls {Local(blueprint.Walls.Id)} · floor {Local(blueprint.Floor.Id)} · columns {blueprint.Columns?.Id ?? "none"}\n"
			+ $"{blueprint.Pieces.Count} props · {blueprint.Doorways.Count} doorways · pockets {pockets:F1}% of standable floor";
	}

	private void StageGroup(RoomGroup group)
	{
		var options = new RoomAssemblyOptions { Furnish = Furnishings, Realm = Realm.Theme, Motifs = PreviewMotifs };
		var cluster = RoomAssembler.AssembleCluster(group, Seed, options, Pool);
		try
		{
			cluster.Rotate(_quarterTurns);
			Stage(group.Id, cluster.Map, cluster.Members.Select(member => (member.Room, member.Offset)).ToList());
			cluster.DetachRooms();
		}
		catch
		{
			cluster.Free();
			throw;
		}
		PreviewBlueprint = null;
		PreviewRoom = cluster.PrimaryRoom;
		Status.Text = $"{group.Id} (group) · {cluster.Members.Count} rooms · {cluster.Map.Width}×{cluster.Map.Height} tiles · Seed {Seed} · {_quarterTurns * 90}°";
	}

	/// <summary>
	/// Replaces the staged site with <paramref name="rooms"/>, placed on <paramref name="map"/> (tile (0, 0) at each
	/// room's offset), centred on the arena's origin, with corridors outside every doorway and between a group's rooms.
	/// Navigation then covers the new site.
	/// </summary>
	private void Stage(string id, MapData map, IReadOnlyList<(Room Room, Vector2I Offset)> rooms)
	{
		var siteMap = new MapData(map.Width + 2 * Margin, map.Height + 2 * Margin);
		var margin = new Vector2I(Margin, Margin);
		siteMap.Stamp(map, margin);
		var doorways = RoomCluster.GetExternalConnectors(map);
		foreach (var (tile, direction) in doorways)
		{
			for (int step = 1; step <= CorridorLength; step++)
			{
				var corridor = tile + margin + direction * step;
				if (!siteMap.IsEmpty(corridor.X, corridor.Y)) break;
				siteMap.SetTile(corridor.X, corridor.Y, MapTile.Corridor);
			}
		}

		ClearSite();
		var site = new Node3D { Name = id.Replace('.', '_') };
		var framedEdges = new HashSet<(Vector3I Center, bool Horizontal)>();
		foreach (var (room, offset) in rooms)
		{
			// Site tile (x, z) is centred TileSize·(x, z) from the site's origin; a room's tile (0, 0) sits at its Origin.
			var position = new Vector3I((offset.X + Margin) * Room.TileSize, 0, (offset.Y + Margin) * Room.TileSize) - room.Origin;
			room.Position = position;
			site.AddChild(room);
			framedEdges.UnionWith(room.FramedEdges.Select(edge => (edge.Center + position, edge.Horizontal)));
		}
		try
		{
			BuildCorridors(site, siteMap, framedEdges);
		}
		catch
		{
			site.Free();
			throw;
		}
		// The room map's middle stands on the arena's origin.
		site.Position = -new Vector3(Margin * Room.TileSize + (map.Width - 1) * Room.TileSize / 2f, 0,
			Margin * Room.TileSize + (map.Height - 1) * Room.TileSize / 2f);
		Arena.Structure.AddChild(site);
		Airflow?.Bind(site);
		_site = site;

		Vector3 TileCenter(Vector2I tile) => site.Position + new Vector3(tile.X * Room.TileSize, 0, tile.Y * Room.TileSize);
		// The doorway nearest the camera, which looks down -Z, so the room lies ahead of the hero on screen.
		var (entranceTile, outward) = doorways.OrderByDescending(doorway => doorway.Tile.Y + doorway.Direction.Y)
			.ThenBy(doorway => doorway.Tile.X).First();
		Entrance = TileCenter(entranceTile + margin + outward);
		Inward = new Vector3(-outward.X, 0, -outward.Y);

		var floor = new List<Rect2>();
		for (int x = 0; x < siteMap.Width; x++)
		for (int z = 0; z < siteMap.Height; z++)
			if (siteMap.IsWalkable(x, z)) floor.Add(RoomFootprint.TileArea(TileCenter(new Vector2I(x, z))));
		var corner = TileCenter(Vector2I.Zero) - new Vector3(Room.TileSize, 0, Room.TileSize) / 2;
		Arena.Bounds = new Rect2(corner.X, corner.Z, siteMap.Width * Room.TileSize, siteMap.Height * Room.TileSize);
		Arena.FloorAreas = floor;
		Arena.RebuildNavigation();
		FrameOverview();
	}

	/// <summary>
	/// Lays the realm's corridor floor on every corridor tile of <paramref name="map"/> and walls it in, leaving the edges
	/// the rooms' doorway frames hold. The rooms keep their own walls, dividers included, in their own grids.
	/// </summary>
	private void BuildCorridors(Node3D site, MapData map, HashSet<(Vector3I Center, bool Horizontal)> framedEdges)
	{
		var corridors = Realm.Corridors;
		var tiles = new List<(Vector2I Tile, Vector3I Center)>();
		for (int x = 0; x < map.Width; x++)
		for (int z = 0; z < map.Height; z++)
			if (map.IsCorridor(x, z)) tiles.Add((new Vector2I(x, z), new Vector3I(x * Room.TileSize, 0, z * Room.TileSize)));
		if (corridors == null || tiles.Count == 0) return;

		using var rng = GenerationRandom.Create(Seed, GenerationRandom.Decoration, "harness-corridors");
		var floorGrid = CreateGrid("CorridorFloors", CorridorFloors);
		foreach (var (_, center) in tiles) floorGrid.SetCellItem(center, corridors.GetCorridorTileIndex(rng), 0);
		site.AddChild(floorGrid);

		var wallGrid = CreateGrid("CorridorWalls", CorridorWalls);
		var wallItems = new Dictionary<string, int>();
		BoundaryWallBuilder.Build(
			wallGrid,
			tiles,
			(tile, dx, dz) => map.HasWallToward(tile.X, tile.Y, new Vector2I(dx, dz)),
			(_, isPartition) =>
			{
				string name = corridors.Walls.DrawEdge(rng, isPartition);
				if (!wallItems.TryGetValue(name, out int item)) wallItems[name] = item = wallGrid.MeshLibrary.FindItemByName(name);
				return item;
			},
			framedEdges: framedEdges,
			structural: corridors.Walls.Pieces);
		site.AddChild(wallGrid);
	}

	private static GridMap CreateGrid(string name, MeshLibrary library) => new()
	{
		Name = name, CellSize = Vector3.One, CellCenterX = false, CellCenterY = false, CellCenterZ = false, MeshLibrary = library,
	};

	/// <summary>
	/// Colours every standable cell of a room type (reachable from the doorways, reserved for circulation, or an
	/// inaccessible pocket) when <see cref="ShowWalkability"/> is on, and returns the pockets' share of standable floor in
	/// percent either way.
	/// </summary>
	private float AddWalkabilityOverlay(RoomBlueprint blueprint)
	{
		var grid = blueprint.Grid;
		var reached = grid.Reachable(blueprint.Doorways.Select(doorway => RoomBlueprint.XZ(doorway.Approach)));
		var cells = new List<(Vector2 Center, Color Color)>();
		int reachable = 0, pockets = 0;
		for (int index = 0; index < reached.Length; index++)
		{
			var cell = new Vector2I(index % grid.Size.X, index / grid.Size.X);
			if (!grid.IsStandable(cell)) continue;
			if (reached[index]) reachable++;
			else pockets++;
			if (!ShowWalkability) continue;
			var center = grid.CellCenter(cell);
			var color = !reached[index] ? new Color(1, .15f, .1f, .8f)
				: blueprint.Reserved.Concat(blueprint.Landings).Any(area => area.HasPoint(center)) ? new Color(.3f, .55f, 1, .45f) : new Color(.2f, .95f, .35f, .3f);
			cells.Add((center, color));
		}
		float pocketShare = reachable + pockets == 0 ? 0 : 100f * pockets / (reachable + pockets);
		if (cells.Count == 0) return pocketShare;
		var multimesh = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, InstanceCount = cells.Count,
			Mesh = new QuadMesh { Size = Vector2.One * grid.CellSize * .8f, Orientation = PlaneMesh.OrientationEnum.Y },
		};
		for (int index = 0; index < cells.Count; index++)
		{
			multimesh.SetInstanceTransform(index, new Transform3D(Basis.Identity, new Vector3(cells[index].Center.X, .15f, cells[index].Center.Y)));
			multimesh.SetInstanceColor(index, cells[index].Color);
		}
		var material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		};
		blueprint.Room.AddChild(new MultiMeshInstance3D { Name = "WalkabilityOverlay", Multimesh = multimesh, MaterialOverride = material });
		return pocketShare;
	}

	private void ClearSite()
	{
		PreviewBlueprint = null;
		PreviewRoom = null;
		if (!IsInstanceValid(_site)) return;
		_site.GetParent()?.RemoveChild(_site);
		_site.QueueFree();
		_site = null;
	}

	/// <summary>Restores the hero and stands them at the entrance, facing into the room.</summary>
	public void ReturnToEntrance()
	{
		Hero.Reset();
		Player.Velocity = Vector3.Zero;
		Player.GlobalTransform = new Transform3D(Basis.LookingAt(Inward), Entrance + Vector3.Up * Player.GlobalPosition.Y);
		Player.MovementComponent.SetLookAtDirection(Inward);
		Player.ResetPhysicsInterpolation();
	}

	private void ApplyCamera()
	{
		if (OverviewCamera == null) return;
		if (Overview) OverviewCamera.MakeCurrent();
		else OverviewCamera.ClearCurrent(true);
	}

	/// <summary>Frames the staged site from above, beside the control panel on the right.</summary>
	private void FrameOverview()
	{
		var bounds = Arena.Bounds;
		float extent = Mathf.Max(bounds.Size.X, bounds.Size.Y);
		var viewport = GetViewport().GetVisibleRect().Size;
		float aspect = Mathf.Max(1, viewport.X) / Mathf.Max(1, viewport.Y);
		float panelFraction = Mathf.Clamp((Controls.GetCombinedMinimumSize().X + 56) / Mathf.Max(1, viewport.X), 0, .75f);
		float height = Mathf.Max(extent, extent / (aspect * (1 - panelFraction)));
		var center = bounds.GetCenter();
		float targetX = center.X + height * aspect * panelFraction / 2;
		OverviewCamera.Size = height;
		OverviewCamera.Position = new(targetX, height * 1.3f, center.Y + height * .6f);
		OverviewCamera.LookAt(new Vector3(targetX, 0, center.Y));
	}

	private void SyncControls()
	{
		if (_realmPicker == null) return;
		_syncing = true;
		var type = Recipes.ElementAtOrDefault(SelectedRecipe) as RoomType;
		_realmPicker.Select(SelectedRealm);
		_recipePicker.Select(SelectedRecipe);
		Fill(_shapeRow, _shapePicker, type?.Shapes.Select(ShapeName), ShapeOverride + 1, "Seeded");
		Fill(_wallsRow, _wallsPicker, type?.WallsIn(Realm.Theme).Select(style => Local(style.Id)), WallsOverride + 1, "Seeded");
		Fill(_floorRow, _floorPicker, type?.FloorsIn(Realm.Theme).Select(style => Local(style.Id)), FloorOverride + 1, "Seeded");
		Fill(_columnRow, _columnPicker, type == null || type.Columns.Length == 0 ? null : type.Columns.Select(pattern => pattern.Id),
			ColumnsOverride == RoomAssemblyOptions.Seeded ? 0 : ColumnsOverride == RoomAssemblyOptions.Off ? 1 : ColumnsOverride + 2,
			$"Type chance ({type?.ColumnChance:P0})", "Off");
		Fill(null, _motifPicker, _motifIds, SelectedMotif, "Every motif", "No motif");
		_seedInput.SetValueNoSignal(Seed);
		_furnishingsToggle.SetPressedNoSignal(Furnishings);
		_walkabilityToggle.SetPressedNoSignal(ShowWalkability);
		_walkabilityToggle.Visible = type != null;
		_overviewToggle.SetPressedNoSignal(Overview);
		_syncing = false;
	}

	private static void Fill(HBoxContainer row, OptionButton picker, IEnumerable<string> items, int selected, params string[] defaults)
	{
		picker.Clear();
		if (row != null) row.Visible = items != null;
		if (items == null) return;
		foreach (var item in defaults.Concat(items)) picker.AddItem(item);
		picker.Select(Mathf.Clamp(selected, 0, picker.ItemCount - 1));
	}
}