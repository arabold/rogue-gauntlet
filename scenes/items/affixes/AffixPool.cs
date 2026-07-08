using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The shared catalog of affixes plus the rules for how loot rolls rarity and how many
/// affixes each rarity gets. Authored once as a <c>.tres</c> and consulted by
/// <see cref="LootRoller"/> when an equipable drops.
/// </summary>
[GlobalClass]
public partial class AffixPool : Resource
{
	[Export] public Affix[] Affixes { get; set; } = [];
	[Export] public AffixRollCount[] RollCounts { get; set; } = [];
	[Export] public RarityWeight[] RarityWeights { get; set; } = [];

	/// <summary>Rolls a rarity for a drop at the given depth, or Common if no weights apply.</summary>
	public EquipableItemRarity RollRarity(uint depth, RandomNumberGenerator rng)
	{
		if (RarityWeights == null || RarityWeights.Length == 0)
		{
			return EquipableItemRarity.Common;
		}

		float[] weights = RarityWeights
			.Select(rw => Mathf.Max(0f, rw.BaseWeight + rw.WeightPerDepth * depth))
			.ToArray();

		if (weights.Sum() <= 0f)
		{
			return EquipableItemRarity.Common;
		}

		return RarityWeights[(int)rng.RandWeighted(weights)].Rarity;
	}

	/// <summary>The number of affixes to roll for the given rarity.</summary>
	public int RollAffixCount(EquipableItemRarity rarity, RandomNumberGenerator rng)
	{
		AffixRollCount config = RollCounts?.FirstOrDefault(rc => rc.Rarity == rarity);
		if (config == null || config.MaxAffixes <= 0)
		{
			return 0;
		}

		int min = Mathf.Max(0, config.MinAffixes);
		int max = Mathf.Max(min, config.MaxAffixes);
		return rng.RandiRange(min, max);
	}

	/// <summary>
	/// Rolls a set of distinct affixes eligible for the given slots, rarity, and base item
	/// tier, each with rolled values. Picks weighted and without replacement so an item never
	/// stacks the same affix twice.
	/// </summary>
	public RolledAffix[] RollAffixes(ValidSlots slots, EquipableItemRarity rarity, int tier, RandomNumberGenerator rng)
	{
		int count = RollAffixCount(rarity, rng);
		if (count <= 0 || Affixes == null || Affixes.Length == 0)
		{
			return [];
		}

		List<Affix> eligible = Affixes.Where(a => a != null && a.CanRollOn(slots, rarity, tier)).ToList();
		var rolled = new List<RolledAffix>();
		for (int i = 0; i < count && eligible.Count > 0; i++)
		{
			int index = PickWeightedIndex(eligible, rng);
			rolled.Add(eligible[index].Roll(rng));
			eligible.RemoveAt(index);
		}

		return rolled.ToArray();
	}

	/// <summary>
	/// Rolls one affix eligible for the given slots/rarity/tier, excluding any name
	/// fragment already present, or null if nothing is eligible. Used by the Enchant
	/// scroll to add a single affix without re-rolling an item's existing ones.
	/// </summary>
	public RolledAffix RollSingleAffix(
		ValidSlots slots,
		EquipableItemRarity rarity,
		int tier,
		IEnumerable<string> excludeNameFragments,
		RandomNumberGenerator rng)
	{
		if (Affixes == null || Affixes.Length == 0)
		{
			return null;
		}

		var exclude = new HashSet<string>(excludeNameFragments ?? Enumerable.Empty<string>());
		List<Affix> eligible = Affixes
			.Where(a => a != null && a.CanRollOn(slots, rarity, tier) && !exclude.Contains(a.NameFragment))
			.ToList();

		if (eligible.Count == 0)
		{
			return null;
		}

		int index = PickWeightedIndex(eligible, rng);
		return eligible[index].Roll(rng);
	}

	private static int PickWeightedIndex(List<Affix> affixes, RandomNumberGenerator rng)
	{
		float total = affixes.Sum(a => Mathf.Max(0f, a.Weight));
		if (total <= 0f)
		{
			return rng.RandiRange(0, affixes.Count - 1);
		}

		float roll = rng.Randf() * total;
		for (int i = 0; i < affixes.Count; i++)
		{
			roll -= Mathf.Max(0f, affixes[i].Weight);
			if (roll <= 0f)
			{
				return i;
			}
		}

		return affixes.Count - 1;
	}
}
