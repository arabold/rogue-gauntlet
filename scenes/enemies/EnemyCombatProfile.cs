using Godot;

/// <summary>One switchable set of movement, defense and action choices for an enemy.</summary>
[GlobalClass]
public partial class EnemyCombatProfile : Resource
{
	[ExportGroup("Tactics")]
	[Export] public EnemyTacticalStrategy TacticalStrategy { get; set; }
	[Export] public EnemyMovementStrategy MovementStrategy { get; set; }
	[Export] public EnemyDefenseStrategy DefenseStrategy { get; set; }
	[Export] public EnemyRetreatCondition RetreatCondition { get; set; }
	[Export] public EnemyActionProfile ApproachAction { get; set; }
	[Export] public Godot.Collections.Array<EnemyActionChoiceProfile> Actions { get; set; } = [];
	[Export(PropertyHint.Range, "0,1,0.01")] public float RepeatWeightMultiplier { get; set; } = 0.5f;

	/// <summary>Returns authoring errors that would make the profile impossible or ambiguous to execute.</summary>
	public string[] Validate()
	{
		var errors = new System.Collections.Generic.List<string>();
		if (MovementStrategy == null) errors.Add("combat profile has no movement strategy");
		if (!float.IsFinite(RepeatWeightMultiplier) || RepeatWeightMultiplier < 0 || RepeatWeightMultiplier > 1)
			errors.Add("combat profile repeat weight multiplier must be between zero and one");
		if (Actions == null) errors.Add("combat profile action choices are missing");
		else
		{
			bool approachFound = ApproachAction == null;
			var seen = new System.Collections.Generic.HashSet<ulong>();
			foreach (var choice in Actions)
			{
				if (choice?.Action == null) { errors.Add("combat profile has an empty action choice"); continue; }
				if (!float.IsFinite(choice.Weight) || choice.Weight <= 0) errors.Add($"action '{choice.Action.ResourceName}' has an invalid weight");
				if (!choice.Action.HasValidConfiguration) errors.Add($"action '{choice.Action.ResourceName}' has invalid configuration");
				if (!seen.Add(choice.Action.GetInstanceId())) errors.Add($"action '{choice.Action.ResourceName}' is listed more than once");
				if (ReferenceEquals(choice.Action, ApproachAction)) approachFound = true;
			}
			if (ApproachAction != null && (!ApproachAction.IsOffensive || !approachFound))
				errors.Add("approach action must be an offensive action choice in the same combat profile");
		}
		return errors.ToArray();
	}
}
