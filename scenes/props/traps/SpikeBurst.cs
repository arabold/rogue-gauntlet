using Godot;

/// <summary>
/// Spikes that spring from their plate, the receiver of a spike trap. A pulse springs them once, hurting whoever set
/// them off, and lowers them again; active, they spring the same way and stay up until turned off.
/// </summary>
[Tool, GlobalClass]
public partial class SpikeBurst : Trap, ITriggerable
{
	/// <summary>Where the spikes wait: their tips just under the floor plane, below every floor tile, and none show.</summary>
	private const float Lowered = -2.1f;

	[Export] public MeshInstance3D Spikes { get; set; }

	/// <summary>Up, or on the way up or down.</summary>
	public bool IsRaised => _held || _tween?.IsRunning() == true;

	private bool _held;
	private Tween _tween;

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		if (Spikes == null)
		{
			GD.PushError($"{GetPath()}: assign the spikes mesh.");
			return;
		}
		Spikes.Position = Vector3.Up * Lowered;
	}

	public void Pulse(in TriggerActivation activation)
	{
		if (IsRaised) return;
		Hurt(activation.Activator);
		Move(0, .2f).TweenProperty(Spikes, "position:y", Lowered, 1.5f).SetDelay(.5f);
	}

	public void SetActive(bool active, in TriggerActivation activation)
	{
		if (active == _held) return;
		_held = active;
		if (active) Hurt(activation.Activator);
		Move(active ? 0 : Lowered, active ? .2f : 1.5f);
	}

	/// <summary>Replaces any running motion with a move of the spikes to <paramref name="height"/>.</summary>
	private Tween Move(float height, float seconds)
	{
		_tween?.Kill();
		_tween = CreateTween();
		_tween.TweenProperty(Spikes, "position:y", height, seconds);
		return _tween;
	}

	private void Hurt(Node3D body)
	{
		if (body is IDamageable damageable) damageable.TakeDamage(CurrentDamage, body.Basis.Z, this);
	}
}