using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>Samples enemy owners once and delivers focused flights, arcs or falling strikes.</summary>
public partial class TargetedSpell : Node, ICombatSpell
{
	[Export] public TargetedSpellDefinition Definition { get; set; }
	[Export] public PackedScene VisualScene { get; set; }
	[Export] public PackedScene DebrisScene { get; set; }
	[Export] public Node3D VisualRoot { get; set; }
	public string DisplayName => Definition?.SpellName ?? "";
	public int LevelCount => Definition?.LevelCount ?? 0;
	public DamageDeliveryKind? Delivery => Definition?.IsAreaTargeting == true ? DamageDeliveryKind.AreaImpact : DamageDeliveryKind.Projectile;
	public IEnumerable<string> DescribeLevel(in SpellPower power) => Definition?.DescribeLevel(power) ?? [];
	public bool CanCast(in SpellCastContext context) => GodotObject.IsInstanceValid(context.Caster)
		&& Definition?.IsValidLevel(context.Power.Level) == true && VisualScene != null && VisualRoot != null;
	private readonly AreaCastLifecycle _cast = new();
	private readonly List<PendingHit> _pending = [];
	private double _clock;
	private ulong _sequence;
	private sealed record PendingHit(Node3D Caster, Node3D Target, Vector3 Origin, Vector3 Position,
		World3D World, float Radius, AttackStats Damage, RandomNumberGenerator Random, AttackIdentity Identity, AttackIdentity Parent,
		double Due, bool IsAreaImpact = false);

	public override void _Ready() => SetPhysicsProcess(false);
	public override void _PhysicsProcess(double delta)
	{
		_clock += delta;
		while (_pending.Count > 0 && _pending[0].Due <= _clock)
		{
			var hit = _pending[0]; _pending.RemoveAt(0);
			if (!IsInstanceValid(hit.Caster) || !hit.Caster.IsInsideTree()) continue;

			var baseContact = new DamageContext
			{
				ExecutionId = hit.Identity,
				Parent = hit.Parent,
				SourceNode = this,
				OwnerNode = hit.Caster,
				Faction = HurtBoxComponent.GetDamageSource(hit.Caster),
				Random = hit.Random,
				// A focused strike can be dodged; an area strike only escaped.
				DefenseTraits = hit.IsAreaImpact ? AttackDefenseTraits.Guardable : AttackDefenseTraits.Dodgeable | AttackDefenseTraits.Guardable,
				DeliveryKind = hit.IsAreaImpact ? DamageDeliveryKind.AreaImpact : DamageDeliveryKind.Projectile,
				Element = Definition.Element
			};

			if (hit.IsAreaImpact)
			{
				var targets = AreaSpellTargets.Capture(this, hit.Caster, hit.Position, hit.Radius, hit.World);
				if (targets is { Length: > 0 })
				{
					Vector3 flat = hit.Position - hit.Origin; flat.Y = 0;
					Vector3 facing = flat.LengthSquared() >= .0001f ? flat.Normalized() : Vector3.Forward;
					AreaSpellTargets.Resolve(targets, hit.World, hit.Position, facing, baseContact, hit.Damage,
						() => IsInstanceValid(hit.Caster) && hit.Caster.IsInsideTree());
				}
			}
			else if (hit.Target != null && IsInstanceValid(hit.Target) && AreaSpellTargets.IsVisible(hit.Target, hit.World, hit.Origin)
				&& hit.Target.GlobalPosition.DistanceTo(hit.Position) <= hit.Radius)
			{
				Vector3 direction = hit.Position - hit.Origin; direction.Y = 0;
				var (amount, critical) = hit.Damage.Roll(hit.Random);
				var directed = baseContact with { IncomingDirection = direction.Normalized(), Amount = amount, Critical = critical };
				((ICombatDamageable)hit.Target).ResolveDamage(in directed);
			}
		}
		if (_pending.Count == 0) SetPhysicsProcess(false);
	}

