using Godot;

/// <summary>Encircle holds a spread ring position around a target whose attack slots are all taken.</summary>
public enum EnemyMovementGoal { Hold, Approach, Retreat, Encircle }

/// <summary>A decision, not a locomotion operation; the host applies navigation and action restrictions.</summary>
public readonly record struct EnemyMovementIntent(EnemyMovementGoal Goal, bool AllowAttack, float DesiredDistance = 0, bool PreferRear = false);

/// <summary>Shared positioning policy; no per-actor state belongs on a Resource.</summary>
[GlobalClass]
public abstract partial class EnemyMovementStrategy : Resource
{
	public abstract EnemyMovementIntent Evaluate(EnemyContext context);
	/// <summary>Lets stateful tactics react to a committed attack without coupling the host to a strategy type.</summary>
	public virtual void AttackAccepted(EnemyContext context, AttackIdentity identity) { }
}
