using Godot;

/// <summary>
/// Resolves how an item should read and look right now, accounting for the per-run
/// identification state. An identifiable item looks like its type's run appearance model the
/// whole run, identified or not; only its label changes from the disguised descriptor to the
/// true name. Centralizes that logic so world pickups, inventory and action-bar previews and
/// labels stay consistent, and falls back to the item's own data when no game session
/// applies, which keeps the editor working.
/// </summary>
public static class ItemIdentity
{
	private static IdentificationService Service => GameSession.Instance?.Identification;

	/// <summary>
	/// The model to show for <paramref name="item"/>: its type's run appearance model for the item's
	/// <see cref="IIdentifiable.AppearanceForm"/>, else the definition's own <see cref="Item.Scene"/>
	/// (items without a hidden identity, or no active session in the editor/tests).
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

	/// <summary>
	/// The label to show: true name when identified, else the disguised name, with any rolled
	/// affix fragments folded in for equipables ("Vicious Broadsword of the Bear").
	/// </summary>
	public static string ResolveDisplayName(Item item)
	{
		string baseName = item is IIdentifiable identifiable && Service != null
			? Service.GetDisplayName(identifiable)
			: item?.Name ?? "";

		// Affix name fragments would leak rolled stats through an unidentified disguise.
		if (item is EquipableItem equipable && IsIdentified(item))
		{
			return equipable.ComposeName(baseName);
		}

		return baseName;
	}

	/// <summary>
	/// True for items with no hidden identity, or when identified; false only for an
	/// unidentified <see cref="IIdentifiable"/>. Editor/tests without an active session
	/// read as identified so nothing appears mysteriously disguised.
	/// </summary>
	public static bool IsIdentified(Item item)
	{
		return item is not IIdentifiable identifiable || Service == null || Service.IsIdentified(identifiable);
	}
}