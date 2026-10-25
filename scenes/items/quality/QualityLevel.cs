using Godot;

/// <summary>
/// Authored rules for one <see cref="ItemQuality"/>: the depth it is introduced at, how its weight grows from
/// there, the band its <see cref="EquipableItem.MagnitudeScale"/> is rolled in, and how many affixes it carries.
/// </summary>
[Tool]
[GlobalClass]
public partial class QualityLevel : Resource
{
	[Export] public ItemQuality Quality { get; set; } = ItemQuality.Normal;
	/// <summary>Introduction depth: shallower drops never roll this quality.</summary>
	[Export] public uint MinDepth { get; set; } = 1;
	/// <summary>Drop weight at <see cref="MinDepth"/>.</summary>
	[Export] public float StartWeight { get; set; }
	/// <summary>Weight gained per depth past <see cref="MinDepth"/>; negative values let a quality thin out.</summary>
	[Export] public float WeightPerDepth { get; set; }
	/// <summary>Lower end of the base-stat band, as a multiple of the definition's authored values.</summary>
	[Export] public float MinScale { get; set; } = 1f;
	[Export] public float MaxScale { get; set; } = 1f;
	[Export] public int MinAffixes { get; set; }
	[Export] public int MaxAffixes { get; set; }

	public float WeightAt(uint depth) =>
		depth < MinDepth ? 0f : Mathf.Max(0f, StartWeight + WeightPerDepth * (depth - MinDepth));
}
