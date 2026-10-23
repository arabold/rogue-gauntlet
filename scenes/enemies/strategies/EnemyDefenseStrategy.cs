using Godot;

/// <summary>Exclusive reactions to one observed threat; controllers retain timers, RNG and guard state.</summary>
[GlobalClass]
public partial class EnemyDefenseStrategy : Resource
{
	[Export(PropertyHint.Range, "0,1,0.01")] public float GuardChance { get; set; }
	[Export(PropertyHint.Range, "0,1,0.01")] public float DodgeChance { get; set; }
	[Export(PropertyHint.Range, "0,1,0.01")] public float RiposteChance { get; set; } = .65f;
	[Export] public float ReactionDelay { get; set; } = .04f;
	[Export] public float GuardPressureWindow { get; set; } = .85f;
	[Export] public float MaxGuardDuration { get; set; } = 3.5f;
	[Export] public bool CanDefendProjectiles { get; set; } = true;

	/// <summary>Unavailable capabilities leave their interval as no response; never reroll.</summary>
	public EnemyDefenseMode Choose(bool canGuard, bool canDodge, float roll)
	{
		if (roll < DodgeChance) return canDodge ? EnemyDefenseMode.Dodge : EnemyDefenseMode.None;
		if (roll < DodgeChance + GuardChance) return canGuard ? EnemyDefenseMode.GuardParry : EnemyDefenseMode.None;
		return EnemyDefenseMode.None;
	}
}