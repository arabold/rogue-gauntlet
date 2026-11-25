using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// A blade on a rod swinging from a pivot above the walls. The root stands on the floor; <see cref="Arm"/> hangs from the
/// pivot and turns about its local Z, so the blade sweeps along the root's X: across a path running along Z. The blade
/// keeps swinging while its gauntlet sleeps but cuts only while it is awake, each actor at most once per swing, pushing
/// them along the swing. A dodge passes through it; nothing blocks it. The blade crosses the middle moving toward +X at
/// the start of each cycle. A tool script, because room steps read its blade in the editor too; it acts only in a game.
/// </summary>
[Tool, GlobalClass]
public partial class PendulumBlade : Node3D, ITrapHazard
{
	private static readonly StringName TrapGroup = "trap";
	/// <summary>Player and enemy hurtbox layers. Monsters ignore the trap faction, as they ignore floor traps.</summary>
	private const uint HurtBoxLayers = (1 << 2) | (1 << 3);

	/// <summary>Turns about its own origin, the pivot, around its local Z.</summary>
	[Export] public Node3D Arm { get; set; }
	/// <summary>The blade's cutting volume under <see cref="Arm"/>, straight below the pivot: a box overlap query.</summary>
	[Export] public ShapeCast3D Edge { get; set; }
	/// <summary>How far the blade's middle travels to each side of the pivot; rooms fit it so the blade never enters a wall.</summary>
	[Export(PropertyHint.Range, "0.1,4,0.05,suffix:m")] public float Sweep { get; set; } = 1.9f;
	/// <summary>Damage on the first depth; the balance model derives it.</summary>
	[Export] public int Damage { get; set; } = 4;
	[Export] public float DamagePerDepth { get; set; }
	/// <summary>Push along the swing on every cut, even one that only flinches.</summary>
	[Export(PropertyHint.Range, "0,12,0.5")] public float Knockback { get; set; } = 4f;
	[Export(PropertyHint.Range, "0.7,10,0.05,suffix:s")] public float Period { get; set; } = 2f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float Phase { get; set; }

	/// <summary>Half the blade's width across its swing: how far it reaches past its middle.</summary>
	public float BladeHalfWidth => Edge?.Shape is BoxShape3D box ? box.Size.X / 2 : 0;

	/// <summary>What a cut deals on its level's depth (the first outside a level).</summary>
	public int CurrentDamage => TrapDamage.At(Damage, DamagePerDepth, _level?.Depth ?? 1);

	/// <summary>The floor-to-blade box the blade sweeps between its extremes; the rod above it never cuts.</summary>
	public Aabb DangerBounds
	{
		get
		{
			if (!_valid || Edge.Shape is not BoxShape3D box) return new Aabb(GlobalPosition, Vector3.Zero);
			Vector3 half = box.Size / 2;
			float reach = Edge.Position.Length();
			float sweep = Sweep + half.X;
			float top = Arm.Position.Y - reach * Mathf.Cos(Amplitude) + half.Y;
			return GlobalTransform * new Aabb(new Vector3(-sweep, 0, -half.Z), new Vector3(2 * sweep, top, 2 * half.Z));
		}
	}

	private readonly HashSet<ulong> _struck = [];
	private readonly RandomNumberGenerator _random = new();
	private Level _level;
	private bool _valid;
	private long _swing = long.MinValue;

	public override void _EnterTree()
	{
		if (Engine.IsEditorHint()) return;
		_level = this.GetAncestorOrNull<Level>();
	}

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		AddToGroup(TrapGroup);
		_valid = Arm != null && Edge != null && Edge.Shape is BoxShape3D;
		if (!_valid)
		{
			GD.PushError($"{Name}: a pendulum blade needs an Arm and an Edge with a box shape.");
			return;
		}
		Edge.Enabled = false;
		Edge.TargetPosition = Vector3.Zero;
		Edge.CollideWithAreas = true;
		Edge.CollideWithBodies = false;
		Edge.CollisionMask = HurtBoxLayers;
		_random.Seed = GenerationRandom.DeriveSeed(_level?.Seed ?? RunState.DefaultSeed, "trap-blade", GetPath().ToString());
	}

	/// <summary>The swing's largest angle from straight down, in radians, for <see cref="Sweep"/> at the blade's reach.</summary>
	private float Amplitude => Mathf.Asin(Mathf.Clamp(Sweep / Mathf.Max(Edge.Position.Length(), .01f), 0, .9f));

	public void Tune(HazardBeat beat)
	{
		Period = beat.Period;
		Phase = beat.Phase;
		if (beat.Sweep > 0) Sweep = beat.Sweep;
	}

	public void Sync(double clock, bool awake)
	{
		if (!_valid) return;
		double cycle = clock / Period + Phase;
		Arm.Rotation = new Vector3(0, 0, Amplitude * (float)Math.Sin(Math.Tau * cycle));
		if (!awake) return;
		// A swing runs from one extreme to the other; the extremes fall at a quarter and three quarters of the cycle.
		long swing = (long)Math.Floor(2 * cycle - .5);
		if (swing != _swing)
		{
			_swing = swing;
			_struck.Clear();
		}
		Vector3 travel = GlobalBasis.X * Math.Sign(Math.Cos(Math.Tau * cycle));
		Cut(swing, travel with { Y = 0 });
	}

	private void Cut(long swing, Vector3 travel)
	{
		Edge.ForceShapecastUpdate();
		int damage = CurrentDamage;
		for (int index = 0; index < Edge.GetCollisionCount(); index++)
		{
			if (Edge.GetCollider(index) is not HurtBoxComponent hurtBox || !_struck.Add(hurtBox.GetInstanceId())) continue;
			var contact = new DamageContext
			{
				ExecutionId = new AttackIdentity(GetInstanceId(), unchecked((ulong)swing)),
				SourceNode = this,
				OwnerNode = this,
				Faction = DamageSourceFlags.Trap,
				Amount = damage,
				Random = _random,
				IncomingDirection = travel.Normalized(),
				DefenseTraits = AttackDefenseTraits.Dodgeable,
				DeliveryKind = DamageDeliveryKind.Hazard,
				Impact = new AttackImpact(damage, Knockback, ForcePushback: true),
			};
			hurtBox.ResolveDamage(in contact);
		}
	}
}
