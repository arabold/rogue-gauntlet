using Godot;

/// <summary>
/// How a monster fights: positioning, engagement tactics, defense, an optional low-health retreat and the actions it
/// chooses from. The first offensive attack or spell is the primary action: movement strategies close to its range and
/// wait for its attack slot, while every other eligible action (a rush, a support spell) can still fire.
/// </summary>
[Tool]
[GlobalClass]
public partial class EnemyCombatProfile : Resource
{
	[ExportGroup("Tactics")]
	[Export] public EnemyTacticalStrategy TacticalStrategy { get; set; }
	[Export] public EnemyMovementStrategy MovementStrategy { get; set; }
	[Export] public EnemyDefenseStrategy DefenseStrategy { get; set; }
	[Export] public EnemyRetreatCondition RetreatCondition { get; set; }
	[ExportGroup("Actions")]
	/// <summary>Actions in authored order; monster-only actions can live here as sub-resources.</summary>
	[Export] public Godot.Collections.Array<MonsterAction> Actions { get; set; } = [];
	/// <summary>Weight factor for repeating the action that started last, so a monster varies its moves.</summary>
	[Export(PropertyHint.Range, "0,1,0.01")] public float RepeatWeightMultiplier { get; set; } = 0.5f;

	/// <summary>The first offensive attack or spell, which sets the engagement range and attack slot for movement.</summary>
	public MonsterAction PrimaryOffense
	{
		get
		{
			foreach (var action in Actions)
				if (IsPrimaryCandidate(action)) return action;
			return null;
		}
	}

	/// <summary>Only an offensive attack or spell can anchor engagement; a dash has no reach or slot of its own.</summary>
	public static bool IsPrimaryCandidate(MonsterAction action) => action is MonsterTimedAction { IsOffensive: true };

	public void Validate(MonsterValidationContext context)
	{
		context.Check(MonsterValidationContext.IsProbability(RepeatWeightMultiplier),
			"RepeatWeightMultiplier must lie between 0 and 1.");
		if (context.Check(TacticalStrategy != null, "TacticalStrategy: assign an engagement priority strategy."))
			TacticalStrategy.Validate(context);
		if (context.Check(MovementStrategy != null, "MovementStrategy: assign a positioning strategy."))
			MovementStrategy.Validate(context, this);
		DefenseStrategy?.Validate(context);
		RetreatCondition?.Validate(context);
		var seen = new System.Collections.Generic.HashSet<MonsterAction>();
		for (int index = 0; index < Actions.Count; index++)
		{
			var action = Actions[index];
			if (!context.Check(action != null, $"Actions[{index}] is empty.")) continue;
			context.Check(seen.Add(action), $"Actions: '{action.Label}' is listed more than once.");
			using (context.Scope($"Actions[{index}] '{action.Label}'")) action.Validate(context);
		}
	}
}
