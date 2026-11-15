using Godot;

/// <summary>
/// Encircle holds a spread ring position around a target whose attack slots are all taken. Flank moves to the monster's
/// ring point before it closes in (<see cref="FlankStrategy"/>). Both are ring goals: the host moves the monster to its
/// point on the target's ring.
/// </summary>
public enum EnemyMovementGoal { Hold, Approach, Retreat, Encircle, Flank }

/// <summary>
/// A decision, not a locomotion operation; the host applies navigation and action restrictions. Offensive actions start
/// only while <see cref="AllowAttack"/> is set; support actions start either way. For the ring goals,
/// <see cref="DesiredDistance"/> is the ring's radius, <see cref="Arc"/> the degrees behind the target that ring points
/// spread over (<see cref="AttackSlotsComponent.GetRingBearing"/>) and <see cref="OrbitStep"/> the most degrees the
/// monster swings around the target per ring point, so it circles the target instead of crossing it.
/// </summary>
public readonly record struct EnemyMovementIntent(EnemyMovementGoal Goal, bool AllowAttack, float DesiredDistance = 0,
	float Arc = 360, float OrbitStep = 180);

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
