using Godot;

public enum EnemyType
{
	SkeletonMinion,
}

public struct EnemySpawnPoint
{
	public EnemyType Type; // The type of entity to spawn (e.g., "Goblin", "Skeleton")
	public Vector3 Position; // World position of the spawn point

	public EnemySpawnPoint(EnemyType type, Vector3 position)
	{
		Type = type;
		Position = position;
	}
}
