using Godot;

/// <summary>Encircle holds a spread ring position around a target whose attack slots are all taken.</summary>
public enum EnemyMovementGoal { Hold, Approach, Retreat, Encircle }

/// <summary>
/// A decision, not a locomotion operation; the host applies navigation and action restrictions. Offensive actions start
/// only while <see cref="AllowAttack"/> is set; support actions start either way.
/// </summary>
public readonly record struct EnemyMovementIntent(EnemyMovementGoal Goal, bool AllowAttack, float DesiredDistance = 0, bool PreferRear = false);

/// <summary>Shared positioning policy; no per-actor state belongs on a Resource.</summary>
[Tool]
[GlobalClass]
public abstract partial class EnemyMovementStrategy : Resource
{
	public abstract EnemyMovementIntent Evaluate(EnemyContext context);
	/// <summary>Lets stateful tactics react to a committed attack without coupling the host to a strategy type.</summary>
	public virtual void AttackAccepted(EnemyContext context, AttackIdentity identity) { }

	/// <summary>Authoring problems against the combat profile this strategy positions for.</summary>
	public virtual void Validate(MonsterValidationContext context, EnemyCombatProfile combat) { }

	/// <summary>Strategies that close to melee need an offensive action to close for.</summary>
	protected static void RequirePrimary(MonsterValidationContext context, EnemyCombatProfile combat) =>
		context.Check(combat.PrimaryOffense != null, "MovementStrategy: list an offensive attack or spell; the first one sets the engagement range.");
}
