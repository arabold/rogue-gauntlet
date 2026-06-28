using Godot;

/// <summary>
/// Data-driven buff that applies one or more <see cref="StatModifier"/>s for its duration
/// and reverses them on removal. Covers the common "+X stat" / "+Y% stat" effects without a
/// bespoke <see cref="Buff"/> subclass each. Use <see cref="Buff.Duration"/> 0 for a permanent
/// modifier (e.g. a strength potion or equipment bonus); periodic effects still use a
/// <see cref="PeriodicBuff"/> subclass.
/// </summary>
[GlobalClass]
public partial class StatModifierBuff : Buff
{
	[Export] public StatModifier[] Modifiers { get; set; } = [];

	public override void OnApply(Player player)
	{
		player.Stats.AddModifiers(this, Modifiers);
	}

	public override void OnRemove(Player player)
	{
		player.Stats.RemoveModifiersFrom(this);
	}
}
