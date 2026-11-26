using Godot;
using System;

/// <summary>
/// A wall-mounted trap that fires one projectile through its <see cref="Shot"/> every <see cref="TrapHazard.Period"/>:
/// a dart slit, a fire-breathing gargoyle. It warns for <see cref="WindUp"/> seconds before each shot (its
/// <see cref="WindUpEffect"/> and a lane on the floor) and fires only shots whose whole warning the hero could see: a
/// spout woken mid-warning skips that shot. Monsters ignore its shots, but a monster's body still stops them.
/// </summary>
[Tool, GlobalClass]
public partial class WallSpout : TrapHazard
{
	private const float LaneHalfWidth = .6f;
	private const float LaneHeight = 2f;

	/// <summary>The projectile and how it leaves the wall; the spout's floor is its own origin.</summary>
	[Export] public TrapShotComponent Shot { get; set; }
	/// <summary>Optional: draws the shot's lane on the floor during the warning.</summary>
	[Export] public AttackIndicatorComponent Indicator { get; set; }
	/// <summary>Optional: shown only during the warning (glow, embers, dust).</summary>
	[Export] public Node3D WindUpEffect { get; set; }
	/// <summary>Seconds of warning before each shot; never shorter than an unblockable attack's minimum.</summary>
	[Export(PropertyHint.Range, "0.6,3,0.05,suffix:s")] public float WindUp { get; set; } = .6f;

	[Signal] public delegate void FiredEventHandler(Node3D shot);

	public bool IsWindingUp { get; private set; }
	public int ShotsFired { get; private set; }

	/// <summary>The lane from the muzzle to the first wall or blocking prop, floor to above head height.</summary>
	public override Aabb DangerBounds
	{
		get
		{
			if (!_valid) return new Aabb(GlobalPosition, Vector3.Zero);
			if (_laneLength < 0 && IsInsideTree()) _laneLength = Shot.MeasureLane();
			Vector3 from = Shot.Muzzle.GlobalPosition;
			Vector3 to = from + Shot.FireDirection * (_laneLength >= 0 ? _laneLength : Shot.Range);
			var lane = new Aabb(from, Vector3.Zero).Expand(to);
			return new Aabb(new Vector3(lane.Position.X - LaneHalfWidth, GlobalPosition.Y, lane.Position.Z - LaneHalfWidth),
				new Vector3(lane.Size.X + 2 * LaneHalfWidth, LaneHeight, lane.Size.Z + 2 * LaneHalfWidth));
		}
	}

	private bool _valid;
	/// <summary>The cycle index seen on the last sync.</summary>
	private long _cycle = long.MinValue;
	/// <summary>The cycle whose whole warning the spout was awake for; only its shot may fire.</summary>
	private long _armedCycle = long.MinValue;
	private float _laneLength = -1;
	private AttackIdentity _shot;

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		_valid = Shot is { IsValid: true } && Period > WindUp && WindUp >= AttackThreatTier.Unblockable.MinimumWindUp();
		if (!_valid) GD.PushError($"{Name}: a wall spout needs a valid Shot, a WindUp no shorter than an unblockable "
			+ "attack's and a Period longer than its WindUp.");
		SetWindUpEffect(false);
	}

	/// <summary>Asleep spouts still track their cycle, so a spout woken mid-warning knows to skip that shot.</summary>
	public override void Sync(double clock, Node3D hero)
	{
		if (!_valid) return;
		bool awake = hero != null;
		double cycle = CycleAt(clock);
		long index = (long)Math.Floor(cycle);
		float untilShot = (float)((index + 1 - cycle) * Period);
		if (index > _cycle && _cycle != long.MinValue && awake && _armedCycle == _cycle) Fire();
		_cycle = index;
		if (awake && untilShot > WindUp) _armedCycle = index;
		bool winding = awake && _armedCycle == index && untilShot <= WindUp;
		if (winding) WarnFor(1f - untilShot / WindUp);
		else if (IsWindingUp) StopWarning(interrupted: true);
	}

	private void WarnFor(float progress)
	{
		if (!IsWindingUp)
		{
			IsWindingUp = true;
			_laneLength = Shot.MeasureLane();
			_shot = new AttackIdentity(GetInstanceId(), (ulong)ShotsFired + 1);
			SetWindUpEffect(true);
		}
		Indicator?.Present(LaneFrame(AttackIndicatorStage.Aiming, progress));
	}

	private void StopWarning(bool interrupted)
	{
		IsWindingUp = false;
		SetWindUpEffect(false);
		if (!interrupted) Indicator?.Present(LaneFrame(AttackIndicatorStage.Released, 1));
		Indicator?.End(_shot, interrupted);
	}

	private void Fire()
	{
		ShotsFired++;
		var shot = Shot.Fire(this, CurrentDamage, DeriveSeed("trap-shot", $":{ShotsFired}"), _shot);
		if (IsWindingUp) StopWarning(interrupted: false);
		EmitSignalFired(shot);
	}

	private AttackIndicatorFrame LaneFrame(AttackIndicatorStage stage, float progress)
	{
		Vector3 muzzle = Shot.Muzzle.GlobalPosition;
		return new AttackIndicatorFrame(_shot, stage, Shot.DefenseTraits.ThreatTier(), true,
			new Vector3(muzzle.X, GlobalPosition.Y, muzzle.Z), Shot.FireDirection, _laneLength, [], AimProgress: progress);
	}

	private void SetWindUpEffect(bool shown)
	{
		if (WindUpEffect != null) WindUpEffect.Visible = shown;
	}
}
