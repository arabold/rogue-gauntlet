using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Previews whole floors from the production generator: any realm's first depth with any floor profile and seed, from a
/// bright top-down overview. Rooms alone, and walking through them, live in the room generation harness.
/// </summary>
public partial class MapGenerationHarness : Node3D
{
	private const string FloorProfilesDir = "res://scenes/levels/generators/floor_profiles";

	/// <summary>The production generator: its realms and room pool are what the harness previews.</summary>
	[Export] public MapGenerator Generator { get; set; }
	[Export] public Airflow Airflow { get; set; }
	[Export] public int SelectedRealm { get; set; }
	/// <summary>Index into <see cref="Profiles"/>; out of range picks the generator's own profile, else the cluster layout.</summary>
	[Export] public int SelectedProfile { get; set; } = -1;
	[Export] public Camera3D PreviewCamera { get; set; }
	[Export] public Container Controls { get; set; }
	[Export] public Label Status { get; set; }
	[Export] public uint Seed { get; set; } = 23;

	public string LastError { get; private set; } = "";
	public Realm Realm => Generator.Realms[SelectedRealm];
	public IReadOnlyList<(FloorProfile Resource, string Path)> Profiles { get; private set; } = [];

	private OptionButton _profilePicker, _realmPicker;
	private SpinBox _seedInput;
	private bool _syncing;
	private bool _regenerationPending;

	public override void _Ready()
	{
		if (Generator == null || PreviewCamera == null || Controls == null || Status == null
			|| Generator.Rooms == null || Generator.Realms.Length == 0 || Generator.Realms.Any(realm => realm == null))
		{
			LastError = "Assign the map generator (with an assembled room pool and realms), camera, controls and status label in the harness scene.";
			GD.PushError(LastError);
			return;
		}
		LoadProfiles();
		BuildControls();
		SelectedRealm = Mathf.Clamp(SelectedRealm, 0, Generator.Realms.Length - 1);
		Regenerate();
	}

	private void LoadProfiles()
	{
		Profiles = DebugCatalog.Resources<FloorProfile>(FloorProfilesDir);
		if (Profiles.Count == 0 || SelectedProfile >= 0 && SelectedProfile < Profiles.Count) return;
		string preferred = Generator.Profile?.ResourcePath;
		int index = Profiles.ToList().FindIndex(entry => !string.IsNullOrEmpty(preferred) && entry.Path == preferred);
		if (index < 0) index = Profiles.ToList().FindIndex(entry => entry.Path.EndsWith("/cluster.tres", StringComparison.OrdinalIgnoreCase));
		SelectedProfile = Mathf.Max(index, 0);
	}

	private void BuildControls()
	{
		_realmPicker = Picker("Realm", SelectRealm);
		foreach (var realm in Generator.Realms) _realmPicker.AddItem($"{realm.Id.Capitalize()} ({realm.FirstDepth}–{realm.LastDepth})");
		_profilePicker = Picker("Profile", SelectProfile);
		foreach (var (_, path) in Profiles) _profilePicker.AddItem(path.GetFile().GetBaseName().Replace('_', ' ').Capitalize());
		_profilePicker.GetParent<Control>().Visible = Profiles.Count > 0;

		_seedInput = new SpinBox { MinValue = 0, MaxValue = uint.MaxValue, Step = 1, CustomMinimumSize = new(180, 0) };
		_seedInput.ValueChanged += value => { if (!_syncing) { Seed = (uint)value; RequestRegeneration(); } };
		AddControlRow("Seed", _seedInput);

		var buttons = new HBoxContainer();
		buttons.AddThemeConstantOverride("separation", 8);
		Controls.AddChild(buttons);
		AddButton(buttons, "Rebuild", RequestRegeneration);
		AddButton(buttons, "Next seed", NextSeed);
		Controls.AddChild(new Label { Text = "R: next seed" });
	}

	private OptionButton Picker(string title, Action<int> select)
	{
		var picker = new OptionButton();
		picker.ItemSelected += index => { if (!_syncing) select((int)index); };
		AddControlRow(title, picker);
		return picker;
	}

