using Godot;

/// <summary>
/// A disguise applied to an unidentified <see cref="IdentifiableItem"/>. Each run the
/// <see cref="IdentificationService"/> assigns one appearance to one item type from
/// the matching <see cref="AppearancePool"/>. Until the type is identified the player
/// sees the descriptor (in labels) and the tint (recoloring the item's own mesh).
/// </summary>
[GlobalClass]
public partial class ItemAppearance : Resource
{
	/// <summary>Flavor descriptor shown while unidentified, e.g. "fizzy crimson".</summary>
	[Export] public string Descriptor { get; set; } = "";

	/// <summary>
	/// Tint applied to the item's mesh while unidentified, which reads as a
	/// differently colored bottle without needing new art.
	/// </summary>
	[Export] public Color TintColor { get; set; } = Colors.White;
}
