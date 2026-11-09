using Godot;

/// <summary>Scales an action's weight while a condition holds: favor a slam in a late phase, a bite on a weakened target.</summary>
[Tool]
[GlobalClass]
public partial class MonsterActionWeightModifier : Resource
{
	[Export] public MonsterActionCondition Condition { get; set; }
	/// <summary>Factor applied to the action's weight; modifiers that hold together multiply.</summary>
	[Export(PropertyHint.Range, "0,20,0.05")] public float Multiplier { get; set; } = 1;
}
