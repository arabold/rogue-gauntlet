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
	/// <summary>Current health as a share of the maximum; 1 without a positive maximum.</summary>
	public float Fraction => MaxHealth > 0 ? CurrentHealth / MaxHealth : 1;

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
	/// Heals by <paramref name="amount"/>, up to the maximum. Potions, food, fountains and spells heal through here; growth
	/// of the maximum (a level-up, gear) sets health directly instead.
	/// </summary>
	public void Heal(float amount)
	{
		if (amount > 0)
		{
			GD.Print($"{GetOwner()?.Name ?? Name} healed for {amount}.");
			float before = CurrentHealth;
			CurrentHealth = Mathf.Clamp(CurrentHealth + amount, 0, MaxHealth);
			CombatTrace.Write(this, "health.healed", ("amount", CurrentHealth - before), ("health", CurrentHealth), ("max_health", MaxHealth));
			if (!IsNodeReady())
			{
				EmitSignalHealthChanged(CurrentHealth, MaxHealth);
			}
		}
	}

	private void Die()
	{
		GD.Print($"{GetOwner()?.Name ?? Name} has died.");
		EmitSignalDied();
	}
}