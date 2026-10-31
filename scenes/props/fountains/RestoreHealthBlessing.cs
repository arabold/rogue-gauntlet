using Godot;

/// <summary>Refills the drinker's health to its maximum.</summary>
[GlobalClass]
public partial class RestoreHealthBlessing : FountainBlessing
{
	public override bool CanBenefit(Player drinker) =>
		drinker?.HealthComponent is { IsDead: false } health && health.CurrentHealth < health.MaxHealth;

	public override void Grant(Player drinker)
	{
		var health = drinker.HealthComponent;
		health.Heal(health.MaxHealth - health.CurrentHealth);
	}
}