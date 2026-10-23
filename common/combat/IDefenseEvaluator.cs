using Godot;

public enum DefenseRequestResult
{
	Success,
	Unavailable,
	Busy,
	Cooldown,
	Disabled,
	InvalidDefinition,
	NoDirection
}

public readonly record struct DefenseEvaluation(
	DamageOutcome Outcome,
	float Amount,
	bool ConsumesParry,
	bool SuppressHitStun);

/// <summary>
/// Strategy contract for damage mitigation components (e.g. dodge, guard/block, barrier).
/// Allows HurtBoxComponent to evaluate incoming attacks polymorphically without concrete coupling.
/// </summary>
public interface IDefenseEvaluator
{
	bool TryEvaluate(in DamageContext contact, out DefenseEvaluation evaluation);
}