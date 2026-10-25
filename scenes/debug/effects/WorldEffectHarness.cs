using Godot;

public enum WorldEffectPreview { TrapDiscovery, SecretDiscovery, Loot, Blood, Invisibility, DoorXray, ChestXray }

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
	[Export] public PackedScene PlayerScene { get; set; }
	[Export] public PackedScene MonsterScene { get; set; }
	[Export] public MeshLibrary FloorLibrary { get; set; }
	[Export] public MeshLibrary WallLibrary { get; set; }
	[Export] public WorldEffectPreview Effect { get; set; }
	[Export] public float RevealGlow { get; set; } = 1.8f;
	[Export] public bool Blend { get; set; } = true;
	[Export] public bool AutoReplay { get; set; }
	public FloorTrap PreviewTrap { get; private set; }
	public Door PreviewDoor { get; private set; }
	public Chest PreviewChest { get; private set; }
	public GridMap PreviewOccluder { get; private set; }
	public bool IsXrayPreview => Effect is WorldEffectPreview.DoorXray or WorldEffectPreview.ChestXray;
	public Player PreviewPlayer { get; private set; }
	public EnemyBase PreviewMonster { get; private set; }
	private bool _invisibilityActive = true;
	public string LastResult { get; private set; } = "";
	private OptionButton _selection;
	private Label _status;
	private Control _glowRow, _blendControl, _wallRow, _autoControl;
	private Button _replayButton;
	private bool _waitingForReveal;
	private Node3D _lootModel;
	private float _elapsed;
	private float _yaw = 0.55f;
	private float _zoom = 12;
	private int _wallItem;
	private bool _replayQueued;
	public override void _Ready()
	{
		BuildControls(); UpdateCamera(); ResetPreview();
	}
	private void BuildControls()
	{
		Controls.AddChild(new Label { Text = "World effects" });
		_selection = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		foreach (string label in new[] { "Trap discovery", "Secret door discovery", "Loot glow", "Blood hit", "Invisibility", "Door x-ray", "Chest x-ray" }) _selection.AddItem(label);
		_selection.Selected = (int)Effect;
		_selection.ItemSelected += index => SelectEffect((int)index);
		AddRow("Effect", _selection);
		var glow = new HSlider { MinValue = 0, MaxValue = 4, Step = 0.1, Value = RevealGlow, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		glow.ValueChanged += value => { RevealGlow = (float)value; RequestReset(); };
		_glowRow = AddRow("Reveal glow", glow);
		var blend = new CheckBox { Text = "Blend surfaces", ButtonPressed = Blend };
		blend.Toggled += value => { Blend = value; RequestReset(); }; Controls.AddChild(blend); _blendControl = blend;
		var wall = new OptionButton();
		foreach (int id in new[] { 0, 10, 22 }) wall.AddItem(WallLibrary.GetItemName(id), id);
		wall.ItemSelected += index => { _wallItem = wall.GetItemId((int)index); RequestReset(); };
		_wallRow = AddRow("Wall variant", wall);
		var auto = new CheckBox { Text = "Auto replay", ButtonPressed = AutoReplay };
		auto.Toggled += value => { AutoReplay = value; _elapsed = 0; }; Controls.AddChild(auto); _autoControl = auto;
		var replay = new Button { Text = "Replay  [R / Space]", FocusMode = Control.FocusModeEnum.None };
		_replayButton = replay; replay.Pressed += Replay; Controls.AddChild(replay);
		Controls.AddChild(new Label { Text = "1–7: choose effect\nQ / E: orbit · wheel: zoom", AutowrapMode = TextServer.AutowrapMode.WordSmart });
		_status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; Controls.AddChild(_status);
	}
	private Control AddRow(string text, Control control)
	{
		var row = new HBoxContainer();
		row.AddChild(new Label { Text = text, CustomMinimumSize = new Vector2(130, 0) });
		row.AddChild(control); Controls.AddChild(row); return row;
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
			PreviewOccluder.Visible = !PreviewOccluder.Visible; _elapsed = 0;
			UpdateOccluderButton(); return;
		}
		if (Effect == WorldEffectPreview.Invisibility)
		{
			_invisibilityActive = !_invisibilityActive;
			PreviewPlayer.Invisibility.SetInvisible(_invisibilityActive);
			PreviewMonster.Invisibility.SetInvisible(_invisibilityActive);
			_elapsed = 0; UpdateInvisibilityStatus(); return;
		}
		ResetPreview();
		if (PreviewTrap != null) PreviewTrap.Reveal();
		if (PreviewDoor != null) PreviewDoor.Secret.Reveal();
		_waitingForReveal = false;
		if (PreviewTrap != null || PreviewDoor != null) _status.Text = "Revealing. Press R / Space to replay from the original surface.";
	}
	/// <summary>Stage the concealed surface without starting discovery; selection and tuning never reveal it.</summary>
	public void ResetPreview()
	{
		foreach (var child in PreviewRoot.GetChildren()) { PreviewRoot.RemoveChild(child); child.QueueFree(); }
		PreviewTrap = null; PreviewDoor = null; PreviewChest = null; PreviewOccluder = null; PreviewPlayer = null; PreviewMonster = null; _lootModel = null; _elapsed = 0;
		var floor = Surface(FloorLibrary); floor.SetCellItem(Vector3I.Zero, 0);
		switch (Effect)
		{
			case WorldEffectPreview.TrapDiscovery:
				PreviewTrap = TrapScene.Instantiate<FloorTrap>();
				PreviewRoot.AddChild(PreviewTrap); Configure(PreviewTrap.DiscoveryEffect);
				PreviewTrap.Initialize("preview:trap", 42, floor, Vector3I.Zero);
				LastResult = "Original floor. Press Reveal or R / Space to discover the trap.";
				break;
			case WorldEffectPreview.SecretDiscovery:
				var walls = Surface(WallLibrary); walls.SetCellItem(Vector3I.Zero, _wallItem);
				PreviewDoor = DoorScene.Instantiate<Door>(); PreviewRoot.AddChild(PreviewDoor);
				Configure(PreviewDoor.Secret.DiscoveryEffect);
				PreviewDoor.Secret.Initialize(walls, Vector3I.Zero, Vector3.Back, new SecretDetectionProfile(), 42);
				PreviewDoor.Secret.Discovery.ProcessMode = ProcessModeEnum.Disabled; PreviewDoor.SetIndicatorVisible(false);
				LastResult = "Original wall. Press Reveal or R / Space to discover the closed door.";
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
				LastResult = "Production blood-hit particles with their gameplay lifetime. Replay or enable auto replay.";
				break;
			case WorldEffectPreview.Invisibility:
				for (int x = -4; x <= 4; x += 4) for (int z = -4; z <= 4; z += 4) floor.SetCellItem(new Vector3I(x, 0, z), 0);
				PreviewPlayer = PlayerScene.Instantiate<Player>();
				StageActor(PreviewPlayer, new Vector3(-2.2f, 0, 0), "Idle");
				PreviewMonster = MonsterScene.Instantiate<EnemyBase>();
				StageActor(PreviewMonster, new Vector3(2.2f, 0, 0), "Idle_Combat");
				PreviewPlayer.Invisibility.SetInvisible(_invisibilityActive, true);
				PreviewMonster.Invisibility.SetInvisible(_invisibilityActive, true);
				LastResult = "Production player and skeleton, including equipment. R / Space toggles invisibility; orbit to inspect the faint edges.";
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
					LastResult = "Closed production door behind a wall. R / Space toggles the wall; orbit to inspect partial occlusion."
						+ (GameDebug.DoorXrayEnabled ? "" : " Door x-ray is off in the game's debug settings.");
				}
				else
				{
					PreviewChest = ChestScene.Instantiate<Chest>(); PreviewRoot.AddChild(PreviewChest);
					LastResult = "Unopened production chest behind a wall. R / Space toggles the wall; orbit to inspect body and lid."
						+ (GameDebug.LootXrayEnabled ? "" : " Loot x-ray is off in the game's debug settings.");
				}
				break;
		}
		_waitingForReveal = Effect is WorldEffectPreview.TrapDiscovery or WorldEffectPreview.SecretDiscovery;
		bool discovery = _waitingForReveal;
		_glowRow.Visible = _blendControl.Visible = discovery;
		_wallRow.Visible = Effect == WorldEffectPreview.SecretDiscovery || IsXrayPreview;
		_autoControl.Visible = Effect != WorldEffectPreview.Loot;
		_replayButton.Visible = Effect != WorldEffectPreview.Loot;
		_replayButton.Text = discovery ? "Reveal  [R / Space]" : "Replay  [R / Space]";
		if (_status != null) _status.Text = LastResult;
		if (Effect == WorldEffectPreview.Invisibility) UpdateInvisibilityStatus();
		if (IsXrayPreview) UpdateOccluderButton();
	}
	private void StageActor(CharacterBody3D actor, Vector3 position, string animation)
	{
		// Use the production assembly, keeping only animation and the visual effect active.
		actor.ProcessMode = ProcessModeEnum.Disabled; actor.Position = position; actor.Rotation = Vector3.Up * Mathf.Pi;
		actor.CollisionLayer = actor.CollisionMask = 0; PreviewRoot.AddChild(actor); actor.Visible = true;
		actor.RemoveFromGroup("player"); actor.RemoveFromGroup("enemy");
		foreach (Node node in actor.FindChildren("*", "", true, false))
		{
			if (node is AnimationTree tree) tree.Active = false;
			if (node is Light3D light) light.Visible = false;
			if (node is AnimationPlayer player && player.HasAnimation(animation))
			{ player.ProcessMode = ProcessModeEnum.Always; player.Play(animation); }
			if (node is InvisibilityComponent cloak) cloak.ProcessMode = ProcessModeEnum.Always;
		}
	}
	private void UpdateInvisibilityStatus()
	{
		_replayButton.Text = _invisibilityActive ? "Become visible  [R / Space]" : "Become invisible  [R / Space]";
	}

	private void UpdateOccluderButton()
	{
		_replayButton.Text = PreviewOccluder.Visible ? "Hide wall  [R / Space]" : "Show wall  [R / Space]";
	}

	public override void _Process(double delta)
	{
		if (_lootModel != null && IsInstanceValid(_lootModel)) _lootModel.RotateY((float)delta * 0.7f);
		_elapsed += (float)delta;
		if (AutoReplay && (Effect == WorldEffectPreview.Invisibility || IsXrayPreview))
		{ if (_elapsed >= 3) Replay(); return; }
		if (AutoReplay && Effect != WorldEffectPreview.Loot)
		{
			float duration = PreviewTrap?.DiscoveryEffect.Duration ?? PreviewDoor?.Secret.DiscoveryEffect.Duration ?? 1;
			if (_waitingForReveal && _elapsed >= 1)
			{ PreviewTrap?.Reveal(); PreviewDoor?.Secret.Reveal(); _waitingForReveal = false; _elapsed = 0; }
			else if (!_waitingForReveal && _elapsed >= Mathf.Max(duration, 1) + 1) ResetPreview();
		}
	}
	private void UpdateCamera()
	{
		var center = Vector3.Up * 1.3f;
		PreviewCamera.GlobalPosition = center + new Vector3(Mathf.Sin(_yaw) * 12, 9, Mathf.Cos(_yaw) * 12);
		PreviewCamera.LookAt(center); PreviewCamera.Size = _zoom;
	}
	public override void _UnhandledInput(InputEvent input)
	{
		if (input is InputEventKey { Pressed: true, Echo: false } key)
		{
			if (key.Keycode >= Key.Key1 && key.Keycode <= Key.Key7) SelectEffect((int)(key.Keycode - Key.Key1));
			else if (key.Keycode is Key.R or Key.Space) Replay();
			else if (key.Keycode is Key.Q or Key.E) { _yaw += key.Keycode == Key.Q ? -0.25f : 0.25f; UpdateCamera(); }
			else return;
			GetViewport().SetInputAsHandled();
		}
		if (input is InputEventMouseButton { Pressed: true } mouse && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
		{
			_zoom = Mathf.Clamp(_zoom + (mouse.ButtonIndex == MouseButton.WheelUp ? -1 : 1), 6, 24); UpdateCamera();
			GetViewport().SetInputAsHandled();
		}
	}
}