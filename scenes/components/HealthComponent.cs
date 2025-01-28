using Godot;

public partial class HealthComponent : Node
{
	[Export] public int MaxHealth { get; set; } = 10;

	[Signal] public delegate void HealthChangedEventHandler(int currentHealth, int maxHealth);
	[Signal] public delegate void DiedEventHandler();

	public int CurrentHealth { get; private set; }
	public bool IsDead => CurrentHealth <= 0;

	public override void _Ready()
	{
		CurrentHealth = MaxHealth;
	}

	public void SetHealth(int health)
	{
		CurrentHealth = Mathf.Clamp(health, 0, MaxHealth);
		EmitSignalHealthChanged(CurrentHealth, MaxHealth);
	}

	public void SetMaxHealth(int maxHealth)
	{
		MaxHealth = maxHealth;
		CurrentHealth = Mathf.Clamp(CurrentHealth, 0, MaxHealth);
		EmitSignalHealthChanged(CurrentHealth, MaxHealth);
	}

	public void TakeDamage(int amount)
	{
		if (CurrentHealth > 0)
		{
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
	/// A negative amount will damage the player.
	/// </summary>
	/// <param name="amount"></param>
	public void Heal(int amount)
	{
		CurrentHealth += amount;
		CurrentHealth = Mathf.Clamp(CurrentHealth, 0, MaxHealth);
		EmitSignalHealthChanged(CurrentHealth, MaxHealth);
	}

	private void Die()
	{
		EmitSignalDied();
	}
}
