using Godot;

[Tool]
[GlobalClass]
public partial class LootTableItem : Resource
{
	/// <summary>
	/// Weighted chance of this item being selected from the loot table.
	/// </summary>
	[Export] public float Weight { get; set; } = 1f;
	/// <summary>
	/// Item to be dropped.
	/// </summary>
	[Export] public Item Item { get; set; }
	[Export] public int Quantity { get; set; } = 1;
	/// <summary>Lowest dungeon depth this entry can drop at. 0 means no lower bound.</summary>
	[Export] public uint MinDepth { get; set; }
	/// <summary>Highest dungeon depth this entry can drop at. 0 means no upper bound.</summary>
	[Export] public uint MaxDepth { get; set; }
	/// <summary>Extra quantity granted per dungeon depth, added on top of <see cref="Quantity"/>.</summary>
	[Export] public float QuantityPerDepth { get; set; }

	/// <summary>True if this entry's own window allows the given dungeon depth. A <see cref="LootPool"/> with tier
	/// bands uses its equipment's tier instead, through <see cref="LootPool.IsEligibleAt"/>.</summary>
	public bool IsEligibleAt(uint depth) => IsWithin(depth, MinDepth, MaxDepth);

	/// <summary>True if <paramref name="depth"/> lies between <paramref name="min"/> and <paramref name="max"/>,
	/// where 0 leaves that side open.</summary>
	public static bool IsWithin(uint depth, uint min, uint max) => (min == 0 || depth >= min) && (max == 0 || depth <= max);

	/// <summary>
	/// The quantity to drop at the given depth, scaled by <see cref="QuantityPerDepth"/>.
	/// Clamped to a non-negative <see cref="int"/> so a misauthored negative rate or an
	/// extreme depth can't underflow/overflow into a nonsensical drop quantity.
	/// </summary>
	public int QuantityAt(uint depth)
	{
		double scaled = Quantity + (double)QuantityPerDepth * depth;
		return (int)System.Math.Clamp(scaled, 0, int.MaxValue);
	}
}
