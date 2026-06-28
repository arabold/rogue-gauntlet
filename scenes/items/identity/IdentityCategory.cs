using Godot;
using Godot.Collections;

/// <summary>
/// One identity category in the <see cref="IdentityCatalog"/>: an appearance pool
/// plus every item type that takes part in identification for that category. The
/// authored <see cref="Types"/> order is the stable basis for the per-run
/// type-to-appearance assignment, so keep it consistent across versions.
/// </summary>
[GlobalClass]
public partial class IdentityCategory : Resource
{
	/// <summary>Category key, e.g. "potion". Should match the pool's category.</summary>
	[Export] public string Category { get; set; } = "potion";

	[Export] public AppearancePool AppearancePool { get; set; }

	/// <summary>All item types in this category whose identity is hidden until use.</summary>
	[Export] public Array<IdentifiableItem> Types { get; set; } = new();
}
