using Godot;

public partial class HealthComponent : Node
{
	[Export] public int MaxHitPoints { get; set; } = 10;
	[Export] public PackedScene HitEffect { get; set; }

	[Signal] public delegate void HealthChangedEventHandler(int currentHitPoints);
	[Signal] public delegate void DiedEventHandler();

	public int CurrentHitPoints { get; private set; }

	public override void _Ready()
	{
		CurrentHitPoints = MaxHitPoints;
	}

	public void TakeDamage(int amount)
	{
		if (CurrentHitPoints > 0)
		{
			CurrentHitPoints -= amount;
			SpawnHitEffect();

			EmitSignal(SignalName.HealthChanged, CurrentHitPoints);

			if (CurrentHitPoints <= 0)
			{
				Die();
			}
		}
	}

	private void Die()
	{
		EmitSignal(SignalName.Died);
	}

	private void SpawnHitEffect()
	{
		if (HitEffect == null)
		{
			GD.PrintErr("HitEffectScene is not set!");
			return;
		}

		var hitEffect = HitEffect.Instantiate<GpuParticles3D>();
		hitEffect.GlobalTransform = GetParent<Node3D>().GlobalTransform; // Position the effect at the enemy's location
		hitEffect.OneShot = true;

		// Add to the scene
		GetParent().GetParent().AddChild(hitEffect);
	}
}
