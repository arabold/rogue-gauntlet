using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public enum RoomPreviewMode { Room, Floor }

/// <summary>
/// Inspect each realm's room types alone or in the production floor pipeline. Every axis — shape, walls, floor,
/// columns, furnishing — can be overridden, and a walkability overlay shows reachable floor (green), reserved
/// circulation (blue) and inaccessible pockets (red).
/// </summary>
public partial class RoomGenerationHarness : Node3D
{
	private const string FloorProfilesDir = "res://scenes/levels/generators/floor_profiles";
	private const string RoomGroupsDir = "res://scenes/levels/rooms/room_groups";
	/// <summary>Trap gauntlet rooms: designed and previewed here, kept out of the room pool until floors place them.</summary>
	private const string GauntletRoomsDir = "res://scenes/levels/rooms/room_types/gauntlets";

	/// <summary>The production generator: its room pool and realms are what the harness previews.</summary>
	[Export] public MapGenerator Generator { get; set; }
	[Export] public Airflow Airflow { get; set; }
	[Export] public int SelectedRealm { get; set; }
	[Export] public int SelectedProfile { get; set; } = -1;
	[Export] public Node3D PreviewRoot { get; set; }
	[Export] public Camera3D PreviewCamera { get; set; }
	[Export] public Container Controls { get; set; }
	[Export] public Label Status { get; set; }
	[Export] public RoomPreviewMode Mode { get; set; } = RoomPreviewMode.Room;
	[Export] public int SelectedRecipe { get; set; }
	[Export] public uint Seed { get; set; } = 23;
	[Export] public bool Furnishings { get; set; } = true;
	[Export] public bool ShowWalkability { get; set; } = true;
	/// <summary>Room-type overrides: an index into the type's palette, or <see cref="RoomAssemblyOptions.Seeded"/>.</summary>
	[Export] public int ShapeOverride { get; set; } = RoomAssemblyOptions.Seeded;
	[Export] public int WallsOverride { get; set; } = RoomAssemblyOptions.Seeded;
	[Export] public int FloorOverride { get; set; } = RoomAssemblyOptions.Seeded;
	/// <summary>Column pattern index, <see cref="RoomAssemblyOptions.Seeded"/> (type's chance) or <see cref="RoomAssemblyOptions.Off"/>.</summary>
	[Export] public int ColumnsOverride { get; set; } = RoomAssemblyOptions.Seeded;
	/// <summary>The floor motifs a single room previews with: 0 every motif step, 1 none, then the realm's motifs one by one.</summary>
	[Export] public int SelectedMotif { get; set; }

	public Room PreviewRoom { get; private set; }
	public RoomBlueprint PreviewBlueprint { get; private set; }
	public string LastError { get; private set; } = "";
	public Realm Realm => Generator.Realms[SelectedRealm];
	/// <summary>The room pool narrowed to the selected realm.</summary>
	public RoomPool Pool { get; private set; }
	public IReadOnlyList<(FloorProfile Resource, string Path)> Profiles { get; private set; } = [];
	/// <summary>The realm's ordinary rooms, entrances/exits, and matching room groups.</summary>
	public IReadOnlyList<RoomDefinition> Recipes => _recipes;
	/// <summary>The realm's motifs, in the order the motif picker lists them after "every" and "none".</summary>
	public IReadOnlyList<string> MotifIds => _motifIds;
	/// <summary>The motifs a single room assembles with: null runs every motif step (<see cref="SelectedMotif"/> 0).</summary>
	public IReadOnlySet<string> PreviewMotifs => SelectedMotif switch
	{
		0 => null,
		1 => new HashSet<string>(),
		_ => new HashSet<string> { _motifIds[Mathf.Clamp(SelectedMotif - 2, 0, _motifIds.Count - 1)] },
	};

