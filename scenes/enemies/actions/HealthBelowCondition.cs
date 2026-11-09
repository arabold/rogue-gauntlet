using Godot;

/// <summary>Whose state a condition reads.</summary>
public enum MonsterActionSubject
{
	/// <summary>The monster performing the action.</summary>
	Actor,
	/// <summary>The action's selected target (an ally for a support spell, the player for an attack).</summary>
	Target
}

/// <summary>Holds while the subject's health is at or below a fraction of its maximum: heal thresholds, desperation moves.</summary>
[Tool]
[GlobalClass]
public partial class HealthBelowCondition : MonsterActionCondition
{
	[Export] public MonsterActionSubject Subject { get; set; } = MonsterActionSubject.Target;
	[Export(PropertyHint.Range, "0,1,0.01")] public float Fraction { get; set; } = 0.5f;

	public override bool IsMet(EnemyContext context, Node3D target)
	{
		var subject = Subject == MonsterActionSubject.Actor ? context.Actor : target;
		return subject is IBuffTarget { Health: { IsDead: false } health } && health.Fraction <= Fraction;
	}

	public override void Validate(MonsterValidationContext context) =>
		context.Check(MonsterValidationContext.IsProbability(Fraction) && Fraction > 0, "HealthBelowCondition.Fraction must lie in (0, 1].");
}
