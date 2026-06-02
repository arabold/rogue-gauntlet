using Godot;

/// <summary>
/// Starts child particle systems and frees the effect after a short authored lifetime.
/// </summary>
public partial class TimedEffect : Node3D, IPooledNode
{
	[Export] public float Lifetime { get; set; } = 1.0f;

	private float _remainingLifetime;

	public override void _Ready()
	{
		StartLifetime();
	}

	public override void _Process(double delta)
	{
		_remainingLifetime -= (float)delta;
		if (_remainingLifetime <= 0.0f)
		{
			ReturnToPoolOrFree();
		}
	}

	public virtual void OnSpawnedFromPool()
	{
		StartLifetime();
	}

	public virtual void OnDespawnedToPool()
	{
		StopParticles(this);
	}

	protected void ReturnToPoolOrFree()
	{
		if (ScenePool.IsTracked(this))
		{
			ScenePool.Despawn(this);
			return;
		}

		QueueFree();
	}

	private void StartLifetime()
	{
		_remainingLifetime = Lifetime;
		StartParticles(this);
	}

	private void StartParticles(Node node)
	{
		if (node is GpuParticles3D particles)
		{
			particles.Restart();
			particles.Emitting = true;
		}

		foreach (Node child in node.GetChildren())
		{
			StartParticles(child);
		}
	}

	private void StopParticles(Node node)
	{
		if (node is GpuParticles3D particles)
		{
			particles.Emitting = false;
		}

		foreach (Node child in node.GetChildren())
		{
			StopParticles(child);
		}
	}
}