	private readonly List<RoomDefinition> _recipes = [];
	private readonly List<string> _motifIds = [];
	private Node3D _previewNode;
	private int _quarterTurns;
	private OptionButton _modePicker, _profilePicker, _realmPicker, _recipePicker, _shapePicker, _wallsPicker, _floorPicker, _columnPicker, _motifPicker;
	private HBoxContainer _profileRow, _shapeRow, _wallsRow, _floorRow, _columnRow, _motifRow;
	private SpinBox _seedInput;
	private CheckButton _furnishingsToggle, _walkabilityToggle;
	private Button _rotateButton;
	private bool _syncing;
	private bool _regenerationPending;

	public override void _Ready()
	{
		if (Generator == null || PreviewRoot == null || PreviewCamera == null || Controls == null || Status == null
			|| Generator.Rooms == null || Generator.Realms.Length == 0 || Generator.Realms.Any(realm => realm == null))
		{
			LastError = "Assign the map generator (with an assembled room pool and realms), preview root, camera, controls and status label in the harness scene.";
			GD.PushError(LastError);
			return;
		}
		LoadProfiles();
		BuildControls();
		ApplyRealm(Mathf.Clamp(SelectedRealm, 0, Generator.Realms.Length - 1));
		Regenerate();
	}

	private void LoadProfiles()
	{
		Profiles = DebugCatalog.Resources<FloorProfile>(FloorProfilesDir);
		if (Profiles.Count == 0) return;
		if (SelectedProfile >= 0 && SelectedProfile < Profiles.Count) return;

		string preferredPath = Generator.Profile?.ResourcePath;
		int preferredIdx = -1;
		int clusterIdx = -1;
		for (int i = 0; i < Profiles.Count; i++)
		{
			if (!string.IsNullOrEmpty(preferredPath) && Profiles[i].Path == preferredPath)
			{
				preferredIdx = i;
				break;
			}
			if (Profiles[i].Path.EndsWith("/cluster.tres", StringComparison.OrdinalIgnoreCase))
			{
				clusterIdx = i;
			}
		}
		SelectedProfile = preferredIdx >= 0 ? preferredIdx : clusterIdx >= 0 ? clusterIdx : 0;
	}

	private void BuildControls()
	{
		_modePicker = Picker("View", out _, index => Mode = (RoomPreviewMode)index);
		_modePicker.AddItem("Single room");
		_modePicker.AddItem("Generated floor");
		_profilePicker = Picker("Profile", out _profileRow, index => SelectedProfile = index);
		foreach (var (_, path) in Profiles)
		{
			_profilePicker.AddItem(Local(path.GetFile().GetBaseName()).Replace('_', ' ').Capitalize());
		}
		_realmPicker = Picker("Realm", out _, SelectRealm);
		foreach (var realm in Generator.Realms) _realmPicker.AddItem($"{realm.Id.Capitalize()} ({realm.FirstDepth}–{realm.LastDepth})");
		_recipePicker = Picker("Recipe", out _, SelectRecipe);
		_shapePicker = Picker("Shape", out _shapeRow, index => ShapeOverride = index - 1);
		_wallsPicker = Picker("Walls", out _wallsRow, index => WallsOverride = index - 1);
		_floorPicker = Picker("Floor", out _floorRow, index => FloorOverride = index - 1);
		_columnPicker = Picker("Columns", out _columnRow,
			index => ColumnsOverride = index == 0 ? RoomAssemblyOptions.Seeded : index == 1 ? RoomAssemblyOptions.Off : index - 2);
		_motifPicker = Picker("Motifs", out _motifRow, index => SelectedMotif = index);

		_seedInput = new SpinBox { MinValue = 0, MaxValue = uint.MaxValue, Step = 1, CustomMinimumSize = new(180, 0) };
		_seedInput.ValueChanged += value => { if (!_syncing) { Seed = (uint)value; RequestRegeneration(); } };
		AddControlRow("Seed", _seedInput);

		_furnishingsToggle = Toggle("Furnishings", enabled => Furnishings = enabled);
		_walkabilityToggle = Toggle("Walkability overlay", enabled => ShowWalkability = enabled);
		var buttons = new HBoxContainer();
		buttons.AddThemeConstantOverride("separation", 8);
		Controls.AddChild(buttons);
		AddButton(buttons, "Rebuild", RequestRegeneration);
		AddButton(buttons, "Next seed", () => { Seed = unchecked(Seed + 1); RequestRegeneration(); });
		_rotateButton = AddButton(buttons, "Rotate 90°", () => RotatePreview(1));
		Controls.AddChild(new Label { Text = "R: next seed  ·  A: furnishings  ·  W: walkability  ·  F: room/floor\n1–9 / 0: recipe  ·  Q / E: rotate room" });
	}

