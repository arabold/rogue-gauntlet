using Godot;

/// <summary>One melee or projectile attack executed by the actor's existing AttackController.</summary>
[GlobalClass]
public partial class EnemyAttackProfile : EnemyActionProfile
{
	[ExportGroup("Attack")]
	[Export] public AttackDefinition Attack { get; set; }
	[Export] public StringName Style { get; set; } = "melee";
	[Export(PropertyHint.Range, "0,10,0.01")] public float Duration { get; set; }
	[Export] public float MinDamage { get; set; } = 1;
	[Export] public float MaxDamage { get; set; } = 5;
	[Export(PropertyHint.Range, "0,1,0.01")] public float CritChance { get; set; }

	protected override float EffectiveMaximumDistance
	{
		get
		{
			float attackReach = Attack?.IsRanged == true ? Attack.Range
				: Attack?.MeleeContact?.EngagementDistance ?? MaximumDistance;
			return Mathf.Min(MaximumDistance, attackReach);
		}
	}

	public override bool HasValidConfiguration => base.HasValidConfiguration
		&& Attack != null && !string.IsNullOrWhiteSpace(Style)
		&& float.IsFinite(Duration) && Duration >= 0
		&& float.IsFinite(MinDamage) && MinDamage >= 0
		&& float.IsFinite(MaxDamage) && MaxDamage >= MinDamage
		&& float.IsFinite(CritChance) && CritChance >= 0 && CritChance <= 1
		&& (Attack.IsRanged ? SlotKind is EnemyActionSlotKind.None or EnemyActionSlotKind.Ranged
			: SlotKind is EnemyActionSlotKind.None or EnemyActionSlotKind.Melee);
}
