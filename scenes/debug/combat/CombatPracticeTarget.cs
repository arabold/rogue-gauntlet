using Godot;

/// <summary>
/// The combat arena's stationary practice target. It belongs to the enemy group and resolves contacts through its
/// hurtbox, so spells, which pick damageable hostile actors, target it like a monster.
/// </summary>
public partial class CombatPracticeTarget : CharacterBody3D, ICombatDamageable
{
	[Export] public HurtBoxComponent HurtBox { get; set; }
	[Export] public HealthComponent Health { get; set; }
	[Export] public MovementComponent Movement { get; set; }
	[Export] public ActorActionGateComponent ActionGate { get; set; }

	public DamageResolution ResolveDamage(in DamageContext contact) => HurtBox != null
		? HurtBox.ResolveDamage(in contact) : new DamageResolution(DamageOutcome.Filtered, 0);
}
