using System;
using System.Linq;
using Godot;

public enum RoomPreviewMode { Room, Floor }

/// <summary>Inspect coded room recipes alone or in the production floor pipeline, with explicit scene wiring.</summary>
public partial class RoomGenerationHarness : Node3D
{
	[Export] public MixedRoomFactory Factory { get; set; }
	[Export] public MapGenerator Generator { get; set; }
	[Export] public Node3D PreviewRoot { get; set; }
	[Export] public Camera3D PreviewCamera { get; set; }
	[Export] public Container Controls { get; set; }
	[Export] public Label Status { get; set; }
	[Export] public RoomPreviewMode Mode { get; set; } = RoomPreviewMode.Room;
	[Export] public int SelectedRecipe { get; set; }
	[Export] public uint Seed { get; set; } = 23;
	[Export] public bool Furnishings { get; set; } = true;

	public Room PreviewRoom { get; private set; }
	public string LastError { get; private set; } = "";
	private int _quarterTurns;
	private OptionButton _modePicker;
	private OptionButton _recipePicker;
	private SpinBox _seedInput;
	private CheckButton _furnishingsToggle;
	private Button _rotateButton;
	private bool _syncing;
	private bool _regenerationPending;

	public override void _Ready()
	{
		if (Factory == null || Generator == null || PreviewRoot == null || PreviewCamera == null || Controls == null || Status == null
			|| Generator.RoomFactory != Factory || Factory.RoomGenerators.Length == 0)
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
		_modePicker = new OptionButton();
		_modePicker.AddItem("Single room");
		_modePicker.AddItem("Generated floor");
		_modePicker.ItemSelected += index => { if (!_syncing) { Mode = (RoomPreviewMode)index; RequestRegeneration(); } };
		AddControlRow("View", _modePicker);

		_recipePicker = new OptionButton();
		foreach (var recipe in Factory.RoomGenerators)
			_recipePicker.AddItem(recipe == null ? "Missing recipe" : recipe.ResourcePath.GetFile().GetBaseName().Replace("garrison_", "").Replace("_generator", "").Capitalize());
		_recipePicker.ItemSelected += index => { if (!_syncing) SelectRecipe((int)index); };
		AddControlRow("Recipe", _recipePicker);

		_seedInput = new SpinBox { MinValue = 0, MaxValue = uint.MaxValue, Step = 1, CustomMinimumSize = new(180, 0) };
		_seedInput.ValueChanged += value => { if (!_syncing) { Seed = (uint)value; RequestRegeneration(); } };
		AddControlRow("Seed", _seedInput);

		_furnishingsToggle = new CheckButton { Text = "Furnishings" };
		_furnishingsToggle.Toggled += enabled => { if (!_syncing) { Furnishings = enabled; RequestRegeneration(); } };
		Controls.AddChild(_furnishingsToggle);
		var buttons = new HBoxContainer();
		buttons.AddThemeConstantOverride("separation", 8);
		Controls.AddChild(buttons);
		AddButton(buttons, "Rebuild", RequestRegeneration);
		AddButton(buttons, "Next seed", () => { Seed = unchecked(Seed + 1); RequestRegeneration(); });
		_rotateButton = AddButton(buttons, "Rotate 90°", () => RotatePreview(1));
		Controls.AddChild(new Label { Text = "R: next seed  ·  A: furnishings  ·  F: room/floor\n1–5: recipe  ·  Q / E: rotate room" });
	}

	private void AddControlRow(string title, Control control)
	{
		var row = new HBoxContainer();
		row.AddChild(new Label { Text = title, CustomMinimumSize = new(60, 0) });
		control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		row.AddChild(control);
		Controls.AddChild(row);
	}

