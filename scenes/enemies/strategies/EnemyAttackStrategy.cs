using Godot;

/// <summary>Immutable attack policy and payload; execution remains in AttackController.</summary>
[GlobalClass]
public abstract partial class EnemyAttackStrategy : Resource
{
	[Export] public AttackDefinition Attack { get; set; }
	[Export] public float EngagementDistance { get; set; } = 2;
	[Export] public float MinDamage { get; set; } = 1;
	[Export] public float MaxDamage { get; set; } = 5;
	[Export(PropertyHint.Range, "0,1,0.01")] public float CritChance { get; set; }
	[Export] public float Duration { get; set; } = 1;
	/// <summary>Take a turn through the target's attack slots. Bosses that must never stall behind minions opt out.</summary>
	[Export] public bool UsesAttackSlot { get; set; } = true;
	public AttackSlotKind SlotKind => Attack?.IsRanged == true ? AttackSlotKind.Ranged : AttackSlotKind.Melee;
	public abstract StringName Style { get; }
	public abstract float EffectiveRange { get; }
	/// <summary>
	/// Melee: widest angle between the actor's facing and the target at which a swing may start. A heavy
	/// actor turns toward the target first (at its movement turn rate), so circling it avoids swings. 180 = any.
	/// </summary>
	[Export(PropertyHint.Range, "0,180,1")] public float MaxStartAngle { get; set; } = 180;
	public abstract bool CanAttack(EnemyContext context);

	/// <summary>Close enough (and in sight) to attack once facing allows; positioning holds here instead of approaching.</summary>
	public virtual bool IsInReach(EnemyContext context) => CanAttack(context);
}