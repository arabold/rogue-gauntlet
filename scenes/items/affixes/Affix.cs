using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>Whether an affix reads before the item name ("Vicious …") or after ("… of the Bear").</summary>
public enum AffixKind
{
	Prefix,
	Suffix,
}

/// <summary>
/// One tier of a magical attribute family, e.g. "of the Titan" in the strength family. Authored as its own
/// <c>.tres</c> so several <see cref="EquipmentRollTable"/>s can list it; the list it sits in decides whether it reads
/// as a prefix or a suffix. Rolling picks a value in each modifier's range and records the result as a
/// <see cref="RolledAffix"/> on the item instance.
/// </summary>
[Tool]
[GlobalClass]
public partial class Affix : Resource
{
	/// <summary>Name fragment contributed to the item, e.g. "Vicious" or "of the Bear".</summary>
	[Export] public string NameFragment { get; set; } = "";
	/// <summary>Family this tier belongs to, e.g. "strength". An item holds at most one affix per family.</summary>
	[Export] public string Group { get; set; } = "";
	/// <summary>Shallowest drop depth this tier can roll at.</summary>
	[Export] public uint MinDepth { get; set; } = 1;
	/// <summary>Relative weight when picking among eligible affixes.</summary>
	[Export] public float Weight { get; set; } = 1f;
	[Export] public AffixModifierRange[] Modifiers { get; set; } = [];

	/// <summary>A weighted random pick among <paramref name="affixes"/>; uniform when no weight is positive.</summary>
	public static Affix PickWeighted(IReadOnlyList<Affix> affixes, RandomNumberGenerator rng)
	{
		float[] weights = new float[affixes.Count];
		for (int i = 0; i < weights.Length; i++) { weights[i] = Mathf.Max(0f, affixes[i].Weight); }
		long index = weights.Sum() > 0f ? rng.RandWeighted(weights) : rng.RandiRange(0, affixes.Count - 1);
		return affixes[(int)index];
	}

	/// <summary>The family key; an affix without a group is its own family.</summary>
	public string Family => string.IsNullOrEmpty(Group) ? NameFragment : Group;

	/// <summary>Rolls this tier as a named affix in the given position.</summary>
	public RolledAffix Roll(AffixKind kind, RandomNumberGenerator rng) => new()
	{
		NameFragment = NameFragment,
		Group = Family,
		Kind = kind,
		Modifiers = RollModifiers(rng),
	};

	/// <summary>
	/// Rolls concrete modifier values. Whole-number stats round to whole numbers (+3 Strength); fractions such as
	/// critical chance and percentages round to whole percents.
	/// </summary>
	public StatModifier[] RollModifiers(RandomNumberGenerator rng)
	{
		var modifiers = new List<StatModifier>();
		foreach (AffixModifierRange range in Modifiers)
		{
			if (range == null)
			{
				continue;
			}

			var modifier = new StatModifier { Stat = range.Stat, Op = range.Op };
			modifier.Value = modifier.Round(rng.RandfRange(range.MinValue, range.MaxValue));
			modifiers.Add(modifier);
		}

		return modifiers.ToArray();
	}
}