	public bool Cast(in SpellCastContext context, RandomNumberGenerator random)
	{
		Node3D caster = context.Caster;
		SpellPower power = context.Power;
		int level = power.Level;
		if (!CanCast(in context) || !AreaCastLifecycle.TryCapture(this, in context, VisualRoot, random, Definition.Radius, out var cast)) return false;
		Node template = VisualScene.Instantiate();
		bool valid = Definition.Delivery switch
		{
			TargetDeliveryKind.Fireball => template is Projectile,
			TargetDeliveryKind.Traveling => template is TravelingEffectVisual { Timeline: not null, Core: not null, Shell: not null },
			TargetDeliveryKind.Chain => template is LightningArc { Timeline: not null },
			TargetDeliveryKind.Rockfall => template is EarthSpellVisual { Kind: EarthSpellVisual.EarthKind.Rockfall, Fall: not null, Timeline: not null },
			TargetDeliveryKind.Meteor => template is FireSpellVisual { Meteor.Fall: not null, Timeline: not null },
			_ => false
		};
		template?.Free();
		if (!valid) return false;
		int count = Definition.TargetsByLevel[level - 1];
		AttackStats hit = power.Hit.Scaled(Definition.DamageScaleByLevel[level - 1]);
		bool shower = Definition.IsAreaTargeting;
		List<Node3D> candidates = Definition.IsAreaTargeting
			? cast.Targets.Where(target => AreaSpellTargets.IsEligible(target, cast.World, cast.Origin)).ToList()
			: AreaSpellTargets.FilterForward(cast.Targets, cast.Origin, cast.Facing, Definition.AimingAngle, cast.World);

		if (Definition.IsAreaTargeting)
		{
			for (int i = candidates.Count - 1; i > 0; i--)
			{
				int j = random.RandiRange(0, i); (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
			}
		}

		var group = new SpellEffectGroup(); VisualRoot.AddChild(group); _cast.Track(group);
		var cosmetic = new RandomNumberGenerator { Seed = GetInstanceId() ^ ++_sequence * 7919UL };
		AreaVisibility visibility = AreaVisibility.Capture(cast.World, cast.Origin, Definition.Radius);
		Vector3 previous = cast.Origin + Vector3.Up;
		Node3D previousTarget = null;
		int candidateCount = candidates.Count;

		for (int i = 0; i < count; i++)
		{
			Node3D target = i < candidateCount ? candidates[i] : null;

			Vector3 position;
			Vector3 start;
			Vector3 end;

			if (target != null)
			{
				position = target.GlobalPosition;
				bool jump = Definition.Delivery == TargetDeliveryKind.Chain && i > 0 && !ReferenceEquals(target, previousTarget);
				start = jump ? previous : cast.Origin + Vector3.Up;
				end = position + Vector3.Up;
				if (jump && !WorldSight.IsClear(cast.World, start, end)) continue;
			}
			else
			{
				start = cast.Origin + Vector3.Up;
				if (Definition.IsAreaTargeting)
				{
					position = CosmeticPosition(cast, cosmetic);
					end = position + Vector3.Up;
				}
				else
				{
					Vector3 direction = cast.Facing;
					if (i > 0 || candidateCount > 0)
					{
						float halfSpread = Definition.SpreadAngle * 0.5f;
						float angleOffset = random.RandfRange(-halfSpread, halfSpread);
						direction = cast.Facing.Rotated(Vector3.Up, Mathf.DegToRad(angleOffset)).Normalized();
					}
					position = ForwardPosition(cast, direction);
					end = position + Vector3.Up;
				}
			}

			double delay = Spawn(group, VisualScene, level, start, end, i, visibility, target, caster, hit, random, parent: cast.Parent);
			previous = end; previousTarget = target;
			if (Definition.IsAreaTargeting || (target != null && Definition.Delivery != TargetDeliveryKind.Fireball))
				_pending.Add(new PendingHit(caster, target, cast.Origin, position, cast.World, Definition.HitRadius,
					hit, random, new AttackIdentity(caster.GetInstanceId(), ++_sequence), cast.Parent, _clock + delay, IsAreaImpact: Definition.IsAreaTargeting));
		}
		if (shower && DebrisScene != null)
			for (int i = 0; i < level * 2 + 2; i++)
				Spawn(group, DebrisScene, level, cast.Origin + Vector3.Up, CosmeticPosition(cast, cosmetic) + Vector3.Up,
					count + i, visibility, null, caster, default, random, true);
		_pending.Sort((a, b) => a.Due.CompareTo(b.Due));
		if (_pending.Count > 0) SetPhysicsProcess(true);
		return true;
	}

	private double Spawn(SpellEffectGroup group, PackedScene scene, int level, Vector3 start, Vector3 end, int index,
		AreaVisibility visibility, Node3D target, Node3D caster, AttackStats hit, RandomNumberGenerator random, bool debris = false,
		AttackIdentity parent = default)
	{
		Node node = scene.Instantiate();
		switch (node)
		{
			case Projectile projectile:
				group.AddChild(projectile); projectile.EffectRoot = group; projectile.Element = Definition.Element;
				projectile.RestrictToTarget(target, 1.1f + .35f * (level - 1));
				Vector3 displacement = end - start;
				projectile.Initialize(start, displacement.Normalized(), 13, displacement.Length() + .5f, hit,
					caster, randomSeed: target != null ? random.Randi() : GetInstanceId() ^ (ulong)(index + 1) * 7919UL, parentExecution: parent);
				FireballSpell.ConfigureProjectileVisual(projectile, level);
				return 0;
			case TravelingEffectVisual flight:
				flight.Configure(start, end, level, visibility); flight.ImpactGroundHeight = start.Y - 1;
				group.AddChild(flight);
				return Mathf.Max(.08f, start.DistanceTo(end) / Mathf.Max(flight.Speed, .01f));
			case LightningArc arc:
				arc.Configure(start, end, GetInstanceId() ^ (ulong)index * 7919UL); group.AddChild(arc); return 0;
			case EarthSpellVisual rock:
				rock.ConfigureLevel(level); rock.StartDelay = index * .11f; rock.Cosmetic = debris;
				rock.ShapeSeed = GetInstanceId() ^ (_sequence << 8) ^ (ulong)(index + 1) * 7919UL;
				if (debris) rock.Scale *= .55f;
				group.AddChild(rock); rock.GlobalPosition = end - Vector3.Up;
				return rock.StartDelay + rock.Fall.FlightTime;
			case FireSpellVisual meteor:
				meteor.ConfigureLevel(level); meteor.StartDelay = index * .08f; meteor.Cosmetic = debris;
				if (debris) meteor.Scale *= .55f;
				group.AddChild(meteor); meteor.GlobalPosition = end - Vector3.Up;
				meteor.SetVisibility(visibility);
				return meteor.StartDelay + meteor.Meteor.Fall.FlightTime;
			default:
				node?.Free(); return 0;
		}
	}

	/// <summary>
	/// The farthest clear point along <paramref name="direction"/> within the spell's radius, stopping short of a wall but never at the caster:
	/// a shot aimed at its own origin has no direction and would hang in place.
	/// </summary>
	private Vector3 ForwardPosition(AreaCastLifecycle.Cast cast, Vector3 direction)
	{
		float reach = WorldSight.ClearDistance(cast.World, cast.Origin + Vector3.Up, direction, Definition.Radius);
		return cast.Origin + direction * Mathf.Max(reach - .5f, 1);
	}

	/// <summary>A random clear spot for shower debris, else straight ahead.</summary>
	private Vector3 CosmeticPosition(AreaCastLifecycle.Cast cast, RandomNumberGenerator random)
	{
		for (int attempt = 0; attempt < 16; attempt++)
		{
			float angle = random.RandfRange(0, Mathf.Tau), distance = random.RandfRange(1, Definition.Radius);
			Vector3 point = cast.Origin + new Vector3(Mathf.Cos(angle) * distance, 0, Mathf.Sin(angle) * distance);
			if (WorldSight.IsClear(cast.World, cast.Origin + Vector3.Up, point + Vector3.Up)) return point;
		}
		return ForwardPosition(cast, cast.Facing);
	}
	public void Cancel()
	{
		_pending.Clear(); SetPhysicsProcess(false);
		var visuals = _cast.Cancel();
		foreach (var group in visuals)
			if (IsInstanceValid(group)) foreach (var projectile in group.GetChildren().OfType<Projectile>()) projectile.Cancel();
		AreaCastLifecycle.ClearVisuals(visuals);
	}
	public override void _ExitTree() => Cancel();
}
