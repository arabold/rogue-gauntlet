using Godot;

/// <summary>
/// Anything a furnishing step can place: a single <see cref="Prop"/>, a data-authored <see cref="PropGroup"/>, or a
/// coded composition such as <see cref="SeatedTable"/>. Composite: groups contain placeables. Shared definitions are
/// stateless; every choice goes into the plan of one placement.
/// </summary>
[Tool, GlobalClass]
public abstract partial class Placeable : Resource
{
	[Export] public string Id { get; set; } = "";
	/// <summary>Relative chance among the options of the list this entry belongs to.</summary>
	[Export(PropertyHint.Range, "0.01,10,0.01")] public float Weight { get; set; } = 1;

	/// <summary>Appends this placeable's pieces to <paramref name="plan"/> at <paramref name="at"/> (anchor space).
	/// <paramref name="optional"/> marks every appended piece optional, as group members are.</summary>
	public abstract void Compose(RoomBlueprint room, RandomNumberGenerator rng, PlacementPlan plan, Transform3D at, bool optional);

	public PlacementPlan Compose(RoomBlueprint room, RandomNumberGenerator rng)
	{
		var plan = new PlacementPlan();
		Compose(room, rng, plan, Transform3D.Identity, false);
		return plan;
	}

	/// <summary>Authoring errors, prefixed with the entry's ID; empty when valid.</summary>
	public abstract string[] Validate();
}