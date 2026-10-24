using Godot;
using Godot.Collections;

/// <summary>
/// A disguise for an unidentified <see cref="IIdentifiable"/> item. Each run the
/// <see cref="IdentificationService"/> assigns one appearance to one item type from
/// the matching <see cref="AppearancePool"/>. The type keeps that appearance for the whole run:
/// its descriptor names it until identified, and its model is what the item looks like,
/// identified or not. The model, not the definition's own scene, is the disguise, so an
/// appearance never reveals which type it belongs to.
/// </summary>
[GlobalClass]
public partial class ItemAppearance : Resource
{
	/// <summary>Flavor descriptor shown while unidentified, e.g. "crimson" or "red wooden".</summary>
	[Export] public string Descriptor { get; set; } = "";

	/// <summary>
	/// Model per appearance form (<see cref="IIdentifiable.AppearanceForm"/>): "" for categories with
	/// one model per appearance (scrolls, jewelry), "small"/"medium"/"large" for potion sizes.
	/// </summary>
	[Export] public Dictionary<string, PackedScene> Models { get; set; } = [];

	/// <summary>The model for <paramref name="form"/>, or null when this appearance has none for it.</summary>
	public PackedScene GetModel(string form)
	{
		return Models.TryGetValue(form ?? "", out PackedScene model) ? model : null;
	}
}