	private OptionButton Picker(string title, out HBoxContainer row, Action<int> select)
	{
		var picker = new OptionButton();
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
		toggle.Toggled += enabled => { if (!_syncing) { set(enabled); RequestRegeneration(); } };
		Controls.AddChild(toggle);
		return toggle;
	}

	private HBoxContainer AddControlRow(string title, Control control)
	{
		var row = new HBoxContainer();
		row.AddChild(new Label { Text = title, CustomMinimumSize = new(80, 0) });
		control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		row.AddChild(control);
		Controls.AddChild(row);
		return row;
	}

	private static Button AddButton(Container parent, string title, Action action)
	{
		var button = new Button { Text = title, CustomMinimumSize = new(120, 36), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		button.Pressed += action;
		parent.AddChild(button);
		return button;
	}

	private static string RecipeName(RoomDefinition def) => def == null ? "Missing room type" : Local(def.Id).Replace('_', ' ').Capitalize();

	/// <summary>An ID without its realm prefix (garrison.hall → hall).</summary>
	private static string Local(string id) => id.Split('.').Last();

	private static string ShapeName(RoomShape shape) =>
		$"{shape.GetType().Name.Replace("Shape", "")} {shape.MinimumSize.X}–{shape.MaximumSize.X} × {shape.MinimumSize.Y}–{shape.MaximumSize.Y}";

	/// <summary>Previews another realm: its room types, and its first depth for the floor view's rooms and corridors.</summary>
	public void SelectRealm(int index)
	{
		ApplyRealm(index);
		SelectRecipe(0);
	}

	/// <summary>Selects a floor profile recipe when viewing generated floors.</summary>
	public void SelectProfile(int index)
	{
		if (Profiles.Count == 0) return;
		SelectedProfile = Mathf.Clamp(index, 0, Profiles.Count - 1);
		RequestRegeneration();
	}

	private void ApplyRealm(int index)
	{
		SelectedRealm = index;
		Generator.DungeonDepth = Realm.FirstDepth;
		Pool = Generator.Rooms.ForRealm(Realm.Theme);
		_recipes.Clear();
		_recipes.AddRange(Pool.AllDefinitions);
		// Rooms the realm schedules beyond the catalog, such as a floor plan's own rooms it turns into headlines.
		_recipes.AddRange(Realm.Rooms.Select(entry => entry?.Room).Where(room => room != null && !_recipes.Contains(room)));
		_motifIds.Clear();
		_motifIds.AddRange(Realm.Motifs.Where(motif => motif != null).Select(motif => motif.Id).Distinct());
		SelectedMotif = Mathf.Clamp(SelectedMotif, 0, _motifIds.Count + 1);
		foreach (var (group, _) in DebugCatalog.Resources<RoomGroup>(RoomGroupsDir))
		{
			if (group != null && group.SupportsRealm(Realm.Theme) && !_recipes.Contains(group))
			{
				_recipes.Add(group);
			}
		}
		_recipes.AddRange(DebugCatalog.Resources<RoomType>(GauntletRoomsDir).Select(entry => entry.Resource)
			.Where(type => type.SupportsRealm(Realm.Theme) && !_recipes.Contains(type)));
		_recipePicker.Clear();
		foreach (var recipe in Recipes) _recipePicker.AddItem(RecipeName(recipe));
	}

	/// <summary>Changing recipes resets the viewing rotation and overrides while preserving the seed.</summary>
	public void SelectRecipe(int index)
	{
		SelectedRecipe = index;
		ShapeOverride = WallsOverride = FloorOverride = ColumnsOverride = RoomAssemblyOptions.Seeded;
		_quarterTurns = 0;
		RequestRegeneration();
	}

	public void RotatePreview(int steps)
	{
		if (Mode != RoomPreviewMode.Room) return;
		_quarterTurns = ((_quarterTurns + steps) % 4 + 4) % 4;
		RequestRegeneration();
	}

	/// <summary>Coalesce UI events so a preview is replaced at most once per process frame.</summary>
	private void RequestRegeneration() => _regenerationPending = true;

	public override void _Process(double delta)
	{
		if (!_regenerationPending) return;
		Regenerate();
	}

	/// <summary>Both views use production generation; single rooms rotate as a whole after assembly.</summary>
	public void Regenerate()
	{
		_regenerationPending = false;
		LastError = "";
		try
		{
			if (SelectedRecipe < 0 || SelectedRecipe >= Recipes.Count || Recipes[SelectedRecipe] == null)
				throw new InvalidOperationException("Select an assigned room type.");
			Generator.Visible = Mode == RoomPreviewMode.Floor;
			PreviewRoot.Visible = Mode == RoomPreviewMode.Room;
			if (Mode == RoomPreviewMode.Room) GenerateRoom();
			else
			{
				ClearRoom();
				if (Profiles.Count > 0 && SelectedProfile >= 0 && SelectedProfile < Profiles.Count)
				{
					Generator.Profile = Profiles[SelectedProfile].Resource;
				}
				Generator.Seed = Seed;
				Generator.GenerateMap(false);
				var usedCells = Generator.FloorGridMap.GetUsedCells();
				if (usedCells.Count > 0)
				{
					int minX = int.MaxValue, maxX = int.MinValue;
					int minZ = int.MaxValue, maxZ = int.MinValue;
					foreach (var cell in usedCells)
					{
						if (cell.X < minX) minX = cell.X;
						if (cell.X > maxX) maxX = cell.X;
						if (cell.Z < minZ) minZ = cell.Z;
						if (cell.Z > maxZ) maxZ = cell.Z;
					}
					var center = new Vector3((minX + maxX) * 0.5f, 0, (minZ + maxZ) * 0.5f);
					float width = maxX - minX + Room.TileSize;
					float depth = maxZ - minZ + Room.TileSize;
					FramePreviewAt(center, Mathf.Max(width, depth) + 4);
				}
				else
				{
					FramePreview(Mathf.Max(Generator.Map.Width, Generator.Map.Height) * Room.TileSize + 12);
				}
				string profileName = Profiles.Count > 0 && SelectedProfile >= 0 && SelectedProfile < Profiles.Count
					? Local(Profiles[SelectedProfile].Path.GetFile().GetBaseName())
					: Generator.Profile?.GetType().Name ?? "custom";
				Status.Text = $"{Realm.Id.Capitalize()} · {profileName} · depth {Generator.DungeonDepth} · Seed {Seed} · {Generator.Floor.Rooms.Count} rooms";
			}
			Airflow?.Bind(Mode == RoomPreviewMode.Room ? PreviewRoot : Generator);
		}
		catch (InvalidOperationException error)
		{
			LastError = error.Message;
			Status.Text = $"Generation failed: {LastError}";
			GD.PushError(LastError);
		}
		SyncControls();
	}

	private void SyncControls()
	{
		if (_modePicker == null) return;
		_syncing = true;
		var definition = Recipes.ElementAtOrDefault(SelectedRecipe);
		var type = definition as RoomType;
		bool room = Mode == RoomPreviewMode.Room;
		_modePicker.Select((int)Mode);
		_profileRow.Visible = !room && Profiles.Count > 0;
		if (Profiles.Count > 0) _profilePicker.Select(Mathf.Clamp(SelectedProfile, 0, Profiles.Count - 1));
		_realmPicker.Select(SelectedRealm);
		_recipePicker.Select(SelectedRecipe);
		_recipePicker.Disabled = !room;
		Fill(_shapeRow, _shapePicker, type?.Shapes.Select(ShapeName), ShapeOverride + 1, "Seeded");
		Fill(_wallsRow, _wallsPicker, type?.WallsIn(Realm.Theme).Select(style => Local(style.Id)), WallsOverride + 1, "Seeded");
		Fill(_floorRow, _floorPicker, type?.FloorsIn(Realm.Theme).Select(style => Local(style.Id)), FloorOverride + 1, "Seeded");
		Fill(_columnRow, _columnPicker, type == null || type.Columns.Length == 0 ? null : type.Columns.Select(pattern => pattern.Id),
			ColumnsOverride == RoomAssemblyOptions.Seeded ? 0 : ColumnsOverride == RoomAssemblyOptions.Off ? 1 : ColumnsOverride + 2,
			$"Type chance ({type?.ColumnChance:P0})", "Off");
		Fill(_motifRow, _motifPicker, _motifIds, SelectedMotif, "Every motif", "No motif");
		_motifRow.Visible = room;
		bool singleType = room && type != null;
		foreach (var row in new[] { _shapeRow, _wallsRow, _floorRow, _columnRow }) row.Visible &= singleType;
		_seedInput.SetValueNoSignal(Seed);
		_furnishingsToggle.SetPressedNoSignal(Furnishings);
		_furnishingsToggle.Visible = room;
		_walkabilityToggle.SetPressedNoSignal(ShowWalkability);
		_walkabilityToggle.Visible = singleType;
		_rotateButton.Disabled = !room;
		_syncing = false;
	}

	private static void Fill(HBoxContainer row, OptionButton picker, IEnumerable<string> items, int selected, params string[] defaults)
	{
		picker.Clear();
		row.Visible = items != null;
		if (items == null) return;
		foreach (var item in defaults.Concat(items)) picker.AddItem(item);
		picker.Select(Mathf.Clamp(selected, 0, picker.ItemCount - 1));
	}

	private void GenerateRoom()
	{
		var definition = Recipes[SelectedRecipe];
		if (definition is RoomGroup group)
		{
			GenerateCluster(group);
			return;
		}

		var blueprint = AssembleRoomType(definition);
		var candidate = blueprint.Room;
		try
		{
			candidate.Rotate(_quarterTurns);
			ClearRoom();
			PreviewRoot.AddChild(candidate);
			var map = candidate.Map;
			candidate.Position = -(candidate.Origin + new Vector3I(map.Width - 1, 0, map.Height - 1) * (Room.TileSize / 2));
			PreviewBlueprint = blueprint;
			PreviewRoom = candidate;
			_previewNode = candidate;
			FramePreview(Mathf.Max(map.Width, map.Height) * Room.TileSize + 12);
		}
		catch
		{
			if (candidate != _previewNode) candidate.Free();
			throw;
		}
	}

	private void GenerateCluster(RoomGroup group)
	{
		var options = new RoomAssemblyOptions { Furnish = Furnishings, Realm = Realm.Theme, Motifs = PreviewMotifs };
		var cluster = RoomAssembler.AssembleCluster(group, Seed, options, Pool);
		Node3D container = null;
		try
		{
			cluster.Rotate(_quarterTurns);
			container = new Node3D { Name = group.Id };
			var map = cluster.Map;
			container.Position = -new Vector3((map.Width - 1) * 0.5f * Room.TileSize, 0, (map.Height - 1) * 0.5f * Room.TileSize);

			var dividerCells = new HashSet<Vector3I>();
			var framedEdges = new HashSet<(Vector3I Center, bool Horizontal)>();
			foreach (var member in cluster.Members)
			{
				container.AddChild(member.Room);
				var gridOffset = new Vector3I(member.Offset.X * Room.TileSize, 0, member.Offset.Y * Room.TileSize) - member.Room.Origin;
				member.Room.Position = gridOffset;
				foreach (var cell in member.Room.DividerCells) dividerCells.Add(cell + gridOffset);
				foreach (var (center, horizontal) in member.Room.FramedEdges) framedEdges.Add((center + gridOffset, horizontal));
			}
			int memberCount = cluster.Members.Count;
			cluster.DetachRooms();

			BuildClusterBridges(container, map, dividerCells, framedEdges);
			ClearRoom();
			PreviewRoot.AddChild(container);
			PreviewRoom = cluster.PrimaryRoom;
			PreviewBlueprint = null;
			_previewNode = container;
			FramePreview(Mathf.Max(map.Width, map.Height) * Room.TileSize + 12);
			Status.Text = $"{group.Id} (group) · {memberCount} rooms · {map.Width}×{map.Height} tiles · Seed {Seed} · {_quarterTurns * 90}°";
		}
		catch
		{
			cluster.Free();
			container?.Free();
			throw;
		}
	}

	private void BuildClusterBridges(
		Node3D container,
		MapData map,
		HashSet<Vector3I> dividerCells,
		HashSet<(Vector3I Center, bool Horizontal)> framedEdges)
	{
		var corridors = Realm?.Corridors;
		if (corridors == null || Generator.FloorGridMap == null || Generator.WallGridMap == null) return;

		var corridorTiles = new List<(Vector2I Tile, Vector3I Center)>();
		for (int x = 0; x < map.Width; x++)
		{
			for (int z = 0; z < map.Height; z++)
			{
				if (map.IsCorridor(x, z))
				{
					corridorTiles.Add((new Vector2I(x, z), new Vector3I(x * Room.TileSize, 0, z * Room.TileSize)));
				}
			}
		}
		if (corridorTiles.Count == 0) return;

		using var rng = GenerationRandom.Create(Seed, GenerationRandom.Decoration, "harness-group-bridges");
		var floorGrid = CreateBridgeGrid("BridgeFloors", Generator.FloorGridMap.MeshLibrary);
		foreach (var (_, center) in corridorTiles)
		{
			floorGrid.SetCellItem(center, corridors.GetCorridorTileIndex(rng), 0);
		}
		container.AddChild(floorGrid);

		var wallGrid = CreateBridgeGrid("BridgeWalls", Generator.WallGridMap.MeshLibrary);
		var wallItemIds = new Dictionary<string, int>();
		BoundaryWallBuilder.Build(
			wallGrid,
			corridorTiles,
			(tile, dx, dz) => map.HasWallToward(tile.X, tile.Y, new Vector2I(dx, dz)),
			(_, isPartition) =>
			{
				string name = corridors.Walls.DrawEdge(rng, isPartition);
				if (!wallItemIds.TryGetValue(name, out int id))
				{
					id = wallGrid.MeshLibrary.FindItemByName(name);
					wallItemIds[name] = id;
				}
				return id;
			},
			dividerCells: dividerCells,
			framedEdges: framedEdges,
			structural: corridors.Walls.Pieces);
		container.AddChild(wallGrid);
	}

	private static GridMap CreateBridgeGrid(string name, MeshLibrary library) => new()
	{
		Name = name,
		CellSize = Vector3.One,
		CellCenterX = false,
		CellCenterY = false,
		CellCenterZ = false,
		MeshLibrary = library,
	};

	private RoomBlueprint AssembleRoomType(RoomDefinition definition)
	{
		if (definition is not RoomType type)
		{
			throw new InvalidOperationException($"Room preview currently requires a RoomType, got {definition?.Id ?? "null"}.");
		}
		var options = new RoomAssemblyOptions
		{
			Shape = ShapeOverride, Walls = WallsOverride, Floor = FloorOverride, Columns = ColumnsOverride, Furnish = Furnishings, Realm = Realm.Theme,
			Motifs = PreviewMotifs,
		};
		var blueprint = RoomAssembler.Build(type, Seed, options);
		var (reachable, pockets) = AddWalkabilityOverlay(blueprint);
		Status.Text = $"{type.Id} · Seed {Seed} · {_quarterTurns * 90}°\n"
			+ $"{blueprint.Footprint.Width} × {blueprint.Footprint.Length} tile bounds · {blueprint.Footprint.Count} footprint tiles\n"
			+ $"{ShapeName(blueprint.Shape)}\n"
			+ $"walls {Local(blueprint.Walls.Id)} · floor {Local(blueprint.Floor.Id)} · columns {blueprint.Columns?.Id ?? "none"}\n"
			+ $"{blueprint.Pieces.Count} props · {blueprint.Doorways.Count} doorways · pockets {(reachable + pockets == 0 ? 0 : 100f * pockets / (reachable + pockets)):F1}% of standable floor";
		return blueprint;
	}

	/// <summary>Colours every standable cell: reachable from the doorways, reserved for circulation, or an inaccessible pocket.</summary>
	private (int Reachable, int Pockets) AddWalkabilityOverlay(RoomBlueprint blueprint)
	{
		var grid = blueprint.Grid;
		var reached = grid.Reachable(blueprint.Doorways.Select(doorway => RoomBlueprint.XZ(doorway.Approach)));
		var cells = ShowWalkability ? new List<(Vector2 Center, Color Color)>() : null;
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
		if (!ShowWalkability || cells.Count == 0) return (reachable, pockets);
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
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha, NoDepthTest = false,
		};
		blueprint.Room.AddChild(new MultiMeshInstance3D { Name = "WalkabilityOverlay", Multimesh = multimesh, MaterialOverride = material });
		return (reachable, pockets);
	}

