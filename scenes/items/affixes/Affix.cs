using Godot;

/// <summary>Whether an affix reads before the item name ("Vicious …") or after ("… of the Bear").</summary>
public enum AffixKind
{
	Prefix,
	Suffix,
}

/// <summary>
/// A randomized modifier template for equipment, the building block of Diablo-style loot.
/// When loot rolls an affix it picks a value in each modifier's range and records the result
/// as a <see cref="RolledAffix"/> on the item instance. The affix definition itself is only
/// consulted at roll time.
/// </summary>
[GlobalClass]
public partial class Affix : Resource
{
	/// <summary>Name fragment contributed to the item, e.g. "Vicious" or "of the Bear".</summary>
	[Export] public string NameFragment { get; set; } = "";
	[Export] public AffixKind Kind { get; set; } = AffixKind.Prefix;
	[Export] public AffixModifierRange[] Modifiers { get; set; } = [];
	/// <summary>Equipment slots this affix may roll on; 0 means any slot.</summary>
	[Export(PropertyHint.Flags)] public ValidSlots AllowedSlots { get; set; } = 0;
	/// <summary>Relative weight when picking among eligible affixes.</summary>
	[Export] public float Weight { get; set; } = 1f;
	/// <summary>Lowest item rarity at which this affix can appear.</summary>
	[Export] public EquipableItemRarity MinRarity { get; set; } = EquipableItemRarity.Common;

	/// <summary>True if this affix is eligible for an item of the given slots and rarity.</summary>
	public bool CanRollOn(ValidSlots slots, EquipableItemRarity rarity)
	{
		if (rarity < MinRarity)
		{
			return false;
		}

		return AllowedSlots == 0 || (AllowedSlots & slots) != 0;
	}

	/// <summary>Rolls concrete modifier values for this affix using the given RNG.</summary>
	public RolledAffix Roll(RandomNumberGenerator rng)
	{
		var modifiers = new System.Collections.Generic.List<StatModifier>();
		foreach (AffixModifierRange range in Modifiers)
		{
			if (range == null)
			{
				continue;
			}

			float value = rng.RandfRange(range.MinValue, range.MaxValue);
			// Flat stat rolls read better as whole numbers (e.g. +3 Strength, +5 Armor).
			if (range.Op == ModifierOp.Flat)
			{
				value = Mathf.Round(value);
			}

			modifiers.Add(new StatModifier { Stat = range.Stat, Op = range.Op, Value = value });
		}

		return new RolledAffix
		{
			NameFragment = NameFragment,
			Kind = Kind,
			Modifiers = modifiers.ToArray(),
		};
	}
}