	private static Button AddButton(Container parent, string title, Action action)
	{
		var button = new Button { Text = title, CustomMinimumSize = new(120, 36), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		button.Pressed += action;
		parent.AddChild(button);
		return button;
	}

	/// <summary>Changing recipes resets the viewing rotation while preserving the seed.</summary>
	public void SelectRecipe(int index)
	{
		SelectedRecipe = index;
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

	/// <summary>Both views use production generation; single rooms rotate before the normal furnishing pass.</summary>
	public void Regenerate()
	{
		_regenerationPending = false;
		LastError = "";
		try
		{
			if (SelectedRecipe < 0 || SelectedRecipe >= Factory.RoomGenerators.Length || Factory.RoomGenerators[SelectedRecipe] == null)
				throw new InvalidOperationException("Select an assigned room recipe.");
			Generator.Visible = Mode == RoomPreviewMode.Floor;
			PreviewRoot.Visible = Mode == RoomPreviewMode.Room;
			if (Mode == RoomPreviewMode.Room) GenerateRoom();
			else
			{
				ClearRoom();
				Generator.Seed = Seed;
				Generator.EnableFurnishings = Furnishings;
				Generator.GenerateMap(false);
				FramePreview(Mathf.Max(Generator.MapWidth, Generator.MapDepth) * Room.TileSize + 12);
				Status.Text = $"Floor · Seed {Seed} · {Generator.Floor.Rooms.Count} rooms · {Generator.ArrangementCount} arrangements";
			}
		}
		catch (InvalidOperationException error)
		{
			LastError = error.Message;
			Status.Text = $"Generation failed: {LastError}";
			GD.PushError(LastError);
		}
		if (_modePicker == null) return;
		_syncing = true;
		_modePicker.Select((int)Mode);
		_recipePicker.Select(SelectedRecipe);
		_recipePicker.Disabled = Mode == RoomPreviewMode.Floor;
		_seedInput.SetValueNoSignal(Seed);
		_furnishingsToggle.SetPressedNoSignal(Furnishings);
		_rotateButton.Disabled = Mode == RoomPreviewMode.Floor;
		_syncing = false;
	}

	private void GenerateRoom()
	{
		var recipe = Factory.RoomGenerators[SelectedRecipe];
		using var rng = new RandomNumberGenerator { Seed = Seed };
		var candidate = new Room { Name = "PreviewRoom" };
		try
		{
			if (!recipe.PopulateRoom(candidate, rng, standalone: true, furnish: false))
				throw new InvalidOperationException(string.Join("\n", recipe.Validate()));
			candidate.Rotate(_quarterTurns);
			if (Furnishings) recipe.ShellBuilder.FurnishStandaloneRoom(candidate, Seed, recipe.CreateInteriorPool(), recipe.Dressing);
			ClearRoom();
			PreviewRoot.AddChild(candidate);
			var center = candidate.Bounds.Position + (candidate.Bounds.Size - Vector2I.One * Room.TileSize) / 2;
			candidate.Position = new(-center.X, 0, -center.Y);
			PreviewRoom = candidate;
			FramePreview(Mathf.Max(candidate.Bounds.Size.X, candidate.Bounds.Size.Y) + 12);
			Status.Text = $"{recipe.GetType().Name} · Seed {Seed} · {_quarterTurns * 90}°\n"
				+ $"{candidate.Map.Width} × {candidate.Map.Height} tiles · {candidate.GetChildren().OfType<RoomArrangement>().Count()} arrangements";
		}
		catch
		{
			if (candidate != PreviewRoom) candidate.Free();
			throw;
		}
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
		if (key.Keycode == Key.F) { Mode = Mode == RoomPreviewMode.Room ? RoomPreviewMode.Floor : RoomPreviewMode.Room; RequestRegeneration(); }
		if (key.Keycode == Key.Q) RotatePreview(-1);
		if (key.Keycode == Key.E) RotatePreview(1);
		if (Mode == RoomPreviewMode.Room && key.Keycode >= Key.Key1 && key.Keycode <= Key.Key5) SelectRecipe((int)(key.Keycode - Key.Key1));
	}
}