	private void ClearRoom()
	{
		PreviewBlueprint = null;
		if (_previewNode == null) return;
		PreviewRoot.RemoveChild(_previewNode);
		_previewNode.QueueFree();
		_previewNode = null;
		PreviewRoom = null;
	}

	private void FramePreview(float extent) => FramePreviewAt(Vector3.Zero, extent);

	private void FramePreviewAt(Vector3 center, float extent)
	{
		var viewport = GetViewport().GetVisibleRect().Size;
		float aspect = Mathf.Max(1, viewport.X) / Mathf.Max(1, viewport.Y);
		float panelFraction = Mathf.Clamp((Controls.GetCombinedMinimumSize().X + 56) / Mathf.Max(1, viewport.X), 0, .75f);
		float height = Mathf.Max(extent, extent / (aspect * (1 - panelFraction)));
		float targetX = center.X - height * aspect * panelFraction / 2;
		PreviewCamera.Size = height;
		PreviewCamera.Position = new(targetX, center.Y + height * 1.3f, center.Z + height * .6f);
		PreviewCamera.LookAt(new Vector3(targetX, center.Y, center.Z));
	}

	public override void _UnhandledKeyInput(InputEvent input)
	{
		if (input is not InputEventKey { Pressed: true, Echo: false } key) return;
		if (key.Keycode == Key.R) { Seed = unchecked(Seed + 1); RequestRegeneration(); }
		if (key.Keycode == Key.A) { Furnishings = !Furnishings; RequestRegeneration(); }
		if (key.Keycode == Key.W) { ShowWalkability = !ShowWalkability; RequestRegeneration(); }
		if (key.Keycode == Key.F) { Mode = Mode == RoomPreviewMode.Room ? RoomPreviewMode.Floor : RoomPreviewMode.Room; RequestRegeneration(); }
		if (key.Keycode == Key.Q) RotatePreview(-1);
		if (key.Keycode == Key.E) RotatePreview(1);
		if (Mode == RoomPreviewMode.Room && key.Keycode == Key.Key0 && Recipes.Count > 9) SelectRecipe(9);
		if (Mode == RoomPreviewMode.Room && key.Keycode >= Key.Key1 && key.Keycode <= Key.Key9 && (int)(key.Keycode - Key.Key1) < Recipes.Count)
			SelectRecipe((int)(key.Keycode - Key.Key1));
	}
}