using Godot;

/// <summary>How many affixes an item of a given rarity rolls.</summary>
[GlobalClass]
public partial class AffixRollCount : Resource
{
	[Export] public EquipableItemRarity Rarity { get; set; }
	[Export] public int MinAffixes { get; set; }
	[Export] public int MaxAffixes { get; set; }
}
