using Godot;

/// <summary>
/// Holds while the actor's <see cref="HealthPhaseComponent"/> phase lies in an inclusive range; phase 0 is full health.
/// An actor without phases is always in phase 0.
/// </summary>
[Tool]
[GlobalClass]
public partial class HealthPhaseCondition : MonsterActionCondition
{
	[Export(PropertyHint.Range, "0,10,1")] public int MinimumPhase { get; set; }
	/// <summary>Highest phase that still holds; -1 means no upper bound.</summary>
	[Export(PropertyHint.Range, "-1,10,1")] public int MaximumPhase { get; set; } = -1;

	public override bool IsMet(EnemyContext context, Node3D target)
	{
		int phase = context.Phase;
		return phase >= MinimumPhase && (MaximumPhase < 0 || phase <= MaximumPhase);
	}

	public override void Validate(MonsterValidationContext context)
	{
		context.Check(MinimumPhase >= 0 && (MaximumPhase < 0 || MaximumPhase >= MinimumPhase),
			"HealthPhaseCondition requires 0 <= MinimumPhase <= MaximumPhase (or MaximumPhase -1).");
		context.Check(MaximumPhase < 0 || MaximumPhase <= context.PhaseCount, "HealthPhaseCondition names a phase the actor never reaches.");
		context.Check(MinimumPhase <= context.PhaseCount, "HealthPhaseCondition names a phase the actor never reaches.");
	}
}
