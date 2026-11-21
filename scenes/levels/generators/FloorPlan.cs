using System.Collections.Generic;
using Godot;

/// <summary>Inputs provided to a <see cref="RoomLayoutStrategy"/> for a single floor generation attempt.</summary>
public sealed record FloorBuildContext(
	RoomPool Pool,
	FloorProfile Profile,
	EncounterPlan Encounter,
	uint MaxRooms,
	RandomNumberGenerator Rng);

/// <summary>
/// Placed clusters, optional strict route constraints (<see cref="ApprovedEdges"/>), and protected internal
/// cluster bridge tiles produced by <see cref="RoomLayoutStrategy.GenerateLayout"/>.
/// </summary>
public sealed class FloorPlan
{
	public List<(RoomCluster Cluster, Vector2I Position)> Placements { get; } = [];
	/// <summary>Directed room-index pairs for strict topological layouts (empty for unconstrained layouts).</summary>
	public List<(int FromClusterId, int ToClusterId)> ApprovedEdges { get; } = [];
	/// <summary>Internal bridge corridor tiles owned by placed clusters that later corridor passes must not overwrite or breach.</summary>
	public HashSet<Vector2I> ProtectedCorridors { get; } = [];

	/// <summary>Frees any still-owned clusters if generation aborts before <see cref="MapGenerator"/> detaches them.</summary>
	public void FreeUnplaced()
	{
		foreach (var (cluster, _) in Placements)
		{
			cluster?.Free();
		}
	}
}
