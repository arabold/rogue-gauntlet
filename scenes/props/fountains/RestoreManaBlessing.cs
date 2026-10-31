using Godot;

/// <summary>Refills the drinker's mana to its maximum.</summary>
[GlobalClass]
public partial class RestoreManaBlessing : FountainBlessing
{
	public override bool CanBenefit(Player drinker) =>
		drinker?.ManaComponent is { } mana && drinker.HealthComponent is { IsDead: false } && mana.CurrentMana < mana.MaxMana;

	public override void Grant(Player drinker) => drinker.ManaComponent.SetMana(drinker.ManaComponent.MaxMana, drinker.ManaComponent.MaxMana);
}