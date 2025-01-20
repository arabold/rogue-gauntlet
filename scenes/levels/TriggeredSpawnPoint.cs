using Godot;
using Godot.Collections;

/// <summary>
/// A spawn point that spawns objects when a player enters the area
/// </summary>
public partial class TriggeredSpawnPoint : Area3D
{
	public Array<PackedScene> Scenes
	{
		get => _scenes;
		set
		{
			_scenes = value;
			if (IsNodeReady())
			{
				SpawnPoint.Scenes = value;
			}
		}
	}
	public SpawnPoint SpawnPoint;

	private Array<PackedScene> _scenes;

	public override void _Ready()
	{
		SpawnPoint = GetNode<SpawnPoint>("SpawnPoint");
		SpawnPoint.Scenes = Scenes;
		SpawnPoint.SpawnOnStart = false;

		AreaEntered += OnAreaEntered;
	}

	private void OnAreaEntered(Node node)
	{
		if (node is Player || node.Owner is Player)
		{
			SpawnPoint.Spawn();
		}
	}
}
