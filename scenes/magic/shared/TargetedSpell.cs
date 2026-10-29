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
	private readonly AreaCastLifecycle _cast = new();
	private readonly List<PendingHit> _pending = [];
	private double _clock;
	private ulong _sequence;
	private sealed record PendingHit(Node3D Caster, Node3D Target, Vector3 Origin, Vector3 Position,
		World3D World, float Radius, float Damage, RandomNumberGenerator Random, AttackIdentity Identity, double Due);

	public override void _Ready() => SetPhysicsProcess(false);
	public override void _PhysicsProcess(double delta)
	{
		_clock += delta;
		while (_pending.Count > 0 && _pending[0].Due <= _clock)
		{
			var hit = _pending[0]; _pending.RemoveAt(0);
			if (!IsInstanceValid(hit.Caster) || !hit.Caster.IsInsideTree() || !AreaSpellTargets.IsVisible(hit.Target, hit.World, hit.Origin)
				|| hit.Target.GlobalPosition.DistanceTo(hit.Position) > hit.Radius) continue;
			Vector3 direction = hit.Position - hit.Origin; direction.Y = 0;
			var contact = new DamageContext { ExecutionId = hit.Identity, SourceNode = this, OwnerNode = hit.Caster,
				Faction = HurtBoxComponent.GetDamageSource(hit.Caster), Amount = hit.Damage, Random = hit.Random,
				IncomingDirection = direction.Normalized(), DefenseTraits = AttackDefenseTraits.Dodgeable | AttackDefenseTraits.Guardable,
				DeliveryKind = Definition.Delivery is TargetDeliveryKind.Rockfall or TargetDeliveryKind.Meteor ? DamageDeliveryKind.AreaImpact : DamageDeliveryKind.Projectile,
				Element = Definition.Element };
			((ICombatDamageable)hit.Target).ResolveDamage(in contact);
		}
		if (_pending.Count == 0) SetPhysicsProcess(false);
	}

	public bool Cast(Node3D caster, int level, RandomNumberGenerator random)
	{
		if (Definition?.IsValidLevel(level) != true || VisualScene == null
			|| !AreaCastLifecycle.TryCapture(this, caster, VisualRoot, random, Definition.Radius, out var cast)) return false;
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
		bool shower = Definition.Delivery is TargetDeliveryKind.Rockfall or TargetDeliveryKind.Meteor;
		var candidates = cast.Targets.Where(target => target is not IShieldOwner { ShieldHurtBox.HealthComponent.IsDead: true }
			&& AreaSpellTargets.IsVisible(target, cast.World, cast.Origin)).ToList();
		for (int i = candidates.Count - 1; i > 0; i--)
		{
			int j = random.RandiRange(0, i); (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
		}
		var group = new SpellEffectGroup(); VisualRoot.AddChild(group); _cast.Track(group);
		var cosmetic = new RandomNumberGenerator { Seed = GetInstanceId() ^ ++_sequence * 7919UL };
		AreaVisibility visibility = AreaVisibility.Capture(cast.World, cast.Origin, Definition.Radius);
		Vector3 previous = cast.Origin + Vector3.Up;
		for (int i = 0; i < count; i++)
		{
			Node3D target = candidates.Count > 0 && (shower || i < candidates.Count) ? candidates[i % candidates.Count] : null;
			Vector3 position = target?.GlobalPosition ?? CosmeticPosition(cast, cosmetic);
			Vector3 start = Definition.Delivery == TargetDeliveryKind.Chain ? previous : cast.Origin + Vector3.Up;
			Vector3 end = position + Vector3.Up;
			if (Definition.Delivery == TargetDeliveryKind.Chain && !WorldSight.IsClear(cast.World, start, end)) continue;
			double delay = Spawn(group, VisualScene, level, start, end, i, visibility, target, caster, Definition.DamageByLevel[level - 1], random);
			if (Definition.Delivery == TargetDeliveryKind.Chain) previous = end;
			if (target != null && Definition.Delivery != TargetDeliveryKind.Fireball)
				_pending.Add(new PendingHit(caster, target, cast.Origin, position, cast.World, Definition.HitRadius,
					Definition.DamageByLevel[level - 1], random, new AttackIdentity(caster.GetInstanceId(), ++_sequence), _clock + delay));
		}
		if (shower && DebrisScene != null)
			for (int i = 0; i < level * 2 + 2; i++)
				Spawn(group, DebrisScene, level, cast.Origin + Vector3.Up, CosmeticPosition(cast, cosmetic) + Vector3.Up,
					count + i, visibility, null, caster, 0, random, true);
		_pending.Sort((a, b) => a.Due.CompareTo(b.Due));
		if (_pending.Count > 0) SetPhysicsProcess(true);
		return true;
	}

	private double Spawn(SpellEffectGroup group, PackedScene scene, int level, Vector3 start, Vector3 end, int index,
		AreaVisibility visibility, Node3D target, Node3D caster, float damage, RandomNumberGenerator random, bool debris = false)
	{
		Node node = scene.Instantiate();
		switch (node)
		{
			case Projectile projectile:
				group.AddChild(projectile); projectile.EffectRoot = group; projectile.Element = Definition.Element;
				projectile.RestrictToTarget(target, 1.1f + .35f * (level - 1));
				Vector3 displacement = end - start;
				projectile.Initialize(start, displacement.Normalized(), 13, displacement.Length() + .5f, damage, damage, 0,
					caster, randomSeed: target != null ? random.Randi() : GetInstanceId() ^ (ulong)(index + 1) * 7919UL);
				FireballSpell.ConfigureProjectileVisual(projectile, new[] { .58f, 1.15f, 1.8f }[level - 1]);
				return 0;
			case TravelingEffectVisual flight:
				flight.Configure(start, end, level, visibility); flight.ImpactGroundHeight = start.Y - 1;
				group.AddChild(flight);
				return Mathf.Max(.08f, start.DistanceTo(end) / Mathf.Max(flight.Speed, .01f));
			case LightningArc arc:
				arc.Configure(start, end, GetInstanceId() ^ (ulong)index * 7919UL); group.AddChild(arc); return 0;
			case EarthSpellVisual rock:
				rock.ConfigureLevel(level); rock.StartDelay = index * .11f; rock.Cosmetic = debris;
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

	private Vector3 CosmeticPosition(AreaCastLifecycle.Cast cast, RandomNumberGenerator random)
	{
		for (int attempt = 0; attempt < 16; attempt++)
		{
			float angle = random.RandfRange(0, Mathf.Tau), distance = random.RandfRange(1, Definition.Radius);
			Vector3 point = cast.Origin + new Vector3(Mathf.Cos(angle) * distance, 0, Mathf.Sin(angle) * distance);
			if (WorldSight.IsClear(cast.World, cast.Origin + Vector3.Up, point + Vector3.Up)) return point;
		}
		return cast.Origin;
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
