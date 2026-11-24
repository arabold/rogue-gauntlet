using Godot;

/// <summary>
/// A one-shot curtain of dust sliding down a wall face: Tremor shakes it loose and the mines' rock lets it settle.
/// Its scene authors the burst as a box along local Z falling down local Y; <see cref="Play"/> turns local X out of the
/// wall. Spawn it through <see cref="ScenePool"/>; it returns there once its last grain has fallen.
/// </summary>
[GlobalClass]
public partial class WallDust : GpuParticles3D, IPooledNode
{
	/// <summary>How far in front of the wall's face the curtain falls.</summary>
	[Export(PropertyHint.Range, "0,2,0.05,suffix:m")] public float Standoff { get; set; } = .35f;
	/// <summary>Where on the wall the dust breaks loose.</summary>
	[Export(PropertyHint.Range, "0.5,4,0.05,suffix:m")] public float Height { get; set; } = 3.7f;

	public override void _Ready() => Finished += OnFinished;

	/// <summary>Restarts the burst on the wall face through <paramref name="faceFoot"/> (at floor level), facing <paramref name="outward"/>.</summary>
	public void Play(Vector3 faceFoot, Vector3 outward)
	{
		outward = outward.Horizontal().Normalized();
		GlobalTransform = new Transform3D(new Basis(outward, Vector3.Up, outward.Cross(Vector3.Up)),
			faceFoot + outward * Standoff + Vector3.Up * Height);
		ResetPhysicsInterpolation();
		Restart();
		Emitting = true;
	}

	private void OnFinished() => ScenePool.Despawn(this);

	public void OnSpawnedFromPool() { }

	public void OnDespawnedToPool() => Emitting = false;
}