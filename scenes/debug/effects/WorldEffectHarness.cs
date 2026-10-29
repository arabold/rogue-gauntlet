using Godot;
using static EffectPreviewControls;

public enum WorldEffectPreview { TrapDiscovery, SecretDiscovery, Loot, Blood, DoorXray, ChestXray }

/// <summary>Repeatable previews of production effects, without dungeon generation or combat requirements.</summary>
public partial class WorldEffectHarness : Node3D
{
	[Export] public Node3D PreviewRoot { get; set; }
	[Export] public Camera3D PreviewCamera { get; set; }
	[Export] public Container Controls { get; set; }
	[Export] public PackedScene TrapScene { get; set; }
	[Export] public PackedScene DoorScene { get; set; }
	[Export] public PackedScene ChestScene { get; set; }
	[Export] public PackedScene BloodScene { get; set; }
	[Export] public PackedScene LootModel { get; set; }
	[Export] public MeshLibrary FloorLibrary { get; set; }
	[Export] public MeshLibrary WallLibrary { get; set; }
	[Export] public WorldEffectPreview Effect { get; set; }
	[Export] public float RevealGlow { get; set; } = 1.8f;
	[Export] public bool Blend { get; set; } = true;
	public FloorTrap PreviewTrap { get; private set; }
	public Door PreviewDoor { get; private set; }
	public Chest PreviewChest { get; private set; }
	public GridMap PreviewOccluder { get; private set; }
	public bool IsXrayPreview => Effect is WorldEffectPreview.DoorXray or WorldEffectPreview.ChestXray;
	public string LastResult { get; private set; } = "";
	private OptionButton _selection;
	private Label _status;
	private Control _glowRow, _blendControl, _wallRow;
	private Button _replayButton;
	private Node3D _lootModel;
	private int _wallItem;
	private bool _replayQueued;
	public override void _Ready()
	{
		BuildControls(); ResetPreview();
	}
	private void BuildControls()
	{
		Controls.AddChild(new Label { Text = "World effects" });
		_selection = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		foreach (string label in new[] { "Trap discovery", "Secret door discovery", "Loot glow", "Blood hit", "Door x-ray", "Chest x-ray" }) _selection.AddItem(label);
		_selection.Selected = (int)Effect;
		_selection.ItemSelected += index => SelectEffect((int)index);
		Row(Controls, "Effect", _selection, 130);
		var glow = new HSlider { MinValue = 0, MaxValue = 4, Step = 0.1, Value = RevealGlow, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		glow.ValueChanged += value => { RevealGlow = (float)value; RequestReset(); };
		_glowRow = Row(Controls, "Reveal glow", glow, 130);
		var blend = new CheckBox { Text = "Blend surfaces", ButtonPressed = Blend };
		blend.Toggled += value => { Blend = value; RequestReset(); }; Controls.AddChild(blend); _blendControl = blend;
		var wall = new OptionButton();
		foreach (int id in new[] { 0, 10, 22 }) wall.AddItem(WallLibrary.GetItemName(id), id);
		wall.ItemSelected += index => { _wallItem = wall.GetItemId((int)index); RequestReset(); };
		_wallRow = Row(Controls, "Wall variant", wall, 130);
		_replayButton = Button(Controls, "Replay", Replay);
		CameraHelp(Controls);
		_status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; Controls.AddChild(_status);
	}
	public void SelectEffect(int index)
	{
		if (index < 0 || index > (int)WorldEffectPreview.ChestXray) return;
		Effect = (WorldEffectPreview)index; _selection.Selected = index; ResetPreview();
	}
	private void RequestReset()
	{
		if (_replayQueued) return;
		_replayQueued = true; Callable.From(() => { _replayQueued = false; if (IsInsideTree()) ResetPreview(); }).CallDeferred();
	}
	private GridMap Surface(MeshLibrary library)
	{
		var grid = new GridMap { MeshLibrary = library, CellSize = Vector3.One, CellCenterX = false, CellCenterY = false, CellCenterZ = false };
		PreviewRoot.AddChild(grid); return grid;
	}
	private void Configure(DiscoveryPulse effect)
	{
		effect.GlowStrength = RevealGlow; effect.Blend = Blend;
	}
	public void Replay()
	{
		if (IsXrayPreview)
		{
			PreviewOccluder.Visible = !PreviewOccluder.Visible;
			UpdateOccluderButton(); return;
		}
		ResetPreview();
		if (PreviewTrap != null) PreviewTrap.Reveal();
		if (PreviewDoor != null) PreviewDoor.Secret.Reveal();
		if (PreviewTrap != null || PreviewDoor != null) _status.Text = "Revealing. Press Replay to start again.";
	}
	/// <summary>Stage the concealed surface without starting discovery; selection and tuning never reveal it.</summary>
	public void ResetPreview()
	{
		foreach (var child in PreviewRoot.GetChildren()) { PreviewRoot.RemoveChild(child); child.QueueFree(); }
		PreviewTrap = null; PreviewDoor = null; PreviewChest = null; PreviewOccluder = null; _lootModel = null;
		PreviewCamera.Current = true;
		var floor = Surface(FloorLibrary); floor.SetCellItem(Vector3I.Zero, 0);
		switch (Effect)
		{
			case WorldEffectPreview.TrapDiscovery:
				PreviewTrap = TrapScene.Instantiate<FloorTrap>();
				PreviewRoot.AddChild(PreviewTrap); Configure(PreviewTrap.DiscoveryEffect);
				PreviewTrap.Initialize("preview:trap", 42, floor, Vector3I.Zero);
				LastResult = "Original floor. Press Reveal to discover the trap.";
				break;
			case WorldEffectPreview.SecretDiscovery:
				var walls = Surface(WallLibrary); walls.SetCellItem(Vector3I.Zero, _wallItem);
				PreviewDoor = DoorScene.Instantiate<Door>(); PreviewRoot.AddChild(PreviewDoor);
				Configure(PreviewDoor.Secret.DiscoveryEffect);
				PreviewDoor.Secret.Initialize(walls, Vector3I.Zero, Vector3.Back, new SecretDetectionProfile(), 42);
				PreviewDoor.Secret.Discovery.ProcessMode = ProcessModeEnum.Disabled; PreviewDoor.SetIndicatorVisible(false);
				LastResult = "Original wall. Press Reveal to discover the closed door.";
				break;
			case WorldEffectPreview.Loot:
				_lootModel = LootModel.Instantiate<Node3D>(); _lootModel.Position = Vector3.Up;
				PreviewRoot.AddChild(_lootModel);
				PreviewRoot.AddChild(new LootBeam { BeamColor = new Color(1, 0.78f, 0.08f) });
				LastResult = "Production loot glow and rising particles. Orbit around the model to inspect it.";
				break;
			case WorldEffectPreview.Blood:
				var blood = BloodScene.Instantiate<Node3D>(); blood.Position = Vector3.Up * 1.2f;
				PreviewRoot.AddChild(blood);
				LastResult = "Production blood-hit particles with their gameplay lifetime. Press Replay.";
				break;
			case WorldEffectPreview.DoorXray:
			case WorldEffectPreview.ChestXray:
				for (int x = -4; x <= 4; x += 4) for (int z = -4; z <= 4; z += 4) floor.SetCellItem(new Vector3I(x, 0, z), 0);
				// Real opaque wall meshes stand between the camera and the closed production prop.
				PreviewOccluder = Surface(WallLibrary); PreviewOccluder.Name = "OccludingWall";
				PreviewOccluder.CellScale = 1.5f;
				PreviewOccluder.SetCellItem(new Vector3I(-3, 0, 3), _wallItem);
				PreviewOccluder.SetCellItem(new Vector3I(3, 0, 3), _wallItem);
				if (Effect == WorldEffectPreview.DoorXray)
				{
					PreviewDoor = DoorScene.Instantiate<Door>(); PreviewRoot.AddChild(PreviewDoor);
					PreviewDoor.SetIndicatorVisible(true);
					LastResult = "Closed production door behind a wall. The button toggles the wall; orbit to inspect partial occlusion."
						+ (GameDebug.DoorXrayEnabled ? "" : " Door x-ray is off in the game's debug settings.");
				}
				else
				{
					PreviewChest = ChestScene.Instantiate<Chest>(); PreviewRoot.AddChild(PreviewChest);
					LastResult = "Unopened production chest behind a wall. The button toggles the wall; orbit to inspect body and lid."
						+ (GameDebug.LootXrayEnabled ? "" : " Loot x-ray is off in the game's debug settings.");
				}
				break;
		}
		bool discovery = Effect is WorldEffectPreview.TrapDiscovery or WorldEffectPreview.SecretDiscovery;
		_glowRow.Visible = _blendControl.Visible = discovery;
		_wallRow.Visible = Effect == WorldEffectPreview.SecretDiscovery || IsXrayPreview;
		_replayButton.Visible = Effect != WorldEffectPreview.Loot;
		_replayButton.Text = discovery ? "Reveal" : "Replay";
		if (_status != null) _status.Text = LastResult;
		if (IsXrayPreview) UpdateOccluderButton();
	}
	private void UpdateOccluderButton()
	{
		_replayButton.Text = PreviewOccluder.Visible ? "Hide wall" : "Show wall";
	}

	public override void _Process(double delta)
	{
		if (_lootModel != null && IsInstanceValid(_lootModel)) _lootModel.RotateY((float)delta * .7f);
	}
}