using Godot;

/// <summary>
/// What drinking from a fountain grants. Shared and stateless: the drinker is passed in, so one Resource serves every
/// fountain of its kind. A blessing that would grant nothing blocks the drink, so a full pool is never wasted.
/// </summary>
[GlobalClass]
public abstract partial class FountainBlessing : Resource
{
	/// <summary>Prompt; shown blocked while drinking would grant nothing.</summary>
	[Export] public string DrinkLabel { get; set; } = "Drink";
	/// <summary>Optional burst on the drinker after a successful drink.</summary>
	[Export] public WorldEffectDefinition DrinkEffect { get; set; }

	public abstract bool CanBenefit(Player drinker);

	public abstract void Grant(Player drinker);
}