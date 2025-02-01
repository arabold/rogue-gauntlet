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
	/// <summary>
	/// Whether to spawn immediately on scene load
	/// </summary>
	[Export] public bool SpawnOnStart { get; set; } = true;

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

		if (SignalBus.Instance != null) // while in editor
		{
			SignalBus.Instance.LevelLoaded += OnLevelLoaded;
		}
	}

	private void OnLevelLoaded(Level level)
	{
		if (SpawnOnStart && !Engine.IsEditorHint())
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

		var scene = Scenes.PickRandom();
		var node = scene.Instantiate<Node3D>();
		node.GlobalTransform = GlobalTransform;
		node.Rotation = Rotation;

		GameManager.Instance.Level.AddChild(node);

		GD.Print($"Spawned {node.Name} at {node.GlobalPosition}");

		return node;
	}
}
