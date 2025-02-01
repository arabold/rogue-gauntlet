using Godot;
using Godot.Collections;

/// <summary>
/// A spawn point that spawns objects when a player enters the area
/// </summary>
public partial class TriggeredSpawnPoint : Area3D
{
	public Array<PackedScene> Scenes
	{
		get;
		set
		{
			field = value;
			if (IsNodeReady())
			{
				SpawnPoint.Scenes = value;
			}
		}
	}

	public SpawnPoint SpawnPoint;

	public override void _Ready()
	{
		SpawnPoint = GetNode<SpawnPoint>("SpawnPoint");
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
