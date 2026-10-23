using Godot;

/// <summary>Immutable attack policy and payload; execution remains in AttackController.</summary>
[GlobalClass]
public abstract partial class EnemyAttackStrategy : Resource
{
	[Export] public AttackDefinition Attack { get; set; }
	[Export] public float EngagementDistance { get; set; } = 2;
	[Export] public float MinDamage { get; set; } = 1;
	[Export] public float MaxDamage { get; set; } = 5;
	[Export(PropertyHint.Range, "0,1,0.01")] public float Accuracy { get; set; } = .8f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float CritChance { get; set; }
	[Export] public float Duration { get; set; } = 1;
	public abstract StringName Style { get; }
	public abstract float EffectiveRange { get; }
	public abstract bool CanAttack(EnemyContext context);
}