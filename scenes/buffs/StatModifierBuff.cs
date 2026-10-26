using System.Collections.Generic;
using Godot;

/// <summary>
/// Data-driven buff that applies one or more <see cref="StatModifier"/>s for its duration
/// and reverses them on removal. Covers the common "+X stat" / "+Y% stat" effects without a
/// bespoke <see cref="Buff"/> subclass each. Use <see cref="Buff.Duration"/> 0 for a permanent
/// modifier (e.g. a strength potion or equipment bonus); periodic effects still use a
/// <see cref="PeriodicBuff"/> subclass. Modifiers register under the activation, not this shared
/// definition, so two overlapping drinks from one stack each reverse only their own bonus.
/// </summary>
[Tool]
[GlobalClass]
public partial class StatModifierBuff : Buff
{
	[Export] public StatModifier[] Modifiers { get; set; } = [];

	public override void OnApply(ActiveBuff activation)
	{
		activation.Player.Stats.AddModifiers(activation, Modifiers);
	}

	public override void OnRemove(ActiveBuff activation)
	{
		activation.Player.Stats.RemoveModifiersFrom(activation);
	}

	/// <summary>Authoring problems: a stat buff needs at least one modifier, and every modifier must change its stat.</summary>
	public string[] Validate()
	{
		var problems = new List<string>();
		if (Modifiers == null || Modifiers.Length == 0) { problems.Add($"{Name} has no modifiers."); }
		foreach (StatModifier modifier in Modifiers ?? [])
		{
			if (modifier == null) { problems.Add($"{Name} has an empty modifier slot."); }
			else if (modifier.Value == 0f) { problems.Add($"{Name} modifies {modifier.Stat} by 0."); }
		}
		return problems.ToArray();
	}
}
