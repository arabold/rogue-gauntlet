using Godot;

public enum EnemyMovementGoal { Hold, Approach, Retreat }

/// <summary>A decision, not a locomotion operation; the host applies navigation and action restrictions.</summary>
public readonly record struct EnemyMovementIntent(EnemyMovementGoal Goal, bool AllowAttack, float DesiredDistance = 0);

/// <summary>Shared positioning policy; no per-actor state belongs on a Resource.</summary>
[GlobalClass]
public abstract partial class EnemyMovementStrategy : Resource
{
	public abstract EnemyMovementIntent Evaluate(EnemyContext context);
}