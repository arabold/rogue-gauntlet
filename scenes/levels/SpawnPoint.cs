using System;
using Godot;
using Godot.Collections;

/// <summary>
/// A spawn point that spawns objects on scene load or when called
/// </summary>
[Tool]
public partial class SpawnPoint : Node3D
{
	/// <summary>
	/// The type of object to spawn (e.g. an enemy scene)
	/// </summary>
	[Export] public Array<PackedScene> Scenes;
	/// <summary>The level this point spawns into: runtime context, resolved from the tree on entry.</summary>
	public Level Level { get; private set; }
	[Export] public string PersistentId { get; set; } = string.Empty;
	/// <summary>
	/// Whether to spawn immediately on scene load
	/// </summary>
	[Export] public WorldEffectDefinition ArrivalEffect { get; set; }
	[Export] public bool SpawnOnStart { get; set; } = true;

	/// <summary>Local notification for encounter-specific wiring after actor initialization.</summary>
	public event Action<Node3D> Spawned;

	public override void _EnterTree() => Level = this.GetAncestorOrNull<Level>();

	public override void _Ready()
	{
		if (Engine.IsEditorHint())
		{
			// Preview the spawn point in the editor
			if (Scenes?.Count > 0)
			{
				AddChild(Scenes[0].Instantiate<Node3D>());
			}
		}

		this.SubscribeUntilExit(
			SignalBus.Instance,
			signalBus => signalBus.LevelLoaded += OnLevelLoaded,
			signalBus => signalBus.LevelLoaded -= OnLevelLoaded);
	}

	private void OnLevelLoaded(Level level)
	{
		if (level == Level && SpawnOnStart && !Engine.IsEditorHint())
		{
			Spawn();
		}
	}

	/// <summary>
	/// Spawns an objecct at the spawn point
	/// </summary>
	public Node Spawn()
	{
		if (Scenes == null || Scenes.Count == 0)
		{
			GD.PrintErr("No scenes to spawn");
			return null;
		}

		if (!string.IsNullOrEmpty(PersistentId) && Level.Run.IsCleared(PersistentId))
		{
			GD.Print($"Skipping spawn of cleared entity: {PersistentId}");
			return null;
		}

		// Generated encounters already carry a concrete scene; authored multi-scene
		// spawns derive a local stream instead of depending on callback/global draw order.
		using var rng = GenerationRandom.Create(Level.Seed,
			GenerationRandom.Monsters, string.IsNullOrEmpty(PersistentId)
				? Level.GetPathTo(this).ToString() : PersistentId);
		var scene = Scenes[rng.RandiRange(0, Scenes.Count - 1)];
		var node = scene.Instantiate<Node3D>();
		node.GlobalTransform = GlobalTransform;
		node.Rotate(Vector3.Up, Mathf.DegToRad(180)); // FIXME: Why?

		if (!string.IsNullOrEmpty(PersistentId))
		{
			node.SetMeta("persistent_id", PersistentId);
		}

		Level.AddWorldNode(node);
		if (ArrivalEffect != null && node is EnemyBase actor) actor.WorldEffects?.PlayBurst(ArrivalEffect, 271);
		Spawned?.Invoke(node);

		GD.Print($"Spawned {node.Name} at {node.GlobalPosition}");

		return node;
	}
}