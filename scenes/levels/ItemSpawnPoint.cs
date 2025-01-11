using Godot;

public enum ItemType
{
    Chest,
}

public struct ItemSpawnPoint
{
    /// <summary>
    /// The type of item to spawn (e.g., "HealthPotion", "Chest")
    /// </summary>
    public ItemType Type;
    /// <summary>
    /// The world position of the spawn point
    /// </summary>
    public Vector3 Position;
    /// <summary>
    /// The rotation of the entity when spawned (in degrees around the Y axis)
    /// </summary>
    public float Rotation;

    public ItemSpawnPoint(ItemType type, Vector3 position, float rotation)
    {
        Type = type;
        Position = position;
        Rotation = rotation;
    }
}
