using Godot;

/// <summary>Which way a dash travels relative to its target.</summary>
public enum MonsterDashDirection
{
	/// <summary>Close the gap: a rush that ends in an ordinary attack opportunity.</summary>
	Toward,
	/// <summary>Open the gap: a hop back out of reach.</summary>
	Away
}

/// <summary>A short committed displacement executed by the actor's <see cref="DashComponent"/>; it deals no damage.</summary>
[Tool]
[GlobalClass]
public partial class MonsterDashAction : MonsterAction
{
	[ExportGroup("Dash")]
	[Export] public DashProfile Dash { get; set; }
	[Export] public MonsterDashDirection Direction { get; set; } = MonsterDashDirection.Toward;

	/// <summary>A rush toward the target is an attack move; a hop back is evasive and stays allowed while retreating.</summary>
	public override bool IsOffensive => base.IsOffensive && Direction == MonsterDashDirection.Toward;

	public override bool IsAvailable(EnemyContext context) => context.Enemy?.DashComponent != null && Dash != null;

	/// <summary>Only when a body-sized straight corridor is clear (and stays in a held actor's room), so a blocked dash is never chosen.</summary>
	public override bool IsUsefulOn(EnemyContext context, Node3D target) =>
		context.Navigation?.HasClearDash(HeadingTo(context, target), Dash.Distance) == true;

	public override ActorActionRequestResult TryStart(EnemyContext context, Node3D target, out MonsterActionRun run)
	{
		run = null;
		var dash = context.Enemy?.DashComponent;
		if (dash == null || Dash == null || !GodotObject.IsInstanceValid(target)) return ActorActionRequestResult.Unavailable;
		if (!IsUsefulOn(context, target)) return ActorActionRequestResult.Unavailable;
		var result = dash.TryDash(Dash, HeadingTo(context, target), out var execution, FacingTo(context, target));
		if (result != ActorActionRequestResult.Success) return result;
		run = new MonsterDashRun(this, target, dash, execution);
		return ActorActionRequestResult.Success;
	}

	/// <summary>A dash always keeps watching its target, so a hop-back does not turn its back.</summary>
	public Vector3 FacingTo(EnemyContext context, Node3D target) => target.GlobalPosition - context.Actor.GlobalPosition;

	public Vector3 HeadingTo(EnemyContext context, Node3D target)
	{
		Vector3 toward = target.GlobalPosition - context.Actor.GlobalPosition;
		return Direction == MonsterDashDirection.Toward ? toward : -toward;
	}

	public override void Validate(MonsterValidationContext context)
	{
		base.Validate(context);
		context.Check(EffectiveTarget is not SelfTargetSelector, "a dash needs a target to move relative to.");
		if (!context.Check(Dash?.IsValid == true, "assign a valid DashProfile.")) return;
		context.CheckDash(Dash);
	}
}
