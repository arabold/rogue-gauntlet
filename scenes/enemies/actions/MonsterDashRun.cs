using Godot;

/// <summary>A started dash: aims at its target through the wind-up, then lasts as long as the dash execution.</summary>
public sealed class MonsterDashRun(MonsterDashAction action, Node3D target, DashComponent dash, AttackIdentity execution)
	: MonsterActionRun(action, target, null)
{
	public override bool IsFinished => !GodotObject.IsInstanceValid(dash) || !dash.IsRunning(execution);

	public override void Tick(EnemyContext context, double delta)
	{
		if (!IsFinished && GodotObject.IsInstanceValid(Target))
			dash.UpdateDirection(execution, action.HeadingTo(context, Target), action.FacingTo(context, Target));
	}

	public override void Cancel()
	{
		if (!IsFinished) dash.Cancel();
	}
}
