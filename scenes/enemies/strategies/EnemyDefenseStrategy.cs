using Godot;

/// <summary>Exclusive reactions to one observed threat; controllers retain timers, RNG and guard state.</summary>
[Tool]
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

	/// <summary>
	/// Unavailable capabilities leave their interval as no response; never reroll. When the observed attack is
	/// not safe to guard (not Guardable, or BreaksGuard), the guard interval dodges instead if the actor can.
	/// </summary>
	public EnemyDefenseMode Choose(bool canGuard, bool canDodge, float roll, bool guardSafe = true)
	{
		if (roll < DodgeChance) return canDodge ? EnemyDefenseMode.Dodge : EnemyDefenseMode.None;
		if (roll < DodgeChance + GuardChance)
			return guardSafe ? (canGuard ? EnemyDefenseMode.GuardParry : EnemyDefenseMode.None)
				: canDodge ? EnemyDefenseMode.Dodge : EnemyDefenseMode.None;
		return EnemyDefenseMode.None;
	}

	/// <summary>Probabilities and timings; the actor's guard/dodge wiring is checked by <see cref="MonsterSceneValidator"/>.</summary>
	public void Validate(MonsterValidationContext context)
	{
		context.Check(Probability(GuardChance) && Probability(DodgeChance) && Probability(RiposteChance) && GuardChance + DodgeChance <= 1,
			"DefenseStrategy: probabilities must be in [0,1], with guard + dodge <= 1.");
		context.Check(float.IsFinite(ReactionDelay) && ReactionDelay >= 0 && float.IsFinite(GuardPressureWindow) && GuardPressureWindow > 0
			&& float.IsFinite(MaxGuardDuration) && MaxGuardDuration > 0, "DefenseStrategy: invalid delay or guard durations.");
		context.CheckDefenseCapabilities(GuardChance, DodgeChance);
	}

	private static bool Probability(float value) => float.IsFinite(value) && value is >= 0 and <= 1;
}
