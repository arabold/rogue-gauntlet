using Godot;

/// <summary>
/// Spikes that spring from their plate, the receiver of a spike trap. A pulse springs them once, hurting whoever set
/// them off, and lowers them again; active, they stay up and also hurt whoever steps into <see cref="Contact"/>. They
/// are in the trap group, so monsters ignore their hits.
/// </summary>
[GlobalClass]
public partial class SpikeBurst : Node3D, ITriggerable
{
	private static readonly StringName TrapGroup = "trap";
	private const float Lowered = -2f;

	/// <summary>Damage on the first depth; the balance model derives it (`simulate.py derive`).</summary>
	[Export] public int Damage { get; set; } = 5;
	/// <summary>Damage added per depth below the first, so spikes keep their share of the hero's growing health.</summary>
	[Export] public float DamagePerDepth { get; set; }
	[Export] public MeshInstance3D Spikes { get; set; }
	/// <summary>Optional: while the spikes are held up, whoever enters it is hurt too.</summary>
	[Export] public TriggerComponent Contact { get; set; }

	/// <summary>What a spring deals on its level's depth (the first outside a level), at least 1.</summary>
	public int CurrentDamage => TrapDamage.At(Damage, DamagePerDepth, _level?.Depth ?? 1);
	/// <summary>Up, or on the way up or down.</summary>
	public bool IsRaised { get; private set; }

	private Level _level;
	private bool _held;
	private Tween _tween;

	public override void _EnterTree()
	{
		_level = this.GetAncestorOrNull<Level>();
		AddToGroup(TrapGroup);
	}

	public override void _Ready()
	{
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