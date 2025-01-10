using Godot;

public partial class HealthComponent : Node
{
	[Export] public int MaxHealth { get; set; } = 10;
	[Export] public PackedScene HitEffect { get; set; }

	[Signal] public delegate void HealthChangedEventHandler(int currentHealth, int maxHealth);
	[Signal] public delegate void DiedEventHandler();

	public int CurrentHealth { get; private set; }

	public override void _Ready()
	{
		CurrentHealth = MaxHealth;
	}

	public void TakeDamage(int amount)
	{
		if (CurrentHealth > 0)
		{
			CurrentHealth -= amount;
			SpawnHitEffect();

			EmitSignal(SignalName.HealthChanged, CurrentHealth, MaxHealth);

			if (CurrentHealth <= 0)
			{
				Die();
			}
		}
	}

	public void Heal(int amount)
	{
		CurrentHealth += amount;
		CurrentHealth = Mathf.Clamp(CurrentHealth, 0, MaxHealth);
		EmitSignal(SignalName.HealthChanged, CurrentHealth, MaxHealth);
	}

	private void Die()
	{
		EmitSignal(SignalName.Died);
	}

	private void SpawnHitEffect()
	{
		if (HitEffect == null)
		{
			return;
		}

		var hitEffect = HitEffect.Instantiate<GpuParticles3D>();
		hitEffect.GlobalTransform = GetParent<Node3D>().GlobalTransform; // Position the effect at the enemy's location
		hitEffect.OneShot = true;

		// Add to the scene
		GetParent().GetParent().AddChild(hitEffect);
	}
}
