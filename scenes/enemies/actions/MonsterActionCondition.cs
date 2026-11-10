using Godot;

/// <summary>
/// One reusable "only when…" rule for a <see cref="MonsterAction"/> or a weight modifier. Conditions are stateless
/// shared Resources; a condition that needs memory (how long something has held) keeps it in the actor's
/// <see cref="MonsterActionRuntime"/>, keyed by the condition.
/// </summary>
[Tool]
[GlobalClass]
public abstract partial class MonsterActionCondition : Resource
{
	/// <summary>Whether the rule holds for the action aimed at <paramref name="target"/>.</summary>
	public abstract bool IsMet(EnemyContext context, Node3D target);

	/// <summary>Advance per-actor state once per physics frame; most conditions have none.</summary>
	public virtual void Tick(EnemyContext context, double delta) { }

	public virtual void Validate(MonsterValidationContext context) { }
}
