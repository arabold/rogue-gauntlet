using Godot;

/// <summary>
/// A committed, collision-resolved displacement in phases: wind-up in place, travel along a heading, recovery. Like
/// <see cref="DodgeComponent"/>, it owns its action lease and poses and drives <see cref="MovementComponent"/> through a
/// locomotion override, so the mover stays a mover. Monster rushes and hop-backs use it; it grants no immunity,
/// pass-through or contact damage.
/// </summary>
/// <remarks>
/// Travel scales each frame by the part of the frame that lies inside the travel window, so the distance matches the
/// profile at any frame rate; the last travel slice still moves before the dash ends. A grounded collision that slows
/// travel below most of its speed stops the rest of the travel for this execution instead of extending it. The facing
/// can differ from the heading, so a hop-back keeps watching its target.
/// </remarks>
[GlobalClass]
public partial class DashComponent : Node
{
	/// <summary>Reports an accepted dash after its action and pose have started.</summary>
	[Signal] public delegate void DashStartedEventHandler(DashProfile profile);
	/// <summary>Clears pending presentation on completion, interruption or tree exit.</summary>
	[Signal] public delegate void DashEndedEventHandler();

	private const float PoseBlend = 0.08f;
	/// <summary>Travel slower than this share of the commanded speed counts as blocked.</summary>
	private const float BlockedSpeedShare = 0.85f;

	[Export] public CharacterBody3D Actor { get; set; }
	[Export] public MovementComponent MovementComponent { get; set; }
	[Export] public ActorActionGateComponent ActionGate { get; set; }
	[Export] public CombatAnimationComponent AnimationComponent { get; set; }

	private readonly AttackExecution _clock = new();
	private DashProfile _profile;
	private AttackPlayback _playback;
	private Vector3 _heading;
	private Vector3 _facing;
	private static readonly StringName NoClip = "";
	private StringName _clip = NoClip;
	private ActorActionLease _lease;
	private ulong _sequence;
	private float _commandedSpeed;
	private float _commandedFactor = 1;
	private bool _blocked;
	private bool _finishPending;

	public bool IsDashing => _clock.IsRunning;
	/// <summary>Identity of the running dash; default when idle.</summary>
	public AttackIdentity Execution { get; private set; }
	public bool IsRunning(AttackIdentity execution) => IsDashing && execution != default && execution == Execution;

	public override void _Ready()
	{
		ProcessPhysicsPriority = CombatPhysics.Defense;
		SetPhysicsProcess(false);
	}

	/// <summary>
	/// Start a dash along <paramref name="direction"/>, facing <paramref name="facing"/> (the heading when omitted).
	/// Rejected without side effects when busy, invalid, without a direction or without the bound animation; the action
	/// gate decides whether it may replace the current action.
	/// </summary>
	public ActorActionRequestResult TryDash(DashProfile profile, Vector3 direction, out AttackIdentity execution, Vector3? facing = null)
	{
		execution = default;
		if (IsDashing) return ActorActionRequestResult.Busy;
		if (profile?.IsValid != true) return ActorActionRequestResult.InvalidDefinition;
		if (!direction.TryNormalizeHorizontal(out var heading)) return ActorActionRequestResult.NoDirection;
		if (!GodotObject.IsInstanceValid(Actor) || !GodotObject.IsInstanceValid(MovementComponent)
			|| !GodotObject.IsInstanceValid(ActionGate) || !GodotObject.IsInstanceValid(AnimationComponent)
			|| !AnimationComponent.TryResolve(profile.Style, out var playback)) return ActorActionRequestResult.Unavailable;
		if (profile.RecoveryDuration > 0 && !AnimationComponent.HasClip(ActionGate.AnimationProfile?.RecoveryClip ?? NoClip))
			return ActorActionRequestResult.Unavailable;
		if (!_clock.TryStart(profile.WindUpDuration + profile.TravelDuration + profile.RecoveryDuration,
			profile.WindUpDuration, profile.WindUpDuration + profile.TravelDuration)) return ActorActionRequestResult.InvalidDefinition;
		ActorActionLease lease = ActorActionLease.None;
		var result = ActionGate.TryAcquire(ActorActionKind.Dash,
			profile.WindUpDuration > 0 ? ActorActionPhase.WindUp : ActorActionPhase.Active, _ => OnInterrupted(lease), out lease);
		if (result != ActorActionRequestResult.Success)
		{
			_clock.Finish();
			return result;
		}
		_profile = profile;
		_playback = playback;
		_lease = lease;
		_blocked = false;
		_finishPending = false;
		_commandedSpeed = 0;
		Execution = execution = new AttackIdentity(GetInstanceId(), ++_sequence);
		MovementComponent.SetInputDirection(Vector3.Zero);
		Aim(heading, facing);
		_clip = NoClip;
		if (!SamplePose())
		{
			End(interrupted: true, releaseGate: true);
			execution = default;
			return ActorActionRequestResult.Unavailable;
		}
		SetPhysicsProcess(true);
		if (CombatTrace.Enabled) CombatTrace.Write(this, "dash.started", ("execution", Execution.Sequence),
			("distance", profile.Distance), ("direction", CombatTrace.Vector(_heading)));
		EmitSignalDashStarted(profile);
		return ActorActionRequestResult.Success;
	}

