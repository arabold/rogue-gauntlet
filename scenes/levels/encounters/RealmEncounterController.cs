using Godot;
using System.Linq;

/// <summary>Level-local nest substitutions and gatekeeper objective; cleared IDs own persistence.</summary>
public partial class RealmEncounterController : Node
{
	public bool DescentUnlocked { get; private set; } = true;
	public string GatekeeperId { get; private set; } = "";
	private Label3D _objective;

	public void Initialize(Level level, RealmEncounterFactory definition)
	{
		int floor = definition.FloorIndex(level.MapGenerator.DungeonDepth);
		var points = level.MapGenerator.EnemySpawnPoints;
		if (floor == 1 && definition.NestScene != null)
		{
			foreach (var point in points.Take(2))
			{
				point.Scenes = [definition.NestScene];
				point.PersistentId += ":nest";
			}
		}
		if (floor != 2) return;
		GatekeeperId = $"depth:{level.MapGenerator.DungeonDepth}:gatekeeper";
		DescentUnlocked = GameSession.Instance?.IsEntityCleared(GatekeeperId) == true;
		var exit = level.FindChildren("*", "", true, false).OfType<LevelTransitionTrigger>()
			.FirstOrDefault(t => t.Direction == GameSession.LevelTravelDirection.Down);
		if (exit != null)
		{
			exit.RejectedWalkDirection = Vector3.Forward;
			_objective = new Label3D { Text = DescentUnlocked ? $"{definition.GatekeeperName} defeated — descent open" : $"Defeat {definition.GatekeeperName} to descend", Position = Vector3.Up * 4,
				Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 48, PixelSize = .008f,
				NoDepthTest = true, RenderPriority = FloatingLabel.LabelRenderPriority,
				Modulate = new Color(1, .72f, .25f) };
			_objective.AddChild(new ConstantScreenSize3D { Target = _objective });
			exit.AddChild(_objective);
		}
		var bossErrors = MonsterSceneValidator.ValidateScene(definition.GatekeeperScene, out _);
		if (bossErrors.Length > 0)
		{
			DescentUnlocked = false;
			if (_objective != null) _objective.Text = "Invalid gatekeeper configuration — descent locked";
			GD.PushError("Invalid gatekeeper scene: " + string.Join("; ", bossErrors));
			return;
		}
		var marker = GatekeeperSpawnMarker.Find(level);
		var pointForBoss = points.OrderBy(p => (marker?.GlobalPosition ?? exit?.GlobalPosition ?? Vector3.Zero)
			.DistanceSquaredTo(p.GlobalPosition)).FirstOrDefault();
		if (pointForBoss == null && marker != null)
		{
			pointForBoss = new SpawnPoint();
			level.MapGenerator.AddChild(pointForBoss);
			points.Add(pointForBoss);
		}
		if (pointForBoss == null || definition.GatekeeperScene == null)
		{
			GD.PushError("Gatekeeper could not be placed; descent remains locked.");
			return;
		}
		if (marker != null)
		{
			pointForBoss.GlobalTransform = marker.GlobalTransform;
			// SpawnPoint's existing model-facing correction is cancelled for authored anchors.
			pointForBoss.RotateObjectLocal(Vector3.Up, Mathf.Pi);
		}
		else if (definition.GatekeeperFit != null)
		{
			if (!level.MapGenerator.TryFindLargeEncounterPosition(exit?.GlobalPosition ?? pointForBoss.GlobalPosition,
				definition.GatekeeperFit.MinimumClearance, definition.GatekeeperFit.PreferredClearance, out var position))
			{
				GD.PushError("Gatekeeper has no room with sufficient clearance; descent remains locked.");
				return;
			}
			pointForBoss = points.OrderBy(p => p.GlobalPosition.DistanceSquaredTo(position)).First();
			pointForBoss.GlobalPosition = position;
		}
		pointForBoss.PersistentId = GatekeeperId;
		pointForBoss.Scenes = [definition.GatekeeperScene];
		pointForBoss.Spawned += node =>
		{
			if (node is EnemyBase enemy) enemy.HealthComponent.Died += Unlock;
		};
	}

	private void Unlock()
	{
		DescentUnlocked = true;
		if (GodotObject.IsInstanceValid(_objective)) _objective.Text = "Descent open";
	}
}