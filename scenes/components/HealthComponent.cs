using Godot;

[GlobalClass]
public partial class HealthComponent : Node
{
	[Signal] public delegate void HealthChangedEventHandler(float currentHealth, float maxHealth);
	[Signal] public delegate void DiedEventHandler();

	[Export]
	public float MaxHealth
	{
		get;
		set
		{
			if (Mathf.IsEqualApprox(field, value)) { return; }
			field = value;
			if (!IsNodeReady()) { return; }
			EmitSignalHealthChanged(CurrentHealth, MaxHealth);
		}
	} = 10;

	[Export]
	public float CurrentHealth
	{
		get;
		set
		{
			if (Mathf.IsEqualApprox(field, value)) { return; }
			field = value;
			if (!IsNodeReady()) { return; }
			EmitSignalHealthChanged(CurrentHealth, MaxHealth);
		}
	} = 10;
	public bool IsDead => CurrentHealth <= 0;

	public HealthComponent()
	{
		CurrentHealth = MaxHealth;
	}

	public void SetHealth(float health, float maxHealth)
	{
		float clamped = Mathf.Clamp(health, 0, maxHealth);
		if (clamped != CurrentHealth || maxHealth != MaxHealth)
		{
			MaxHealth = maxHealth;
			CurrentHealth = clamped;
			GD.Print($"Updated {GetOwner()?.Name ?? Name} health to {CurrentHealth}/{MaxHealth}.");
			if (!IsNodeReady())
			{
				EmitSignalHealthChanged(CurrentHealth, MaxHealth);
			}
		}
	}

	public void TakeDamage(float amount)
	{
		if (CurrentHealth > 0)
		{
			GD.Print($"{GetOwner()?.Name ?? Name} took {amount} damage.");
			CurrentHealth -= amount;
			if (!IsNodeReady())
			{
				EmitSignalHealthChanged(CurrentHealth, MaxHealth);
			}

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
			GD.Print($"{GetOwner()?.Name ?? Name} healed for {amount}.");
			CurrentHealth = Mathf.Clamp(CurrentHealth + amount, 0, MaxHealth);
			if (!IsNodeReady())
			{
				EmitSignalHealthChanged(CurrentHealth, MaxHealth);
			}
		}
	}

	private void Die()
	{
		GD.Print($"{GetOwner().Name} has died.");
		EmitSignalDied();
	}
}