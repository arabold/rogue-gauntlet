using Godot;

/// <summary>
/// A realm floor's descent objective: on a gatekeeper floor the exit stays sealed until the gatekeeper dies. Generation
/// already placed every encounter from the floor's <see cref="EncounterPlan"/>; this only reads it. Cleared IDs own persistence.
/// </summary>
public partial class RealmEncounterController : Node
{
	public bool DescentUnlocked { get; private set; } = true;
	public string GatekeeperId { get; private set; } = "";
	private Label3D _objective;

	public void Initialize(Level level, EncounterPlan plan)
	{
		if (plan.GatekeeperId == null) return;
		GatekeeperId = plan.GatekeeperId;
		DescentUnlocked = level.Run.IsCleared(GatekeeperId);
		var exit = level.Floor.Descent;
		string name = plan.Definition.GatekeeperName;
		if (exit != null)
		{
			exit.RejectedWalkDirection = Vector3.Forward;
			_objective = new Label3D { Text = DescentUnlocked ? $"{name} defeated — descent open" : $"Defeat {name} to descend", Position = Vector3.Up * 4,
				Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 48, PixelSize = .008f,
				NoDepthTest = true, RenderPriority = FloatingLabel.LabelRenderPriority,
				Modulate = new Color(1, .72f, .25f) };
			_objective.AddChild(new ConstantScreenSize3D { Target = _objective });
			exit.AddChild(_objective);
		}
		var bossErrors = MonsterSceneValidator.ValidateScene(plan.Gatekeeper, out _);
		if (bossErrors.Length > 0)
		{
			DescentUnlocked = false;
			if (_objective != null) _objective.Text = "Invalid gatekeeper configuration — descent locked";
			GD.PushError("Invalid gatekeeper scene: " + string.Join("; ", bossErrors));
			return;
		}
		var spawn = level.Floor.GatekeeperSpawn;
		if (spawn == null)
		{
			GD.PushError("Gatekeeper could not be placed; descent remains locked.");
			return;
		}
		this.SubscribeUntilExit(spawn, s => s.Spawned += OnGatekeeperSpawned, s => s.Spawned -= OnGatekeeperSpawned);
	}

	private void OnGatekeeperSpawned(Node3D node)
	{
		if (node is EnemyBase enemy)
		{
			this.SubscribeUntilExit(enemy.HealthComponent, h => h.Died += Unlock, h => h.Died -= Unlock);
		}
	}

	private void Unlock()
	{
		DescentUnlocked = true;
		if (GodotObject.IsInstanceValid(_objective)) _objective.Text = "Descent open";
	}
}