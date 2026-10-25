using Godot;

/// <summary>
/// Maps an item to the color of its loot beam and glow: equipment by <see cref="ItemQualityPalette"/>, gold and keys by
/// their type, everything else neutral.
/// </summary>
public static class LootVisuals
{
	public static readonly Color GoldColor = new("ffcf40");

	public static Color ResolveColor(Item item) => item switch
	{
		Gold => GoldColor,
		KeyItem key => key.MarkerColor,
		_ => ItemQualityPalette.Color(item),
	};
}
