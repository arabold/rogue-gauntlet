using System.Collections.Generic;
using Godot;

/// <summary>Inputs provided to a <see cref="RoomLayoutStrategy"/> for a single floor generation attempt.</summary>
public sealed record FloorBuildContext(
	RoomPool Pool,
	FloorProfile Profile,
	EncounterPlan Encounter,
	uint MaxRooms,
	RandomNumberGenerator Rng,
	HeadlineSlot Headline = null);

/// <summary>
/// The floor's headline room (<see cref="RealmFloor.Headline"/>) for one layout attempt: layouts draw it before their
/// other rooms, a few times at most, until it is placed. A headline that never fits leaves the floor without one rather
/// than failing the floor.
/// </summary>
public sealed class HeadlineSlot(RoomDefinition definition)
{
	/// <summary>How often an attempt tries the headline before its other rooms take over.</summary>
	public const int Tries = 4;
	private int _tries;

	public RoomDefinition Definition { get; } = definition;

	/// <summary>
	/// The headline while it is unplaced (not yet recorded in <paramref name="uses"/>) and has tries left; each call
	/// spends a try.
	/// </summary>
	public RoomDefinition Take(IReadOnlyDictionary<RoomDefinition, int> uses)
	{
		if (Definition == null || uses.GetValueOrDefault(Definition) > 0 || _tries >= Tries) return null;
		_tries++;
		return Definition;
	}
}

/// <summary>
/// One corridor a strict layout requires, between the doorway exit tiles the layout paired: the empty tiles just
/// outside one doorway of each cluster. Both exits may be the same tile when two doorways face across a one-tile gap.
/// </summary>
public readonly record struct ApprovedEdge(int FromClusterId, int ToClusterId, Vector2I FromExit, Vector2I ToExit);

/// <summary>
/// Placed clusters, optional strict route constraints (<see cref="ApprovedEdges"/>), and protected internal
/// cluster bridge tiles produced by <see cref="RoomLayoutStrategy.GenerateLayout"/>.
/// </summary>
public sealed class FloorPlan
{
	public List<(RoomCluster Cluster, Vector2I Position)> Placements { get; } = [];
	/// <summary>
	/// The only corridors a strict topological layout allows (empty for unconstrained layouts). Routing honors each edge's
	/// paired exits, so no corridor can end on a doorway planned for another edge and skip the room between them.
	/// </summary>
	public List<ApprovedEdge> ApprovedEdges { get; } = [];
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
