using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// A blade on a rod swinging from a pivot above the walls. The root stands on the floor; <see cref="Arm"/> hangs from the
/// pivot and turns about its local Z, so the blade sweeps along the root's X: across a path running along Z. Awake, it
/// cuts the hero at most once per swing and pushes them out of its path, back to the side they stood on, so one cut
/// never becomes several; a dodge passes through it and nothing blocks it.
/// Asleep, it holds still: its gauntlet wakes before the blade comes into view. The blade crosses the middle moving
/// toward +X at the start of each cycle.
/// </summary>
[Tool, GlobalClass]
public partial class PendulumBlade : TrapHazard
{
	/// <summary>Turns about its own origin, the pivot, around its local Z.</summary>
	[Export] public Node3D Arm { get; set; }
	/// <summary>The blade's cutting volume under <see cref="Arm"/>, straight below the pivot: a box overlap query on the
	/// hero's hurtbox layer, authored in the scene.</summary>
	[Export] public ShapeCast3D Edge { get; set; }
	/// <summary>How far the blade's middle travels to each side of the pivot at most; a room narrows it to the space it has.</summary>
	[Export(PropertyHint.Range, "0,4,0.05,suffix:m")] public float Sweep { get; set; } = 1.9f;
	/// <summary>How far from the blade's path a cut pushes its victim, whatever the victim's speed: past the blade's own
	/// depth and the hero's hurtbox (0.75 m), short of the next row a tile away. Every cut pushes, even one that only
	/// flinches.</summary>
	[Export(PropertyHint.Range, "0.5,3,0.05,suffix:m")] public float Clearance { get; set; } = 1.6f;

	/// <summary>Half the blade's width across its swing: how far it reaches past its middle.</summary>
	public float BladeHalfWidth => Edge?.Shape is BoxShape3D box ? box.Size.X / 2 : 0;

	/// <summary>The floor-to-blade box the blade sweeps between its extremes; the rod above it never cuts.</summary>
	public override Aabb DangerBounds => _danger ??= MeasureDanger();

	private readonly HashSet<ulong> _struck = [];
	private readonly RandomNumberGenerator _random = new();
	private bool _valid;
	private long _swing = long.MinValue;
	/// <summary>The swing's largest angle from straight down, in radians.</summary>
	private float _amplitude;
	private Aabb? _danger;

	/// <summary>A beat's <see cref="HazardBeat.Space"/> narrows the sweep so the blade stops short of what is beside it.</summary>
	public override void Tune(HazardBeat beat)
	{
		base.Tune(beat);
		if (beat.Space > 0) Sweep = Mathf.Clamp(beat.Space - BladeHalfWidth, 0, Sweep);
	}

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		_valid = Arm != null && Edge?.Shape is BoxShape3D;
		if (!_valid)
		{
			GD.PushError($"{Name}: a pendulum blade needs an Arm and an Edge with a box shape.");
			return;
		}
		_amplitude = Mathf.Asin(Mathf.Clamp(Sweep / Mathf.Max(Edge.Position.Length(), .01f), 0, .9f));
		_random.Seed = DeriveSeed("trap-blade");
	}

	public override void Sync(double clock, Node3D hero)
	{
		if (!_valid || hero == null) return;
		double cycle = CycleAt(clock);
		Arm.Rotation = new Vector3(0, 0, _amplitude * (float)Math.Sin(Math.Tau * cycle));
		// A swing runs from one extreme to the other; the extremes fall at a quarter and three quarters of the cycle.
		long swing = (long)Math.Floor(2 * cycle - .5);
		if (swing != _swing)
		{
			_swing = swing;
			_struck.Clear();
		}
		// Only a hero within reach of the swing can be cut; the body's width is the margin.
		if (DangerBounds.Grow(1).HasPoint(hero.GlobalPosition + Vector3.Up)) Cut(swing);
	}

	/// <summary>Straight out of the blade's path toward the side <paramref name="point"/> is on, and how far off the path
	/// it stands.</summary>
	private (Vector3 Away, float Offset) OffPath(Vector3 point)
	{
		Vector3 across = GlobalBasis.Z.Horizontal().Normalized();
		float offset = (point - GlobalPosition).Dot(across);
		return (offset < 0 ? -across : across, Mathf.Abs(offset));
	}

	private Aabb MeasureDanger()
	{
		if (!_valid) return new Aabb(GlobalPosition, Vector3.Zero);
		Vector3 half = ((BoxShape3D)Edge.Shape).Size / 2;
		float sweep = Sweep + half.X;
		float top = Arm.Position.Y - Edge.Position.Length() * Mathf.Cos(_amplitude) + half.Y;
		return GlobalTransform * new Aabb(new Vector3(-sweep, 0, -half.Z), new Vector3(2 * sweep, top, 2 * half.Z));
	}

	private void Cut(long swing)
	{
		Edge.ForceShapecastUpdate();
		int damage = CurrentDamage;
		for (int index = 0; index < Edge.GetCollisionCount(); index++)
		{
			if (Edge.GetCollider(index) is not HurtBoxComponent hurtBox || !_struck.Add(hurtBox.GetInstanceId())) continue;
			var (away, offset) = OffPath(hurtBox.GlobalPosition);
			float push = hurtBox.MovementComponent?.PushStrengthFor(Clearance - offset) ?? 0;
			var contact = new DamageContext
			{
				ExecutionId = new AttackIdentity(GetInstanceId(), unchecked((ulong)swing)),
				SourceNode = this,
				OwnerNode = this,
				Faction = DamageSourceFlags.Trap,
				Amount = damage,
				Random = _random,
				IncomingDirection = away,
				DefenseTraits = AttackDefenseTraits.Dodgeable,
				DeliveryKind = DamageDeliveryKind.Hazard,
				Impact = new AttackImpact(damage, push, ForcePushback: true),
			};
			hurtBox.ResolveDamage(in contact);
		}
	}
}
