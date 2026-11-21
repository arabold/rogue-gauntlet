using Godot;

/// <summary>
/// A keeper the generator stands on one floor of a realm, near where the hero arrives. The realm lists its shops, so a
/// keeper moves with the realm's depth band instead of being tied to an absolute depth.
/// </summary>
[GlobalClass]
public partial class ShopPlacementProfile : Resource
{
	/// <summary>Stable save key, unique among the realm's shops.</summary>
	[Export] public string Id { get; set; } = "";
	/// <summary>
	/// The realm floor the keeper stands on, from 0 for the realm's first depth. The gatekeeper's floor is the realm's
	/// last (<see cref="RealmEncounterFactory.GatekeeperFloorIndex"/>).
	/// </summary>
	[Export(PropertyHint.Range, "0,10,1,or_greater")] public int FloorIndex { get; set; }
	[Export] public PackedScene KeeperScene { get; set; }
	/// <summary>The keeper prefers a spot this far ahead of the arrival, in the direction the hero walks off the stairs.</summary>
	[Export(PropertyHint.Range, "0,30,0.1,or_greater,suffix:m")] public float DistanceFromArrival { get; set; } = 7f;
	[Export(PropertyHint.Range, "0,30,0.1,or_greater,suffix:m")] public float MinimumDistanceFromArrival { get; set; } = 3f;
}