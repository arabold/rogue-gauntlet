using System.Collections.Generic;
using Godot;

/// <summary>
/// Trap gauntlets: the run's few gauntlet floors (<see cref="GauntletProfile"/>), each building its descent behind its
/// gauntlet.
/// </summary>
public partial class MapGenerator
{
	/// <summary>Layout attempts that build the floor's gauntlet; later ones go without it rather than fail the floor.</summary>
	private const int GauntletAttempts = 6;

	[ExportGroup("Gauntlets")]
	/// <summary>The run's trap gauntlets; none without. A pinned <see cref="Profile"/> builds every floor without one.</summary>
	[Export] public GauntletProfile Gauntlets { get; set; }

	/// <summary>The gauntlet the run schedules on this floor, decided with its plan; null on most floors.</summary>
	public RoomDefinition CurrentGauntlet { get; private set; }

	/// <summary>Every gauntlet of the run, shallowest first; empty without a profile and when a pinned plan builds every floor.</summary>
	public List<(uint Depth, RoomDefinition Room)> GauntletSchedule() =>
		Gauntlets == null || Profile != null || Rooms == null ? []
			: Gauntlets.Schedule(RunSeed, Rooms.Gauntlets, depth => RealmAt(depth)?.Theme, CanHoldGauntlet);

	/// <summary>
	/// An ordinary floor no unique plan takes over (gatekeepers, shops and the labyrinth stand on their own) whose realm
	/// has a plan there with room for a gauntlet before the descent.
	/// </summary>
	private bool CanHoldGauntlet(uint depth) =>
		IsOrdinaryFloor(depth) && UniqueFloorPlan.At(UniqueFloorPlans, RunSeed, depth, IsOrdinaryFloor) == null
		&& RealmAt(depth) is { } realm && realm.HoldsGauntletOn(realm.FloorOf(depth));

	/// <summary>
	/// What this attempt's descent builds from: the plan's own goal, or the approach through the floor's gauntlet while
	/// attempts for it remain; null for an ordinary exit.
	/// </summary>
	private RoomDefinition GoalFor(int attempt) =>
		CurrentProfile.GoalOverride ?? (CurrentGauntlet != null && attempt < GauntletAttempts ? Gauntlets.Approach : null);
}