using Godot;

/// <summary>
/// Starts child particle systems and frees the effect after a short authored lifetime.
/// </summary>
public partial class TimedEffect : Node3D, IPooledNode
{
	[Export] public float Lifetime { get; set; } = 1.0f;

	private SceneTreeTimer _lifeTimer;
	private bool _isActive;
	private int _lifetimeVersion;

	public override void _Ready()
	{
		StartLifetime();
	}

	public virtual void OnSpawnedFromPool()
	{
		StartLifetime();
	}

	public virtual void OnDespawnedToPool()
	{
		_isActive = false;
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
		_isActive = true;
		StartParticles(this);
		int lifetimeVersion = ++_lifetimeVersion;
		_lifeTimer = GetTree().CreateTimer(Lifetime);
		_lifeTimer.Timeout += () => OnLifetimeExpired(lifetimeVersion);
	}

	private void OnLifetimeExpired(int lifetimeVersion)
	{
		if (!_isActive || lifetimeVersion != _lifetimeVersion)
		{
			return;
		}

		ReturnToPoolOrFree();
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