	/// <summary>Re-aim during the wind-up only; travel keeps the heading and facing it started with.</summary>
	public bool UpdateDirection(AttackIdentity execution, Vector3 direction, Vector3? facing = null)
	{
		if (!IsRunning(execution) || _clock.Phase != ActorActionPhase.WindUp || !direction.TryNormalizeHorizontal(out var heading)) return false;
		Aim(heading, facing);
		return true;
	}

	/// <summary>Hold still along a new heading, facing <paramref name="facing"/> when it has a direction.</summary>
	private void Aim(Vector3 heading, Vector3? facing)
	{
		_heading = heading;
		_facing = facing is { } look && look.TryNormalizeHorizontal(out var lookHeading) ? lookHeading : heading;
		MovementComponent.SetLocomotionOverride(this, _heading, 0);
		MovementComponent.SetFixedFacing(this, _facing);
	}

	/// <summary>Stop the running dash and release its action.</summary>
	public void Cancel() => End(interrupted: true, releaseGate: true);

	public override void _ExitTree() => Cancel();

	public override void _PhysicsProcess(double delta)
	{
		if (!IsDashing) { SetPhysicsProcess(false); return; }
		if (_finishPending)
		{
			End(interrupted: false, releaseGate: true);
			return;
		}
		if (!ActionGate.IsLeaseCurrent(_lease) || ActionGate.Disabled || ActionGate.IsStatusLocked || ActionGate.IsStaggered
			|| MovementComponent.IsPushed)
		{
			End(interrupted: true, releaseGate: true);
			return;
		}
		DetectBlockedTravel();
		var step = _clock.Advance((float)delta);
		ActionGate.ReportPhase(_lease, step.Phase);
		float travelStart = _profile.WindUpDuration;
		float travelEnd = travelStart + _profile.TravelDuration;
		float travelTime = Mathf.Max(0, Mathf.Min(step.CurrentTime, travelEnd) - Mathf.Max(step.PreviousTime, travelStart));
		_commandedSpeed = _blocked || delta <= 0 ? 0 : _profile.TravelSpeed * Mathf.Min(travelTime, (float)delta) / (float)delta;
		_commandedFactor = MovementComponent.StatusSpeedFactor;
		MovementComponent.SetLocomotionOverride(this, _heading, _commandedSpeed);
		SamplePose();
		if (!step.Completed) return;
		// Let the mover apply this frame's last travel slice before releasing the override.
		if (_commandedSpeed > 0) _finishPending = true;
		else End(interrupted: false, releaseGate: true);
	}

	/// <summary>Latch a stop when last frame's grounded travel hit something and resolved well short of its commanded speed.</summary>
	private void DetectBlockedTravel()
	{
		if (_blocked || _commandedSpeed <= 0 || MovementComponent.LastMotionKind != MovementMotionKind.Override
			|| Actor.GetSlideCollisionCount() == 0 || !Actor.IsOnFloor()) return;
		float expected = _commandedSpeed * _commandedFactor;
		if (MovementComponent.GroundSpeed + 0.05f < expected * BlockedSpeedShare)
		{
			_blocked = true;
			if (CombatTrace.Enabled) CombatTrace.Write(this, "dash.blocked", ("execution", Execution.Sequence));
		}
	}

	/// <summary>Pose the wind-up, travel or recovery clip for the current clock time.</summary>
	private bool SamplePose()
	{
		float elapsed = _clock.Elapsed;
		StringName clip;
		double sample;
		if (elapsed < _profile.WindUpDuration)
		{
			bool hasWindUp = _playback.WindUpClipLength > 0;
			clip = hasWindUp ? _playback.WindUpClip : _playback.Clip;
			sample = hasWindUp ? elapsed / _profile.WindUpDuration * _playback.WindUpClipLength : 0;
		}
		else if (elapsed < _profile.WindUpDuration + _profile.TravelDuration)
		{
			clip = _playback.Clip;
			sample = (elapsed - _profile.WindUpDuration) / _profile.TravelDuration * _playback.ClipLength;
		}
		else if (_profile.RecoveryDuration > 0)
		{
			clip = ActionGate.AnimationProfile?.RecoveryClip ?? NoClip;
			sample = elapsed - _profile.WindUpDuration - _profile.TravelDuration;
		}
		else
		{
			clip = _playback.Clip;
			sample = _playback.ClipLength;
		}
		if (_clip != clip)
		{
			if (!AnimationComponent.BeginPose(this, clip, PoseBlend)) return false;
			_clip = clip;
		}
		AnimationComponent.SamplePose(this, sample);
		return true;
	}

	private void OnInterrupted(ActorActionLease lease)
	{
		if (lease == _lease) End(interrupted: true, releaseGate: false);
	}

	private void End(bool interrupted, bool releaseGate)
	{
		if (!IsDashing) return;
		var lease = _lease;
		var execution = Execution;
		_clock.Finish();
		_profile = null;
		_playback = null;
		_lease = ActorActionLease.None;
		_commandedSpeed = 0;
		_finishPending = false;
		_clip = NoClip;
		Execution = default;
		if (GodotObject.IsInstanceValid(AnimationComponent)) AnimationComponent.EndPose(this);
		if (GodotObject.IsInstanceValid(MovementComponent)) MovementComponent.ClearLocomotionOverride(this);
		if (releaseGate && GodotObject.IsInstanceValid(ActionGate) && ActionGate.IsLeaseCurrent(lease)) ActionGate.Release(lease);
		SetPhysicsProcess(false);
		if (CombatTrace.Enabled) CombatTrace.Write(this, "dash.finished", ("execution", execution.Sequence), ("interrupted", interrupted));
		EmitSignalDashEnded();
	}
}