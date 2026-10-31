using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public enum RoomPreviewMode { Room, Floor }

/// <summary>
/// Inspect room types alone or in the production floor pipeline. Every axis — shape, walls, floor, columns,
/// furnishing — can be overridden, and a walkability overlay shows reachable floor (green), reserved circulation
/// (blue) and inaccessible pockets (red).
/// </summary>
public partial class RoomGenerationHarness : Node3D
{
	[Export] public AssembledRoomFactory Factory { get; set; }
	[Export] public MapGenerator Generator { get; set; }
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

	public Room PreviewRoom { get; private set; }
	public RoomBlueprint PreviewBlueprint { get; private set; }
	public string LastError { get; private set; } = "";
	/// <summary>Every room type the factory assembles: ordinary rooms, then entrances and exits.</summary>
	public IReadOnlyList<RoomType> Recipes => [.. Factory.RoomTypes, .. Factory.EntranceTypes, .. Factory.ExitTypes];

	private int _quarterTurns;
	private OptionButton _modePicker, _recipePicker, _shapePicker, _wallsPicker, _floorPicker, _columnPicker;
	private HBoxContainer _shapeRow, _wallsRow, _floorRow, _columnRow;
	private SpinBox _seedInput;
	private CheckButton _furnishingsToggle, _walkabilityToggle;
	private Button _rotateButton;
	private bool _syncing;
	private bool _regenerationPending;

	public override void _Ready()
	{
		if (Factory == null || Generator == null || PreviewRoot == null || PreviewCamera == null || Controls == null || Status == null
			|| Generator.RoomFactory != Factory || Recipes.Count == 0)
		{
			LastError = "Assign the factory, its map generator, preview root, camera, controls and status label in the harness scene.";
			GD.PushError(LastError);
			return;
		}
		BuildControls();
		Regenerate();
	}

