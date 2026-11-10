using Godot;

/// <summary>
/// The world debug tools act in: where spawned nodes go, the navigation monsters use, the depth spawned equipment rolls at,
/// and the lighting override. A generated <see cref="Level"/> is one, so the tools never assume a particular scene layout.
/// </summary>
public interface IDebugWorld
{
	/// <summary>Depth spawned equipment is rolled at.</summary>
	uint Depth { get; }

	/// <summary>Monster navigation; null until the world has one.</summary>
	FloorNavigation Navigation { get; }

	bool IsDark { get; }

	/// <summary>Turns the world's lights off or back on without changing exploration.</summary>
	void SetDark(bool isDark);

	/// <summary>Adds a runtime node to the world at <paramref name="globalPosition"/>.</summary>
	void AddWorldNode(Node3D node, Vector3 globalPosition);

	/// <summary>A free standing spot near <paramref name="origin"/>; the origin itself when there is no floor to search.</summary>
	Vector3 FindFreePositionNear(Vector3 origin);

	/// <summary>Where an item thrown from <paramref name="source"/> lands, as a monster drop would.</summary>
	Vector3 FindDropLanding(Vector3 source, Vector3 direction, float distance);
}
