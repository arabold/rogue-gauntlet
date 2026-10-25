using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The quality odds rolled equipment draws from. Each <see cref="QualityLevel"/> is introduced at a depth and grows from
/// there, so later qualities gradually outweigh earlier ones; it also sets the base-stat band and affix count.
/// </summary>
[Tool]
[GlobalClass]
public partial class QualityLadder : Resource
{
	[Export] public QualityLevel[] Levels { get; set; } = [];

	/// <summary>Rolls a quality for a drop at <paramref name="depth"/>; Normal when no level is eligible there.</summary>
	public ItemQuality Roll(uint depth, RandomNumberGenerator rng)
	{
		QualityLevel[] levels = (Levels ?? []).Where(level => level != null).ToArray();
		float[] weights = levels.Select(level => level.WeightAt(depth)).ToArray();
		return weights.Sum() > 0f ? levels[rng.RandWeighted(weights)].Quality : ItemQuality.Normal;
	}

	/// <summary>A uniform base-stat scale inside the quality's band, or 1 when the quality has no level.</summary>
	public float RollScale(ItemQuality quality, RandomNumberGenerator rng)
	{
		QualityLevel level = Level(quality);
		return level == null ? 1f : rng.RandfRange(level.MinScale, level.MaxScale);
	}

	public int RollAffixCount(ItemQuality quality, RandomNumberGenerator rng)
	{
		QualityLevel level = Level(quality);
		if (level == null || level.MaxAffixes <= 0) { return 0; }
		int min = Mathf.Max(0, level.MinAffixes);
		return rng.RandiRange(min, Mathf.Max(min, level.MaxAffixes));
	}

	public QualityLevel Level(ItemQuality quality) => Levels?.FirstOrDefault(level => level?.Quality == quality);

	/// <summary>Authoring problems: every quality needs exactly one level, with ordered scale and affix ranges.</summary>
	public string[] Validate()
	{
		var problems = new List<string>();
		foreach (ItemQuality quality in System.Enum.GetValues<ItemQuality>())
		{
			QualityLevel[] matches = (Levels ?? []).Where(level => level?.Quality == quality).ToArray();
			if (matches.Length != 1) { problems.Add($"{quality} has {matches.Length} levels; expected 1."); continue; }
			QualityLevel level = matches[0];
			if (!(level.MinScale > 0f && level.MinScale <= level.MaxScale)) { problems.Add($"{quality} has an invalid scale band."); }
			if (level.MinAffixes < 0 || level.MinAffixes > level.MaxAffixes) { problems.Add($"{quality} has an invalid affix range."); }
		}
		return problems.ToArray();
	}
}
