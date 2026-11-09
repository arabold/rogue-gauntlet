using Godot;

/// <summary>Authored engagement priorities. Creates isolated decision state for each actor; never stores runtime timers.</summary>
[Tool]
[GlobalClass]
public partial class EnemyTacticalStrategy : Resource
{
	[Export] public float MaximumRetreatDuration { get; set; } = 2;
	[Export] public float RetreatRetryDelay { get; set; } = .5f;
	[Export] public float StandAndFightDistance { get; set; }
	[Export] public bool RequireAttackAfterRetreat { get; set; }

	/// <summary>Snapshot the authored settings when an actor initializes. Reinstantiate to apply definition edits.</summary>
	public EnemyTacticalSelector CreateRuntime() => new(MaximumRetreatDuration, RetreatRetryDelay,
		StandAndFightDistance, RequireAttackAfterRetreat);

	public void Validate(MonsterValidationContext context) =>
		context.Check(float.IsFinite(MaximumRetreatDuration) && MaximumRetreatDuration > 0 && float.IsFinite(RetreatRetryDelay)
			&& RetreatRetryDelay >= 0 && float.IsFinite(StandAndFightDistance) && StandAndFightDistance >= 0,
			"TacticalStrategy: require positive retreat duration and nonnegative finite retry/stand distances.");
}
