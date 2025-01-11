using Godot;

public enum EnemyType
{
	SkeletonMinion,
}

public struct EnemySpawnPoint
{
	/// <summary>
	/// The type of enemy to spawn (e.g., "Goblin", "Skeleton")
	/// </summary>
	public EnemyType Type;
	/// <summary>
	/// The world position of the spawn point
	/// </summary>
	public Vector3 Position;
	/// <summary>
	/// The rotation of the entity when spawned (in degrees around the Y axis)
	/// </summary>
	public float Rotation;

	public EnemySpawnPoint(EnemyType type, Vector3 position, float rotation)
	{
		Type = type;
		Position = position;
		Rotation = rotation;
	}
}
