using Godot;

/// <summary>
/// A head-down rush: a melee attack whose contact window is a run along the facing it locks before contact. The wind-up
/// warns with a lane as long as the run; the run carries the attack's hit and knockback, stops at its contact window's
/// end and, when it meets a wall head-on, ends early and staggers the actor, the opening a sidestepping target earns.
/// </summary>
[Tool]
[GlobalClass]
public partial class MonsterChargeAction : MonsterAttackAction
{
	[ExportGroup("Charge")]
	/// <summary>Run speed through the contact window; the run's length is this speed times the window.</summary>
	[Export(PropertyHint.Range, "1,30,0.5,suffix:m/s")] public float Speed { get; set; } = 10;
	/// <summary>How long a charge that ends against a wall leaves the actor staggered; 0 recovers normally.</summary>
	[Export(PropertyHint.Range, "0,5,0.05,suffix:s")] public float ImpactStagger { get; set; } = 1.5f;
	/// <summary>Optional camera shake where a charge meets a wall.</summary>
	[Export] public CameraShakeProfile ImpactShake { get; set; }

	/// <summary>A charge covers its own distance, so only <see cref="MonsterAction.MaximumRange"/> bounds its start.</summary>
	public override float Reach => float.PositiveInfinity;

	/// <summary>Only along a body-wide clear line to the target, so a charge never starts into a wall or around a corner.</summary>
	public override bool IsUsefulOn(EnemyContext context, Node3D target)
	{
		Vector3 toward = target.GlobalPosition - context.Actor.GlobalPosition;
		return context.Navigation?.HasClearDash(toward, toward.Length()) == true;
	}

	protected override MonsterActionRun CreateRun(Node3D target, AttackSlotsComponent slots, AttackController controller) =>
		new MonsterChargeRun(this, target, slots, controller, controller.CurrentExecutionId);

	public override void Validate(MonsterValidationContext context)
	{
		base.Validate(context);
		context.Check(Attack?.IsRanged != true, "a charge carries melee contact, not a projectile.");
		context.Check(float.IsFinite(Speed) && Speed > 0, "Speed must be positive.");
		context.Check(MonsterValidationContext.IsNonnegative(ImpactStagger), "ImpactStagger must be nonnegative.");
		context.Check(ImpactShake == null || ImpactShake.IsValid, "ImpactShake: assign a valid shake or none.");
	}
}
