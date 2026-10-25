using Godot;

/// <summary>
/// One color per <see cref="ItemQuality"/>, shared by loot beams, slot tints, names and tooltips. The quality is
/// never secret, so unidentified items show their color too. Orange is reserved for future Unique items.
/// </summary>
public static class ItemQualityPalette
{
	/// <summary>Color of Normal and Superior items and of everything without a quality: consumables, gold, keys.</summary>
	public static readonly Color Neutral = new("dfe4ea");
	public static readonly Color Poor = new("8f8f8f");
	public static readonly Color Magic = new("4caf50");
	public static readonly Color Rare = new("3b82f6");

	public static Color Color(ItemQuality quality) => quality switch
	{
		ItemQuality.Poor => Poor,
		ItemQuality.Magic => Magic,
		ItemQuality.Rare => Rare,
		_ => Neutral,
	};

	public static Color Color(Item item) => item is EquipableItem equipment ? Color(equipment.Quality) : Neutral;
}
