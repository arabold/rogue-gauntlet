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

	/// <summary>Per-actor pressure time, kept by the actor's runtime.</summary>
	private sealed class Pressure { public float Seconds; }

	public override void Tick(EnemyContext context, double delta)
	{
		var pressure = context.Actions.State<Pressure>(this);
		if (!GodotObject.IsInstanceValid(context.Target) || !context.HasLineOfSightToTarget()) pressure.Seconds = 0;
		else if (context.HorizontalTargetDistance <= Range) pressure.Seconds = Mathf.Min(Duration, pressure.Seconds + (float)delta);
	}

	public override bool IsMet(EnemyContext context, Node3D target) => context.Actions.State<Pressure>(this).Seconds >= Duration;

	public override void Validate(MonsterValidationContext context) =>
		context.Check(MonsterValidationContext.IsPositive(Range) && MonsterValidationContext.IsNonnegative(Duration),
			"TargetPressureCondition needs a positive Range and a nonnegative Duration.");
}
