using Godot;

/// <summary>
/// Caches the hero for nodes that react to it every frame; a missing hero is looked up again only every
/// <see cref="RetryInterval"/>, so a scene without one costs no group query per frame.
/// </summary>
/// <remarks>A mutable value type like <see cref="Cooldown"/>: keep it in a field and call <see cref="Find"/> on it.</remarks>
public struct PlayerLookup
{
	private static readonly StringName PlayerGroup = "player";
	private const float RetryInterval = .5f;
	private Node3D _player;
	private Cooldown _retry;

	/// <summary>The hero in <paramref name="from"/>'s tree, or null while there is none.</summary>
	public Node3D Find(Node from, double delta)
	{
		if (GodotObject.IsInstanceValid(_player) && _player.IsInsideTree()) return _player;
		if (!_retry.Tick(delta)) return null;
		_retry.Start(RetryInterval);
		return _player = from.GetTree().GetFirstNodeInGroup(PlayerGroup) as Node3D;
	}
}
