using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The words and affixes one kind of equipment can roll, as plain authored arrays: rings, amulets, metal weapons,
/// wooden weapons, shields. Each definition references one table. Poor and Superior items pick a word; Magic and
/// Rare items pick affixes, where the array an affix sits in decides whether it reads as a prefix or a suffix.
/// </summary>
[Tool]
[GlobalClass]
public partial class EquipmentRollTable : Resource
{
	/// <summary>Words for Poor items, e.g. "damaged", "cracked"; one is picked at random.</summary>
	[Export] public string[] PoorAdjectives { get; set; } = [];
	/// <summary>Words for Superior items, e.g. "polished", "shiny"; one is picked at random.</summary>
	[Export] public string[] SuperiorAdjectives { get; set; } = [];
	[Export] public Affix[] Prefixes { get; set; } = [];
	[Export] public Affix[] Suffixes { get; set; } = [];

	/// <summary>A random adjective for Poor or Superior items; "" for every other quality or an empty list.</summary>
	public string RollAdjective(ItemQuality quality, RandomNumberGenerator rng)
	{
		string[] words = quality switch
		{
			ItemQuality.Poor => PoorAdjectives,
			ItemQuality.Superior => SuperiorAdjectives,
			_ => null,
		};
		return words is { Length: > 0 } ? words[rng.RandiRange(0, words.Length - 1)] ?? "" : "";
	}

	/// <summary>
	/// Rolls up to <paramref name="count"/> affixes eligible at <paramref name="depth"/>: at most one per family and at
	/// most half the count, rounded up, of each kind. Stops early when nothing eligible remains.
	/// </summary>
	public RolledAffix[] RollAffixes(uint depth, int count, RandomNumberGenerator rng)
	{
		var rolled = new List<RolledAffix>();
		for (int i = 0; i < count; i++)
		{
			RolledAffix next = RollNext(depth, rolled, KindCap(count), rng);
			if (next == null) { break; }
			rolled.Add(next);
		}
		return rolled.ToArray();
	}

	/// <summary>
	/// One more affix for an item that already holds <paramref name="existing"/>, under the same family and kind rules
	/// for the grown count; null when nothing fits. Used by enchantment.
	/// </summary>
	public RolledAffix RollAdditional(uint depth, IReadOnlyList<RolledAffix> existing, RandomNumberGenerator rng) =>
		RollNext(depth, existing, KindCap(existing.Count + 1), rng);

	/// <summary>Authoring problems: missing entries, duplicate affixes or an affix listed as both prefix and suffix.</summary>
	public string[] Validate()
	{
		var problems = new List<string>();
		Affix[] all = (Prefixes ?? []).Concat(Suffixes ?? []).ToArray();
		if (all.Any(affix => affix == null)) { problems.Add("A prefix or suffix entry is empty."); }
		foreach (var duplicate in all.Where(affix => affix != null).GroupBy(affix => affix).Where(group => group.Count() > 1))
		{
			problems.Add($"'{duplicate.Key.NameFragment}' is listed more than once.");
		}
		if ((PoorAdjectives ?? []).Concat(SuperiorAdjectives ?? []).Any(string.IsNullOrWhiteSpace)) { problems.Add("A word is empty."); }
		return problems.ToArray();
	}

	private static int KindCap(int count) => (count + 1) / 2;

	private RolledAffix RollNext(uint depth, IReadOnlyCollection<RolledAffix> rolled, int kindCap, RandomNumberGenerator rng)
	{
		var families = rolled.Select(affix => affix.Group).ToHashSet();
		Affix[] Eligible(Affix[] affixes, AffixKind kind) => rolled.Count(affix => affix.Kind == kind) >= kindCap ? []
			: (affixes ?? []).Where(affix => affix != null && affix.MinDepth <= depth && !families.Contains(affix.Family)).ToArray();

		Affix[] prefixes = Eligible(Prefixes, AffixKind.Prefix);
		Affix[] suffixes = Eligible(Suffixes, AffixKind.Suffix);
		if (prefixes.Length == 0 && suffixes.Length == 0) { return null; }

		// With both kinds open, a fair coin picks the kind first.
		bool prefix = suffixes.Length == 0 || prefixes.Length > 0 && rng.RandiRange(0, 1) == 0;
		return Affix.PickWeighted(prefix ? prefixes : suffixes, rng).Roll(prefix ? AffixKind.Prefix : AffixKind.Suffix, rng);
	}
}
