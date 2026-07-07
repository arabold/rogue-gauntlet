using Godot;

[GlobalClass]
public partial class LootTableItem : Resource
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
	/// <summary>Lowest dungeon depth this entry can drop at. 0 means no lower bound.</summary>
	[Export] public uint MinDepth = 0;
	/// <summary>Highest dungeon depth this entry can drop at. 0 means no upper bound.</summary>
	[Export] public uint MaxDepth = 0;
	/// <summary>Extra quantity granted per dungeon depth, added on top of <see cref="Quantity"/>.</summary>
	[Export] public float QuantityPerDepth = 0f;

	/// <summary>True if this entry is allowed to drop at the given dungeon depth.</summary>
	public bool IsEligibleAt(uint depth)
	{
		return (MinDepth == 0 || depth >= MinDepth) && (MaxDepth == 0 || depth <= MaxDepth);
	}

	/// <summary>The quantity to drop at the given depth, scaled by <see cref="QuantityPerDepth"/>.</summary>
	public int QuantityAt(uint depth)
	{
		return Quantity + (int)(QuantityPerDepth * depth);
	}
}
