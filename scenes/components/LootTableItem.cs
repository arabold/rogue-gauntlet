using Godot;

public partial class LootTableItem : Node
{
	/// <summary>
	/// Weighted chance of this item being selected from the loot table.
	/// </summary>
	[Export] public float Weight = 1f;
	/// <summary>
	/// Item to be dropped.
	/// </summary>
	[Export] public Item Item;
	[Export] public int Quantity = 1;
}
