using Godot;

public partial class HealthComponent : Node
{
	[Signal] public delegate void HealthChangedEventHandler(float currentHealth, float maxHealth);
	[Signal] public delegate void DiedEventHandler();

	[Export] public float MaxHealth { get; private set; } = 10;
	[Export] public float CurrentHealth { get; private set; } = 10;
	public bool IsDead => CurrentHealth <= 0;

	HealthComponent()
	{
		CurrentHealth = MaxHealth;
	}

	public void SetHealth(float health, float maxHealth)
	{
		if (health != CurrentHealth || maxHealth != MaxHealth)
		{
			MaxHealth = maxHealth;
			CurrentHealth = Mathf.Clamp(health, 0, MaxHealth);
			GD.Print($"Updated {GetOwner().Name} health to {CurrentHealth}/{MaxHealth}.");
			EmitSignalHealthChanged(CurrentHealth, MaxHealth);
		}
	}

	public void TakeDamage(float amount)
	{
		if (CurrentHealth > 0)
		{
			GD.Print($"{GetOwner().Name} took {amount} damage.");
			CurrentHealth -= amount;
			EmitSignalHealthChanged(CurrentHealth, MaxHealth);

			if (CurrentHealth <= 0)
			{
				Die();
			}
		}
	}

	/// <summary>
	/// Heals the player by the specified amount. 
	/// </summary>
	/// <param name="amount"></param>
	public void Heal(float amount)
	{
		if (amount > 0)
		{
			GD.Print($"{GetOwner().Name} healed for {amount}.");
			CurrentHealth += amount;
			CurrentHealth = Mathf.Clamp(CurrentHealth, 0, MaxHealth);
			EmitSignalHealthChanged(CurrentHealth, MaxHealth);
		}
	}

	private void Die()
	{
		GD.Print($"{GetOwner().Name} has died.");
		EmitSignalDied();
	}
}
