using Godot;
using System;

/// <summary>
/// Combat diagnostics and presentation: attack hit volumes and helper meshes, hurtboxes, hit effects, camera shake, slow
/// motion, and the combat and AI logs. Each toggle applies at once, to actors already in the scene and to any added later.
/// </summary>
[GlobalClass]
public partial class CombatDisplayTool : DebugTool
{
	private const int DebugMeshesId = 0;
	private const int CombatLogsId = 1;
	private const int AiLogsId = 2;
	private const int HurtboxesId = 3;
	private const int EffectsId = 4;
	private const int CameraShakeId = 5;
	private const int SlowMotionId = 6;
	private const double SlowMotionScale = .25;

	/// <summary>Shows attack hit volumes and every node in the <c>debug_mesh</c> group.</summary>
	public bool DebugMeshesEnabled
	{
		get;
		set
		{
			field = value;
			AttackController.GlobalDebugDrawEnabled = value;
			ForEachNode<Node>(ApplyDebugMeshes);
		}
	}

	/// <summary>Outlines every hurtbox's shapes.</summary>
	public bool HurtboxesEnabled
	{
		get;
		set
		{
			field = value;
			ForEachNode<HurtBoxComponent>(ApplyHurtbox);
		}
	}

	/// <summary>Hit reactions, impact effects and the lunge on contact; off shows bare contacts.</summary>
	public bool EffectsEnabled
	{
		get;
		set
		{
			field = value;
			ForEachNode<Node>(ApplyEffects);
		}
	} = true;

	/// <summary>Camera shake on the player's resolved contacts.</summary>
	public bool CameraShakeEnabled
	{
		get;
		set
		{
			field = value;
			ForEachNode<CombatCameraFeedbackController>(ApplyCameraShake);
		}
	} = true;

	/// <summary>Runs the game at a quarter of the speed it had when slow motion was turned on.</summary>
	public bool SlowMotionEnabled
	{
		get;
		set
		{
			if (field == value) { return; }
			field = value;
			if (value) { _normalTimeScale = Engine.TimeScale; }
			Engine.TimeScale = value ? _normalTimeScale * SlowMotionScale : _normalTimeScale;
		}
	}

	public bool CombatLogsEnabled
	{
		get => GameDebug.CombatLogsEnabled;
		set => GameDebug.CombatLogsEnabled = value;
	}

	public bool AiLogsEnabled
	{
		get => GameDebug.AiLogsEnabled;
		set => GameDebug.AiLogsEnabled = value;
	}

	private double _normalTimeScale = 1;

	/// <summary>Whether a node added to the scene needs this tool's settings: only while one differs from the game's.</summary>
	private bool OverridesActors => DebugMeshesEnabled || HurtboxesEnabled || !EffectsEnabled || !CameraShakeEnabled;

	public override void _Ready()
	{
		this.SubscribeUntilExit(GetTree(), tree => tree.NodeAdded += OnNodeAdded, tree => tree.NodeAdded -= OnNodeAdded);
	}

	public override void _ExitTree()
	{
		// Engine-wide switches would otherwise outlive the scene that set them.
		SlowMotionEnabled = false;
		if (DebugMeshesEnabled) { AttackController.GlobalDebugDrawEnabled = false; }
	}

	public override void BuildMenu(PopupMenu menu)
	{
		menu.AddCheckItem("Debug meshes", DebugMeshesId);
		menu.AddCheckItem("Hurtboxes", HurtboxesId);
		menu.AddCheckItem("Hit effects", EffectsId);
		menu.AddCheckItem("Camera shake", CameraShakeId);
		menu.AddCheckItem("Slow motion", SlowMotionId);
		menu.AddSeparator();
		menu.AddCheckItem("Combat logs", CombatLogsId);
		menu.AddCheckItem("AI logs", AiLogsId);
		menu.IdPressed += OnPressed;
		// Hit volumes start hidden; the global switch would otherwise carry over from an earlier scene.
		DebugMeshesEnabled = false;
		RefreshMenu(menu);
	}

	public override void RefreshMenu(PopupMenu menu)
	{
		SetChecked(menu, DebugMeshesId, DebugMeshesEnabled);
		SetChecked(menu, HurtboxesId, HurtboxesEnabled);
		SetChecked(menu, EffectsId, EffectsEnabled);
		SetChecked(menu, CameraShakeId, CameraShakeEnabled);
		SetChecked(menu, SlowMotionId, SlowMotionEnabled);
		SetChecked(menu, CombatLogsId, CombatLogsEnabled);
		SetChecked(menu, AiLogsId, AiLogsEnabled);
	}

	private void OnPressed(long id)
	{
		switch (id)
		{
			case DebugMeshesId: DebugMeshesEnabled = !DebugMeshesEnabled; break;
			case HurtboxesId: HurtboxesEnabled = !HurtboxesEnabled; break;
			case EffectsId: EffectsEnabled = !EffectsEnabled; break;
			case CameraShakeId: CameraShakeEnabled = !CameraShakeEnabled; break;
			case SlowMotionId: SlowMotionEnabled = !SlowMotionEnabled; break;
			case CombatLogsId: CombatLogsEnabled = !CombatLogsEnabled; break;
			case AiLogsId: AiLogsEnabled = !AiLogsEnabled; break;
		}
		RefreshMenu(Popup);
	}

	private void OnNodeAdded(Node node)
	{
		if (!OverridesActors)
		{
			return;
		}
		ApplyDebugMeshes(node);
		ApplyEffects(node);
		if (node is HurtBoxComponent hurtBox) { ApplyHurtbox(hurtBox); }
		if (node is CombatCameraFeedbackController camera) { ApplyCameraShake(camera); }
	}

	private void ApplyDebugMeshes(Node node)
	{
		if (node is AttackController attackController) { attackController.DebugDrawEnabled = DebugMeshesEnabled; }
		else if (node is Node3D node3D && node.IsInGroup("debug_mesh")) { node3D.Visible = DebugMeshesEnabled; }
	}

	private void ApplyHurtbox(HurtBoxComponent hurtBox) => hurtBox.DebugDrawEnabled = HurtboxesEnabled;

	private void ApplyEffects(Node node)
	{
		if (node is AttackController attackController) { attackController.EffectsEnabled = EffectsEnabled; }
		else if (node is CombatFeedbackController feedback) { feedback.Enabled = EffectsEnabled; }
	}

	private void ApplyCameraShake(CombatCameraFeedbackController camera) => camera.Enabled = CameraShakeEnabled;

	/// <summary>Applies a setting to every node of type <typeparamref name="T"/> in the scene; nothing outside the tree.</summary>
	private void ForEachNode<T>(Action<T> apply) where T : Node
	{
		if (!IsInsideTree())
		{
			return;
		}
		foreach (Node node in GetTree().Root.FindChildren("*", "", true, false))
		{
			if (node is T match) { apply(match); }
		}
	}
}
