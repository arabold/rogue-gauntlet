using Godot;

/// <summary>
/// Drop weight for one rarity, scaling with dungeon depth so deeper floors bias toward better
/// gear. The effective weight is <c>max(0, BaseWeight + WeightPerDepth * depth)</c>.
/// </summary>
[GlobalClass]
public partial class RarityWeight : Resource
{
	[Export] public EquipableItemRarity Rarity { get; set; }
	[Export] public float BaseWeight { get; set; }
	[Export] public float WeightPerDepth { get; set; }
}
