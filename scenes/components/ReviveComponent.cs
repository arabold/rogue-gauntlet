using Godot;

/// <summary>
/// Lets an actor get back up after the blow that would kill it, as the stone golem crumbles into rubble and reassembles,
/// <see cref="Revives"/> times. The blow leaves it down with a sliver of health for <see cref="DownedDuration"/>: it
/// stops acting and moving, plays <see cref="CollapseClip"/> to its end and cannot be hurt, so nothing the hero does
/// skips the revive. Then it plays the clip backwards over <see cref="RiseDuration"/> and fights on with
/// <see cref="ReviveHealthShare"/> of its health. Once its revives are spent, the next lethal blow kills it through the
/// ordinary death, which alone pays its rewards and records it cleared.
/// </summary>
[GlobalClass]
public partial class ReviveComponent : Node, IDeathGuard
{
	private const float PoseBlend = 0.12f;

	[Export] public CharacterBody3D Actor { get; set; }
	/// <summary>The health whose lethal blows this component catches; it registers itself as that health's death guard.</summary>
	[Export] public HealthComponent Health { get; set; }
	/// <summary>The hurtbox that turns blows away while the actor is down.</summary>
	[Export] public HurtBoxComponent HurtBox { get; set; }
	[Export] public ActorActionGateComponent ActionGate { get; set; }
	[Export] public CombatAnimationComponent Animation { get; set; }
	/// <summary>The clip the actor collapses through, held at its end while down and played backwards to rise.</summary>
	[Export] public StringName CollapseClip { get; set; } = "Death_A";
	/// <summary>How many times it gets back up; the lethal blow after the last one kills it.</summary>
	[Export(PropertyHint.Range, "1,5,1")] public int Revives { get; set; } = 1;
	[Export(PropertyHint.Range, "0.5,20,0.1,suffix:s")] public float DownedDuration { get; set; } = 3;
	[Export(PropertyHint.Range, "0.2,5,0.05,suffix:s")] public float RiseDuration { get; set; } = 1.2f;
	[Export(PropertyHint.Range, "0.05,1,0.05")] public float ReviveHealthShare { get; set; } = 0.4f;

	private enum State { Standing, Down, Rising }
	private State _state;
	private double _elapsed;
	private double _clipLength;
	private int _revived;

	/// <summary>Down or rising: it cannot be hurt until it stands.</summary>
	public bool IsDowned => _state != State.Standing;
	/// <summary>Revives it has left; at zero the next lethal blow kills it.</summary>
	public int RevivesLeft => Revives - _revived;

	public override void _Ready()
	{
		SetPhysicsProcess(false);
		if (Actor == null || Health == null || HurtBox == null || ActionGate == null || Animation == null)
		{
			GD.PushError($"{Name}: revive requires explicitly assigned actor, health, hurtbox, action gate and combat animation.");
			return;
		}
		Health.DeathGuard = this;
	}

	public override void _ExitTree()
	{
		if (IsInstanceValid(Health) && Health.DeathGuard == this) Health.DeathGuard = null;
		if (IsDowned) SetDowned(false);
	}

	public bool TryCatchLethalBlow()
	{
		// Nothing that slips past the hurtbox, such as poison, kills it while it is down.
		if (IsDowned) return true;
		if (RevivesLeft <= 0 || !IsInsideTree() || !Animation.HasClip(CollapseClip)) return false;
		_revived++;
		// Whatever it was doing ends, even an uninterruptible attack, and nothing new starts until it rises.
		ActionGate.CancelCurrent(ActionInterruptReason.Damage);
		SetDowned(true);
		Animation.BeginPose(this, CollapseClip, PoseBlend);
		_clipLength = Animation.PoseLength(this);
		_state = State.Down;
		_elapsed = 0;
		SetPhysicsProcess(true);
		if (CombatTrace.Enabled) CombatTrace.Write(Actor, "actor.collapsed", ("downed_for", DownedDuration));
		return true;
	}

	public override void _PhysicsProcess(double delta)
	{
		double previous = _elapsed;
		_elapsed += delta;
		if (_state == State.Down)
		{
			// Play the collapse through; its end pose then holds by itself.
			if (previous < _clipLength) Animation.SamplePose(this, Mathf.Min(_elapsed, _clipLength));
			if (_elapsed < DownedDuration) return;
			_state = State.Rising;
			_elapsed = 0;
		}
		double progress = Mathf.Min(_elapsed / RiseDuration, 1);
		Animation.SamplePose(this, _clipLength * (1 - progress));
		if (progress >= 1) Rise();
	}

	/// <summary>Stand back up with the authored share of its health and take back control.</summary>
	private void Rise()
	{
		_state = State.Standing;
		SetPhysicsProcess(false);
		Animation.EndPose(this);
		SetDowned(false);
		Health.Heal(Health.MaxHealth * ReviveHealthShare - Health.CurrentHealth);
		if (CombatTrace.Enabled) CombatTrace.Write(Actor, "actor.revived", ("health", Health.CurrentHealth), ("revives_left", RevivesLeft));
	}

	/// <summary>Down, it neither acts, moves nor turns, and blows glance off it.</summary>
	private void SetDowned(bool downed)
	{
		ActionGate.SetStatusLock(this, downed);
		HurtBox.Invulnerable = downed;
	}
}
