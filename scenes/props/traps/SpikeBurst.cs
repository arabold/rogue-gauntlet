using Godot;

/// <summary>
/// Spikes that spring from their plate, the receiver of a spike trap. A pulse springs them once, hurting whoever set
/// them off, and lowers them again; active, they stay up and also hurt whoever steps into <see cref="Contact"/>.
/// </summary>
[Tool, GlobalClass]
public partial class SpikeBurst : Trap, ITriggerable
{
	private const float Lowered = -2f;

	[Export] public MeshInstance3D Spikes { get; set; }
	/// <summary>Optional: while the spikes are held up, whoever enters it is hurt too.</summary>
	[Export] public TriggerComponent Contact { get; set; }

	/// <summary>Up, or on the way up or down.</summary>
	public bool IsRaised { get; private set; }

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
		if (Contact != null) this.SubscribeUntilExit(Contact, c => c.Triggered += OnContact, c => c.Triggered -= OnContact);
	}

	public void Pulse(in TriggerActivation activation)
	{
		if (IsRaised) return;
		Hurt(activation.Activator);
		var spring = Move(0, .2f);
		spring.TweenProperty(Spikes, "position:y", Lowered, 1.5f).SetDelay(.5f);
		spring.Finished += () => IsRaised = false;
	}

	public void SetActive(bool active, in TriggerActivation activation)
	{
		if (active == _held) return;
		_held = active;
		if (!active)
		{
			Move(Lowered, 1.5f).Finished += () => IsRaised = false;
			return;
		}
		if (activation.Restored)
		{
			_tween?.Kill();
			Spikes.Position = Vector3.Zero;
			IsRaised = true;
			return;
		}
		Hurt(activation.Activator);
		Move(0, .2f);
	}

	/// <summary>Replaces any running motion with a move of the spikes to <paramref name="height"/>.</summary>
	private Tween Move(float height, float seconds)
	{
		IsRaised = true;
		_tween?.Kill();
		_tween = CreateTween();
		_tween.TweenProperty(Spikes, "position:y", height, seconds);
		return _tween;
	}

	private void OnContact(Node3D body)
	{
		if (_held) Hurt(body);
	}

	private void Hurt(Node3D body)
	{
		if (body is IDamageable damageable) damageable.TakeDamage(CurrentDamage, body.Basis.Z, this);
	}
}