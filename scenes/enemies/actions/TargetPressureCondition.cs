using Godot;

/// <summary>
/// Holds once the hostile target has spent <see cref="Duration"/> seconds visible within <see cref="Range"/>: a boss
/// answering a player who hugs it. Stepping out of range pauses the count; losing sight or a new target resets it.
/// </summary>
[Tool]
[GlobalClass]
public partial class TargetPressureCondition : MonsterActionCondition
{
	[Export(PropertyHint.Range, "0,30,0.1,suffix:m")] public float Range { get; set; } = 2;
	[Export(PropertyHint.Range, "0,30,0.05,suffix:s")] public float Duration { get; set; } = 1;

	public override void Tick(EnemyContext context, double delta)
	{
		if (!GodotObject.IsInstanceValid(context.Target) || !context.HasLineOfSightToTarget())
		{
			context.Actions.SetConditionTime(this, 0);
			return;
		}
		if (context.HorizontalTargetDistance <= Range)
			context.Actions.SetConditionTime(this, Mathf.Min(Duration, context.Actions.ConditionTime(this) + (float)delta));
	}

	public override bool IsMet(EnemyContext context, Node3D target) => context.Actions.ConditionTime(this) >= Duration;

	public override void Validate(MonsterValidationContext context) =>
		context.Check(float.IsFinite(Range) && Range > 0 && float.IsFinite(Duration) && Duration >= 0,
			"TargetPressureCondition needs a positive Range and a nonnegative Duration.");
}
