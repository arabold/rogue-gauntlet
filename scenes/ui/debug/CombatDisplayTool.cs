using Godot;
using System;

/// <summary>
/// Combat diagnostics and presentation: attack hit volumes and helper meshes, hurtboxes, hit effects, movement dust and
/// dash trails, camera shake, slow motion, and the combat and AI logs. Each toggle applies at once, to actors already in
/// the scene and to any added later.
/// </summary>
[GlobalClass]
public partial class CombatDisplayTool : DebugTool
{
	private const double SlowMotionScale = .25;
	private static readonly StringName DebugMeshGroup = "debug_mesh";

	/// <summary>Shows attack hit volumes and every node in the <c>debug_mesh</c> group.</summary>
	[Export]
	public bool DebugMeshesEnabled
	{
		get;
		set
		{
			field = value;
			ApplyToScene();
		}
	}

	/// <summary>Outlines every hurtbox's shapes.</summary>
	[Export]
	public bool HurtboxesEnabled
	{
		get;
		set
		{
			field = value;
			ApplyToScene();
		}
	}

	/// <summary>Hit reactions, impact effects and the lunge on contact; off shows bare contacts.</summary>
	[Export]
	public bool EffectsEnabled
	{
		get;
		set
		{
			field = value;
			ApplyToScene();
		}
	} = true;

	/// <summary>Footstep dust, stop and turn puffs and the dash trail; off shows bare movement.</summary>
	[Export]
	public bool MovementEffectsEnabled
	{
		get;
		set
		{
			field = value;
			ApplyToScene();
		}
	} = true;

	/// <summary>Camera shake on the player's resolved contacts.</summary>
	[Export]
	public bool CameraShakeEnabled
	{
		get;
		set
		{
			field = value;
			ApplyToScene();
		}
	} = true;

	/// <summary>Runs the game at a quarter of the speed it had when slow motion was turned on.</summary>
	[Export]
	public bool SlowMotionEnabled
	{
		get;
		set
		{
			field = value;
			if (IsInsideTree()) { ApplySlowMotion(); }
		}
	}

	private double _normalTimeScale = 1;
	private bool _slowed;
	/// <summary>Ends the node-added watch; set while one runs.</summary>
	private Action _stopWatching;

	/// <summary>Whether actors need this tool's settings: only while one differs from the game's.</summary>
	private bool OverridesActors => DebugMeshesEnabled || HurtboxesEnabled || !EffectsEnabled || !MovementEffectsEnabled || !CameraShakeEnabled;

	public override void _EnterTree() => ApplySlowMotion();

	/// <summary>Applies the authored settings to the scene the tool joins; at the game's settings there is nothing to change.</summary>
	public override void _Ready() => ApplyToScene(onlyOverrides: true);

	public override void _ExitTree()
	{
		// Engine-wide switches would otherwise outlive the scene that set them.
		if (_slowed)
		{
			Engine.TimeScale = _normalTimeScale;
			_slowed = false;
		}
		if (DebugMeshesEnabled) { AttackController.GlobalDebugDrawEnabled = false; }
		_stopWatching?.Invoke();
		_stopWatching = null;
	}

	protected override void BuildMenu(PopupMenu menu)
	{
		AddToggle(menu, "Debug meshes", () => DebugMeshesEnabled, value => DebugMeshesEnabled = value);
		AddToggle(menu, "Hurtboxes", () => HurtboxesEnabled, value => HurtboxesEnabled = value);
		AddToggle(menu, "Hit effects", () => EffectsEnabled, value => EffectsEnabled = value);
		AddToggle(menu, "Movement effects", () => MovementEffectsEnabled, value => MovementEffectsEnabled = value);
		AddToggle(menu, "Camera shake", () => CameraShakeEnabled, value => CameraShakeEnabled = value);
		AddToggle(menu, "Slow motion", () => SlowMotionEnabled, value => SlowMotionEnabled = value);
		menu.AddSeparator();
		AddToggle(menu, "Combat logs", () => GameDebug.CombatLogsEnabled, value => GameDebug.CombatLogsEnabled = value);
		AddToggle(menu, "AI logs", () => GameDebug.AiLogsEnabled, value => GameDebug.AiLogsEnabled = value);
	}

	/// <summary>
	/// Applies every setting to the whole scene in one pass and watches for added actors while a setting differs from
	/// the game's. <paramref name="onlyOverrides"/> skips the pass when nothing would change.
	/// </summary>
	private void ApplyToScene(bool onlyOverrides = false)
	{
		if (!IsInsideTree())
		{
			return;
		}

		AttackController.GlobalDebugDrawEnabled = DebugMeshesEnabled;
		SceneTree tree = GetTree();
		if (OverridesActors && _stopWatching == null)
		{
			_stopWatching = this.SubscribeUntilExit(tree, t => t.NodeAdded += ApplyTo, t => t.NodeAdded -= ApplyTo);
		}
		else if (!OverridesActors && _stopWatching != null)
		{
			_stopWatching();
			_stopWatching = null;
		}
		if (onlyOverrides && !OverridesActors)
		{
			return;
		}
		foreach (Node node in tree.Root.FindChildren("*", "", true, false))
		{
			ApplyTo(node);
		}
	}

	private void ApplyTo(Node node)
	{
		switch (node)
		{
			case AttackController attackController:
				attackController.DebugDrawEnabled = DebugMeshesEnabled;
				attackController.EffectsEnabled = EffectsEnabled;
				break;
			case HurtBoxComponent hurtBox:
				hurtBox.DebugDrawEnabled = HurtboxesEnabled;
				break;
			case CombatFeedbackController feedback:
				feedback.Enabled = EffectsEnabled;
				break;
			case MovementEffectsComponent movement:
				movement.Enabled = MovementEffectsEnabled;
				break;
			case CombatCameraFeedbackController camera:
				camera.Enabled = CameraShakeEnabled;
				break;
			case Node3D mesh when mesh.IsInGroup(DebugMeshGroup):
				mesh.Visible = DebugMeshesEnabled;
				break;
		}
	}

	private void ApplySlowMotion()
	{
		if (SlowMotionEnabled == _slowed)
		{
			return;
		}
		if (SlowMotionEnabled) { _normalTimeScale = Engine.TimeScale; }
		Engine.TimeScale = SlowMotionEnabled ? _normalTimeScale * SlowMotionScale : _normalTimeScale;
		_slowed = SlowMotionEnabled;
	}
}