	private void AddControlRow(string title, Control control)
	{
		var row = new HBoxContainer();
		row.AddChild(new Label { Text = title, CustomMinimumSize = new(80, 0) });
		control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		row.AddChild(control);
		Controls.AddChild(row);
	}

	private static void AddButton(Container parent, string title, Action action)
	{
		var button = new Button { Text = title, CustomMinimumSize = new(120, 36), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		button.Pressed += action;
		parent.AddChild(button);
	}

	/// <summary>Previews another realm's first depth, with its rooms, corridors and floor plan.</summary>
	public void SelectRealm(int index)
	{
		SelectedRealm = Mathf.Clamp(index, 0, Generator.Realms.Length - 1);
		RequestRegeneration();
	}

	/// <summary>Builds the next floors from another floor profile: its layout, map size and room count.</summary>
	public void SelectProfile(int index)
	{
		if (Profiles.Count == 0) return;
		SelectedProfile = Mathf.Clamp(index, 0, Profiles.Count - 1);
		RequestRegeneration();
	}

	public void NextSeed()
	{
		Seed = unchecked(Seed + 1);
		RequestRegeneration();
	}

	/// <summary>Coalesce UI events so a floor is rebuilt at most once per process frame.</summary>
	private void RequestRegeneration() => _regenerationPending = true;

	public override void _Process(double delta)
	{
		if (_regenerationPending) Regenerate();
	}

	/// <summary>Generates the selected realm's first depth with the production pipeline and frames it.</summary>
	public void Regenerate()
	{
		_regenerationPending = false;
		LastError = "";
		try
		{
			Generator.DungeonDepth = Realm.FirstDepth;
			if (SelectedProfile >= 0 && SelectedProfile < Profiles.Count) Generator.Profile = Profiles[SelectedProfile].Resource;
			Generator.Seed = Seed;
			Generator.GenerateMap(false);
			FrameFloor();
			string profileName = SelectedProfile >= 0 && SelectedProfile < Profiles.Count
				? Profiles[SelectedProfile].Path.GetFile().GetBaseName()
				: Generator.Profile?.GetType().Name ?? "custom";
			Status.Text = $"{Realm.Id.Capitalize()} · {profileName} · depth {Generator.DungeonDepth} · Seed {Seed} · {Generator.Floor.Rooms.Count} rooms";
			Airflow?.Bind(Generator);
		}
		catch (InvalidOperationException error)
		{
			LastError = error.Message;
			Status.Text = $"Generation failed: {LastError}";
			GD.PushError(LastError);
		}
		SyncControls();
	}

	private void FrameFloor()
	{
		var cells = Generator.FloorGridMap.GetUsedCells();
		if (cells.Count == 0)
		{
			FrameAt(Vector3.Zero, Mathf.Max(Generator.Map.Width, Generator.Map.Height) * Room.TileSize + 12);
			return;
		}
		var bounds = new Rect2(cells[0].X, cells[0].Z, 0, 0);
		foreach (var cell in cells) bounds = bounds.Expand(new Vector2(cell.X, cell.Z));
		var center = bounds.GetCenter();
		FrameAt(new Vector3(center.X, 0, center.Y), Mathf.Max(bounds.Size.X, bounds.Size.Y) + Room.TileSize + 4);
	}

	/// <summary>Frames <paramref name="extent"/> metres around <paramref name="center"/> beside the control panel.</summary>
	private void FrameAt(Vector3 center, float extent)
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

	private void SyncControls()
	{
		if (_realmPicker == null) return;
		_syncing = true;
		_realmPicker.Select(SelectedRealm);
		if (Profiles.Count > 0) _profilePicker.Select(Mathf.Clamp(SelectedProfile, 0, Profiles.Count - 1));
		_seedInput.SetValueNoSignal(Seed);
		_syncing = false;
	}

	public override void _UnhandledKeyInput(InputEvent input)
	{
		if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.R }) NextSeed();
	}
}