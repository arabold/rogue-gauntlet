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
	/// <summary>
	/// How far a drop's quantity strays from <see cref="QuantityAt"/>, as a share of it: 0.4 rolls 60–140% of it, so gold
	/// piles differ in size the way Rogue's and Pixel Dungeon's do. 0 drops exactly that quantity.
	/// </summary>
	[Export(PropertyHint.Range, "0,1,0.05")] public float QuantitySpread { get; set; }

	/// <summary>True if this entry's own window allows the given dungeon depth. A <see cref="LootPool"/> with tier
	/// bands uses its equipment's tier instead, through <see cref="LootPool.IsEligibleAt"/>.</summary>
	public bool IsEligibleAt(uint depth) => IsWithin(depth, MinDepth, MaxDepth);

	/// <summary>Whether this entry can enter a weighted draw at this depth, including a pool's tier bands.</summary>
	internal static bool CanRollAt(LootTableItem entry, uint depth, LootPool pool = null) =>
		entry?.Item != null && float.IsFinite(entry.Weight) && entry.Weight > 0
		&& (pool?.IsEligibleAt(entry, depth) ?? entry.IsEligibleAt(depth));

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

	/// <summary>
	/// The quantity one drop yields at <paramref name="depth"/>: <see cref="QuantityAt"/>, varied by
	/// <see cref="QuantitySpread"/>. Draws from <paramref name="rng"/> only when the entry has a spread.
	/// </summary>
	public int RollQuantity(uint depth, RandomNumberGenerator rng)
	{
		int quantity = QuantityAt(depth);
		if (QuantitySpread <= 0 || quantity <= 0) return quantity;
		float spread = Mathf.Min(QuantitySpread, 1f);
		return Mathf.Max(1, Mathf.RoundToInt(quantity * rng.RandfRange(1f - spread, 1f + spread)));
	}
}
