using System;
using System.Collections.Generic;

/// <summary>Applies the shared flat-then-percent rule and keeps each modifier owned by its activation.</summary>
public sealed class StatModifierLedger
{
	public readonly record struct Entry(object Source, StatModifier Modifier, bool HandLocal);
	private readonly List<Entry> _entries = [];
	public IReadOnlyList<Entry> Entries => _entries;

	public int Register(object source, IEnumerable<StatModifier> modifiers, bool handLocal = false)
	{
		if (source == null || modifiers == null) return 0;
		int added = 0;
		foreach (var modifier in modifiers)
		{
			if (modifier == null) continue;
			_entries.Add(new Entry(source, modifier, handLocal && IsAttackStat(modifier.Stat)));
			added++;
		}
		return added;
	}

	public int Remove(object source) => _entries.RemoveAll(entry => ReferenceEquals(entry.Source, source));

	public float Resolve(StatType stat, float baseValue, Weapon weapon = null)
	{
		float flat = 0, percent = 0;
		foreach (var entry in _entries)
		{
			if (entry.Modifier.Stat != stat || (entry.HandLocal && !ReferenceEquals(entry.Source, weapon))) continue;
			if (entry.Modifier.Op == ModifierOp.Percent) percent += entry.Modifier.Value;
			else flat += entry.Modifier.Value;
		}
		return (baseValue + flat) * (1 + percent);
	}

	public void CopyTo(StatModifierLedger destination, Func<Entry, bool> include)
	{
		foreach (Entry entry in _entries)
			if (include(entry)) destination._entries.Add(entry);
	}

	public static bool IsAttackStat(StatType stat) => stat is StatType.MinDamage or StatType.MaxDamage or StatType.CritChance;
}
