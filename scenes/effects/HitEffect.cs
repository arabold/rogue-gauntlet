using Godot;

public partial class HitEffect : GpuParticles3D, IPooledNode
{
	private Timer _timer;
	private bool _isActive;

	public override void _Ready()
	{
		OneShot = true;
		_timer = GetNode<Timer>("Timer");
		_timer.Timeout += OnTimerExpired;
		StartEffect();
	}

	public void OnSpawnedFromPool()
	{
		StartEffect();
	}

	public void OnDespawnedToPool()
	{
		_isActive = false;
		Emitting = false;
		_timer?.Stop();
	}

	private void StartEffect()
	{
		_isActive = true;
		Restart();
		Emitting = true;
		_timer?.Start();
	}

	private void OnTimerExpired()
	{
		if (!_isActive)
		{
			return;
		}

		if (ScenePool.IsTracked(this))
		{
			ScenePool.Despawn(this);
			return;
		}

		QueueFree();
	}
}
