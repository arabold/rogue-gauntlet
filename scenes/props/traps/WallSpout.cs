using Godot;
using System;

/// <summary>
/// A wall-mounted trap that fires one projectile along its front (+Z) every <see cref="Period"/>: a dart slit, a
/// fire-breathing gargoyle. It warns for <see cref="WindUp"/> seconds before each shot (its <see cref="WindUpEffect"/>
/// and a lane on the floor) and fires only shots whose whole warning the hero could see: a spout woken mid-warning
/// skips that shot. The spout is in the <c>trap</c> group, so its shots carry the trap faction, which monsters ignore;
/// a monster's body still stops them.
/// </summary>
[GlobalClass]
public partial class WallSpout : Node3D, ITrapHazard
{
	private static readonly StringName TrapGroup = "trap";
	private static readonly StringName PositionKey = "position";
	/// <summary>What stops a shot: the world, walls and blocking props, as <see cref="Projectile.WorldCollisionMask"/> does.</summary>
	private const uint ShotBlockingMask = (1 << 0) | (1 << 1) | (1 << 4);
	private const float LaneHalfWidth = .6f;
	private const float LaneHeight = 2f;

	[Export] public PackedScene ProjectileScene { get; set; }
	/// <summary>Where shots leave, in front of the mounting wall; the spout's floor is its own origin.</summary>
	[Export] public Marker3D Muzzle { get; set; }
	/// <summary>Optional: draws the shot's lane on the floor during the warning.</summary>
	[Export] public AttackIndicatorComponent Indicator { get; set; }
	/// <summary>Optional: shown only during the warning (glow, embers, dust).</summary>
	[Export] public Node3D WindUpEffect { get; set; }
	[Export(PropertyHint.Range, "1,20,0.5,suffix:m/s")] public float Speed { get; set; } = 8f;
	/// <summary>Farthest a shot flies; walls and blocking props stop it sooner.</summary>
	[Export(PropertyHint.Range, "1,40,0.5,suffix:m")] public float Range { get; set; } = 16f;
	/// <summary>Seconds of warning before each shot; never shorter than an unblockable attack's minimum.</summary>
	[Export(PropertyHint.Range, "0.6,3,0.05,suffix:s")] public float WindUp { get; set; } = .6f;
	/// <summary>Damage on the first depth; the balance model derives it.</summary>
	[Export] public int Damage { get; set; } = 4;
	[Export] public float DamagePerDepth { get; set; }
	/// <summary>Elemental shots ignore armor; elemental wards and resistances absorb them.</summary>
	[Export] public DamageElement Element { get; set; }
	[Export] public AttackDefenseTraits DefenseTraits { get; set; } = AttackDefenseTraits.Dodgeable | AttackDefenseTraits.Guardable;
	[Export(PropertyHint.Range, "0.7,10,0.05,suffix:s")] public float Period { get; set; } = 2.4f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float Phase { get; set; }

	[Signal] public delegate void FiredEventHandler(Node3D shot);

	/// <summary>What a shot deals on its level's depth (the first outside a level).</summary>
	public int CurrentDamage => TrapDamage.At(Damage, DamagePerDepth, _level?.Depth ?? 1);
	public bool IsWindingUp { get; private set; }
	public int ShotsFired { get; private set; }

	/// <summary>The spout's front, level with the floor.</summary>
	public Vector3 FireDirection
	{
		get
		{
			Vector3 front = GlobalBasis.Z with { Y = 0 };
			return front.LengthSquared() > 1e-6f ? front.Normalized() : Vector3.Back;
		}
	}

	/// <summary>The lane from the muzzle to the first wall or blocking prop, floor to above head height.</summary>
	public Aabb DangerBounds
	{
		get
		{
			if (_laneLength < 0 && _valid && IsInsideTree()) _laneLength = MeasureLane();
			Vector3 from = Muzzle != null ? Muzzle.GlobalPosition : GlobalPosition;
			Vector3 to = from + FireDirection * (_laneLength >= 0 ? _laneLength : Range);
			var lane = new Aabb(from, Vector3.Zero).Expand(to);
			return new Aabb(new Vector3(lane.Position.X - LaneHalfWidth, GlobalPosition.Y, lane.Position.Z - LaneHalfWidth),
				new Vector3(lane.Size.X + 2 * LaneHalfWidth, LaneHeight, lane.Size.Z + 2 * LaneHalfWidth));
		}
	}

	private Level _level;
	private bool _valid;
	/// <summary>The cycle index seen on the last sync.</summary>
	private long _cycle = long.MinValue;
	/// <summary>The cycle whose whole warning the spout was awake for; only its shot may fire.</summary>
	private long _armedCycle = long.MinValue;
	private float _laneLength = -1;
	private AttackIdentity _shot;

	public override void _EnterTree() => _level = this.GetAncestorOrNull<Level>();

	public override void _Ready()
	{
		AddToGroup(TrapGroup);
		_valid = ProjectileScene != null && Muzzle != null && Period > WindUp;
		if (!_valid) GD.PushError($"{Name}: a wall spout needs a ProjectileScene, a Muzzle and a Period longer than its WindUp.");
		SetWindUpEffect(false);
	}

	public void Tune(HazardBeat beat)
	{
		Period = beat.Period;
		Phase = beat.Phase;
	}

	public void Sync(double clock, bool awake)
	{
		if (!_valid) return;
		double cycle = clock / Period + Phase;
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
			_laneLength = MeasureLane();
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
		int damage = CurrentDamage;
		var shot = ScenePool.Spawn<Projectile>(ProjectileScene, GetTree().CurrentScene ?? GetParent());
		shot.Element = Element;
		shot.Impact = new AttackImpact(damage, 0);
		ulong seed = GenerationRandom.DeriveSeed(_level?.Seed ?? RunState.DefaultSeed, "trap-shot", $"{GetPath()}:{ShotsFired}");
		shot.Initialize(Muzzle.GlobalPosition, FireDirection, Speed, Range, new AttackStats(damage, damage, 0), this,
			DefenseTraits, AttackDefenseTraits.None, seed, _shot);
		if (IsWindingUp) StopWarning(interrupted: false);
		EmitSignalFired(shot);
	}

	private AttackIndicatorFrame LaneFrame(AttackIndicatorStage stage, float progress)
	{
		Vector3 muzzle = Muzzle.GlobalPosition;
		return new AttackIndicatorFrame(_shot, stage, DefenseTraits.ThreatTier(), true,
			new Vector3(muzzle.X, GlobalPosition.Y, muzzle.Z), FireDirection, _laneLength, [], AimProgress: progress);
	}

	private float MeasureLane()
	{
		var space = GetWorld3D()?.DirectSpaceState;
		Vector3 from = Muzzle.GlobalPosition;
		if (space == null) return Range;
		var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + FireDirection * Range, ShotBlockingMask));
		return hit.Count > 0 ? from.DistanceTo(hit[PositionKey].AsVector3()) : Range;
	}

	private void SetWindUpEffect(bool shown)
	{
		if (WindUpEffect != null) WindUpEffect.Visible = shown;
	}
}