	private void BuildControls()
	{
		_modePicker = Picker("View", out _, index => Mode = (RoomPreviewMode)index);
		_modePicker.AddItem("Single room");
		_modePicker.AddItem("Generated floor");
		_recipePicker = Picker("Recipe", out _, SelectRecipe);
		foreach (var recipe in Recipes) _recipePicker.AddItem(RecipeName(recipe));
		_shapePicker = Picker("Shape", out _shapeRow, index => ShapeOverride = index - 1);
		_wallsPicker = Picker("Walls", out _wallsRow, index => WallsOverride = index - 1);
		_floorPicker = Picker("Floor", out _floorRow, index => FloorOverride = index - 1);
		_columnPicker = Picker("Columns", out _columnRow,
			index => ColumnsOverride = index == 0 ? RoomAssemblyOptions.Seeded : index == 1 ? RoomAssemblyOptions.Off : index - 2);

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

	private static string RecipeName(RoomType type) => type == null ? "Missing room type" : type.Id.Replace("garrison.", "").Replace('_', ' ').Capitalize();

	private static string ShapeName(RoomShape shape) =>
		$"{shape.GetType().Name.Replace("Shape", "")} {shape.MinimumSize.X}–{shape.MaximumSize.X} × {shape.MinimumSize.Y}–{shape.MaximumSize.Y}";

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
				Generator.Seed = Seed;
				Generator.GenerateMap(false);
				FramePreview(Mathf.Max(Generator.MapWidth, Generator.MapDepth) * Room.TileSize + 12);
				Status.Text = $"Floor · Seed {Seed} · {Generator.Floor.Rooms.Count} rooms";
			}
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
		var type = Recipes.ElementAtOrDefault(SelectedRecipe);
		bool room = Mode == RoomPreviewMode.Room;
		_modePicker.Select((int)Mode);
		_recipePicker.Select(SelectedRecipe);
		_recipePicker.Disabled = !room;
		Fill(_shapeRow, _shapePicker, type?.Shapes.Select(ShapeName), ShapeOverride + 1, "Seeded");
		Fill(_wallsRow, _wallsPicker, type?.Walls.Select(style => style.Id.Replace("garrison.", "")), WallsOverride + 1, "Seeded");
		Fill(_floorRow, _floorPicker, type?.Floors.Select(style => style.Id.Replace("garrison.", "")), FloorOverride + 1, "Seeded");
		Fill(_columnRow, _columnPicker, type == null || type.Columns.Length == 0 ? null : type.Columns.Select(pattern => pattern.Id),
			ColumnsOverride == RoomAssemblyOptions.Seeded ? 0 : ColumnsOverride == RoomAssemblyOptions.Off ? 1 : ColumnsOverride + 2,
			$"Type chance ({type?.ColumnChance:P0})", "Off");
		foreach (var row in new[] { _shapeRow, _wallsRow, _floorRow, _columnRow }) row.Visible &= room;
		_seedInput.SetValueNoSignal(Seed);
		_furnishingsToggle.SetPressedNoSignal(Furnishings);
		_furnishingsToggle.Visible = room;
		_walkabilityToggle.SetPressedNoSignal(ShowWalkability);
		_walkabilityToggle.Visible = room;
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
		var candidate = AssembleRoomType(Recipes[SelectedRecipe]);
		try
		{
			candidate.Rotate(_quarterTurns);
			ClearRoom();
			PreviewRoot.AddChild(candidate);
			var center = candidate.Bounds.Position + (candidate.Bounds.Size - Vector2I.One * Room.TileSize) / 2;
			candidate.Position = new(-center.X, 0, -center.Y);
			PreviewRoom = candidate;
			FramePreview(Mathf.Max(candidate.Bounds.Size.X, candidate.Bounds.Size.Y) + 12);
		}
		catch
		{
			if (candidate != PreviewRoom) candidate.Free();
			throw;
		}
	}

	private Room AssembleRoomType(RoomType type)
	{
		var options = new RoomAssemblyOptions { Shape = ShapeOverride, Walls = WallsOverride, Floor = FloorOverride, Columns = ColumnsOverride, Furnish = Furnishings };
		var blueprint = RoomAssembler.Build(type, Seed, options);
		PreviewBlueprint = blueprint;
		var (reachable, pockets) = AddWalkabilityOverlay(blueprint);
		Status.Text = $"{type.Id} · Seed {Seed} · {_quarterTurns * 90}°\n"
			+ $"{blueprint.Footprint.Width} × {blueprint.Footprint.Length} tiles · {ShapeName(blueprint.Shape)}\n"
			+ $"walls {blueprint.Walls.Id.Replace("garrison.", "")} · floor {blueprint.Floor.Id.Replace("garrison.", "")} · columns {blueprint.Columns?.Id ?? "none"}\n"
			+ $"{blueprint.Pieces.Count} props · {blueprint.Doorways.Count} doorways · pockets {(reachable + pockets == 0 ? 0 : 100f * pockets / (reachable + pockets)):F1}% of standable floor";
		return blueprint.Room;
	}

	/// <summary>Colours every standable cell: reachable from the doorways, reserved for circulation, or an inaccessible pocket.</summary>
	private (int Reachable, int Pockets) AddWalkabilityOverlay(RoomBlueprint blueprint)
	{
		var grid = blueprint.Grid;
		var reached = grid.Reachable(blueprint.Doorways.Select(doorway => RoomBlueprint.XZ(doorway.Approach)));
		var cells = new List<(Vector2 Center, Color Color)>();
		int reachable = 0, pockets = 0;
		for (int index = 0; index < reached.Length; index++)
		{
			var cell = new Vector2I(index % grid.Size.X, index / grid.Size.X);
			if (!grid.IsStandable(cell)) continue;
			var center = grid.CellCenter(cell);
			if (reached[index]) reachable++;
			else pockets++;
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
		if (PreviewRoom == null) return;
		PreviewRoot.RemoveChild(PreviewRoom);
		PreviewRoom.QueueFree();
		PreviewRoom = null;
	}

	private void FramePreview(float extent)
	{
		var viewport = GetViewport().GetVisibleRect().Size;
		float aspect = Mathf.Max(1, viewport.X) / Mathf.Max(1, viewport.Y);
		float panelFraction = Mathf.Clamp((Controls.GetCombinedMinimumSize().X + 56) / Mathf.Max(1, viewport.X), 0, .75f);
		float height = Mathf.Max(extent, extent / (aspect * (1 - panelFraction)));
		float centerX = -height * aspect * panelFraction / 2;
		PreviewCamera.Size = height;
		PreviewCamera.Position = new(centerX, height * 1.3f, height * .6f);
		PreviewCamera.LookAt(new Vector3(centerX, 0, 0));
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