using Godot;

/// <summary>
/// An action whose wind-up, warning, contact and recovery run on the actor's <see cref="AttackController"/> clock:
/// weapon attacks and spell casts. Holds the timing and the hit power both share.
/// </summary>
[Tool]
[GlobalClass]
public abstract partial class MonsterTimedAction : MonsterAction
{
	/// <summary>Collision layer of the player's hurtbox, the only thing monster attacks can damage.</summary>
	protected const uint HostileMask = 4;

	[ExportGroup("Timing and power")]
	/// <summary>Animation style bound in the actor's attack animation profile; the definition's own style wins.</summary>
	[Export] public StringName Style { get; set; } = "melee";
	/// <summary>Whole attack length in seconds; 0 plays the bound clip at its own length.</summary>
	[Export(PropertyHint.Range, "0,10,0.01,suffix:s")] public float Duration { get; set; } = 1;
	[Export(PropertyHint.Range, "0,500,0.5,or_greater")] public float MinDamage { get; set; } = 1;
	[Export(PropertyHint.Range, "0,500,0.5,or_greater")] public float MaxDamage { get; set; } = 5;
	[Export(PropertyHint.Range, "0,1,0.01")] public float CritChance { get; set; }

	/// <summary>The attack definition the controller executes.</summary>
	protected abstract AttackDefinition Definition { get; }

	/// <summary>Style the controller resolves: the definition's authored style, otherwise <see cref="Style"/>.</summary>
	public StringName ResolvedStyle => Definition != null && !string.IsNullOrWhiteSpace(Definition.AttackStyle) ? Definition.AttackStyle : Style;

	public override bool IsAvailable(EnemyContext context) => Definition != null;

	/// <summary>Hit power after the actor's stat modifiers (Rage and similar buffs).</summary>
	protected AttackStats ResolvePower(EnemyContext context) => context.Actor.Stats.ResolveAttack(MinDamage, MaxDamage, CritChance);

	public override void Validate(MonsterValidationContext context)
	{
		base.Validate(context);
		context.Check(MonsterValidationContext.IsNonnegative(Duration), "Duration must be nonnegative.");
		context.Check(MonsterValidationContext.IsNonnegative(MinDamage) && float.IsFinite(MaxDamage) && MaxDamage >= MinDamage
			&& MonsterValidationContext.IsProbability(CritChance), "require 0 <= MinDamage <= MaxDamage and a CritChance in [0, 1].");
		if (Definition != null) context.CheckBinding(ResolvedStyle);
	}
}
