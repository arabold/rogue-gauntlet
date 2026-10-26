using Godot;

/// <summary>
/// Resolves how an item should read and look right now, accounting for the per-run
/// identification state. An identifiable item looks like its type's run appearance model the
/// whole run, identified or not; only its label changes from the disguised descriptor to the
/// true name. Centralizes that logic so world pickups, inventory and action-bar previews and
/// labels stay consistent, and falls back to the item's own data when no game session
/// applies, which keeps the editor working. Identifiable types have no model of their own.
/// </summary>
public static class ItemIdentity
{
	// UI-wide display has no level of its own, so it reads the session's active run: the one documented exception.
	private static IdentificationService Service => GameSession.Instance?.Run.Identification;

	/// <summary>
	/// The model to show for <paramref name="item"/>: its type's run appearance model for the item's
	/// <see cref="IIdentifiable.AppearanceForm"/>, else the definition's own <see cref="Item.Scene"/>.
	/// Identifiable types leave <see cref="Item.Scene"/> empty, so they have no model outside a session.
	/// </summary>
	public static PackedScene ResolveScene(Item item)
	{
		if (item is IIdentifiable identifiable && identifiable.HasIdentity)
		{
			PackedScene model = Service?.GetAppearance(identifiable)?.GetModel(identifiable.AppearanceForm);
			if (model != null)
			{
				return model;
			}
		}

		return item?.Scene;
	}

	/// <summary>The label to show this run; see <see cref="ComposeDisplayName"/>.</summary>
	public static string ResolveDisplayName(Item item) => ComposeDisplayName(item, Service);

	/// <summary>
	/// The label for <paramref name="item"/> under <paramref name="service"/>'s knowledge, composed once and then
	/// capitalized: a revealed curse leads with "cursed"; a hidden type reads as the quality word and disguise ("Shiny
	/// golden ruby ring", "Magic golden ruby ring"); hidden attributes read as quality word and name ("Magic Broadsword");
	/// known attributes fold in the adjective or affixes and, for jewelry, the signed value ("Vicious Broadsword of the Ox",
	/// "Shiny Ring of Strength +4"). The quality is never secret, but affixes and values would leak hidden knowledge.
	/// </summary>
	public static string ComposeDisplayName(Item item, IdentificationService service)
	{
		if (item == null)
		{
			return "";
		}

		string baseName = item is IIdentifiable identifiable && service != null ? service.GetDisplayName(identifiable) : item.Name;
		if (item is not EquipableItem equipable)
		{
			return baseName.UpperFirst();
		}

		string name = !IsTypeKnown(item, service) ? TextExtensions.JoinWords(equipable.QualityWord, baseName)
			: equipable.AttributesKnown ? equipable.ComposeName(baseName) : equipable.ComposeUnidentifiedName(baseName);
		string curse = equipable.IsCursed && equipable.CurseStatusKnown ? "cursed" : null;
		return TextExtensions.JoinWords(curse, name).UpperFirst();
	}

	/// <summary>
	/// True for items with no hidden type, or once their type is identified this run. Editor/tests without an active
	/// session read as known so nothing appears mysteriously disguised.
	/// </summary>
	public static bool IsTypeKnown(Item item) => IsTypeKnown(item, Service);

	private static bool IsTypeKnown(Item item, IdentificationService service) =>
		item is not IIdentifiable identifiable || service == null || service.IsIdentified(identifiable);

	/// <summary>
	/// Whether the tooltip may show the item's stats: its type is known and, for equipment, its own attributes
	/// (unidentified Magic and Rare items hide them).
	/// </summary>
	public static bool AreStatsKnown(Item item) => IsTypeKnown(item) && item is not EquipableItem { AttributesKnown: false };

	/// <summary>Stats and, for equipment, curse status are known. The scroll of identify targets anything less.</summary>
	public static bool IsFullyIdentified(Item item) => AreStatsKnown(item) && item is not EquipableItem { CurseStatusKnown: false };
}