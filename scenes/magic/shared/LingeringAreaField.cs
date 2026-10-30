using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Resolves a lingering field's contacts: moving flame ribbons touch each enemy once; a hailstorm slows everyone
/// inside it and drops evenly spread shards, each hitting whoever stands where it lands; an ice wall stays where
/// it was cast and, once its crystals stand, hits and shoves back out every enemy inside its ring, again while one
/// keeps pushing in. A field only reaches as far as its visual's front has travelled.
/// Each mode runs its cheap checks before the line-of-sight test.
/// </summary>
public partial class LingeringAreaField : Node3D
{
	private LingeringAreaSpell _source;
	private AreaCastLifecycle.Cast _cast;
	private AreaFront _front;
	private LingeringAreaDefinition _definition;
	private Node3D _visual;
	private int _level;
	private AttackStats _hit;
	private float _burnDamage;
	private readonly HashSet<Node3D> _contacted = [];
	private readonly HashSet<BuffController> _slowed = [];
	private readonly HashSet<BuffController> _present = [];
	private readonly Queue<(Vector3 Point, double Due)> _shards = new();
	private readonly Dictionary<Node3D, double> _repelled = [];
	private Func<bool> _isActive;
	private HailPattern _hail;
	private Vector3[] _previousHeads;
	private ulong _sequence;
	private double _clock;
	private bool _ended;

	public void Configure(LingeringAreaSpell source, AreaCastLifecycle.Cast cast, LingeringAreaDefinition definition, in SpellPower power, VisualEffectInstance visual)
	{
		int level = power.Level;
		_source = source; _cast = cast; _definition = definition; _level = level; _visual = visual; _front = visual.Front;
		_hit = power.Hit.Scaled(definition.DamageScaleByLevel[level - 1]);
		// Burns tick small amounts often, so they take the average hit and never roll a critical.
		_burnDamage = definition.Kind == LingeringAreaKind.SolarFlares ? power.Hit.Average * definition.BurnScaleByLevel[level - 1] : 0;
		_isActive = () => !_ended;
		if (definition.Kind == LingeringAreaKind.Hailstorm) _hail = new HailPattern(cast.Random, definition.ShardsPerSecondByLevel[level - 1]);
	}
	public override void _PhysicsProcess(double delta)
	{
		if (_ended || !IsInstanceValid(_source) || !_source.IsInsideTree() || !IsInstanceValid(_cast.Caster) || !_cast.Caster.IsInsideTree()
			|| !IsInstanceValid(_visual) || _visual.IsQueuedForDeletion()) { End(); return; }
		_clock += delta;
		float radius = _definition.RadiusByLevel[_level - 1], reach = _front.Reach((float)_clock);
		if (reach <= 0) return;
		var targets = AreaSpellTargets.Capture(_source, _cast.Caster, _cast.Origin, radius * reach, _cast.World) ?? [];
		switch (_definition.Kind)
		{
			case LingeringAreaKind.Hailstorm: Hail(targets, (float)delta, radius); break;
			case LingeringAreaKind.IceWall: Repel(targets); break;
			default: Sweep(targets); break;
		}
	}
	private void Sweep(Node3D[] targets)
	{
		Vector3[] heads = _visual is FireSpellVisual fire ? fire.Sweep.Heads.Take(_level).Select(head => head.GlobalPosition).ToArray() : [];
		_previousHeads ??= heads;
		var touched = targets.Where(target => !_contacted.Contains(target) && TouchesFlame(target.GlobalPosition, heads)).ToArray();
		if (touched.Length > 0) Resolve(touched, _cast.Origin, (target, result) =>
		{
			_contacted.Add(target);
			if (result.Outcome == DamageOutcome.Damaged && IsInstanceValid(target) && target.IsInsideTree()) _source.Ignite(_cast.Caster, target, _cast.Random, _burnDamage);
		});
		_previousHeads = heads;
	}
	private void Hail(Node3D[] targets, float delta, float radius)
	{
		if (_visual is WaterSpellVisual { Hailstorm: { IsFalling: true } storm })
			for (int count = _hail.Advance(delta); count > 0; count--)
			{
				Vector3 point = _hail.Next(_cast.Origin, radius);
				if (!WorldSight.IsClear(_cast.World, _cast.Origin + Vector3.Up, point + Vector3.Up)) continue;
				storm.DropShard(point); _shards.Enqueue((point, _clock + storm.FlightTime));
			}
		while (_shards.Count > 0 && _shards.Peek().Due <= _clock)
		{
			Vector3 point = _shards.Dequeue().Point;
			var struck = targets.Where(target => new Vector2(target.GlobalPosition.X - point.X, target.GlobalPosition.Z - point.Z).Length() <= _definition.ShardHitRadius).ToArray();
			if (struck.Length > 0) Resolve(struck, point);
			if (_ended) return;
		}
		_present.Clear();
		foreach (var target in targets)
		{
			if (target is not IStatusActor { BuffController: { } statuses } || target is not ISlowActor { SlowStatus: not null }
				|| statuses.Health?.IsDead == true || !AreaSpellTargets.IsVisible(target, _cast.World, _cast.Origin)) continue;
			if (!statuses.HasStatusFrom(this))
			{
				_slowed.Add(statuses);
				if (!statuses.SetPersistentStatus(_definition.SlowConditions[_level - 1], this, true)) _slowed.Remove(statuses);
				if (_ended) return;
			}
			if (_slowed.Contains(statuses)) _present.Add(statuses);
		}
		if (_slowed.Count == _present.Count) return;
		foreach (var statuses in _slowed.Where(value => !_present.Contains(value)).ToArray())
		{
			_slowed.Remove(statuses); if (IsInstanceValid(statuses)) statuses.RemoveStatuses(this);
		}
	}
	private void Repel(Node3D[] targets)
	{
		var due = targets.Where(target => !_repelled.TryGetValue(target, out double next) || _clock >= next).ToArray();
		if (due.Length > 0) Resolve(due, _cast.Origin, (target, _) => _repelled[target] = _clock + _definition.RepelSeconds);
	}
	/// <summary>
	/// Hits each visible target from <paramref name="origin"/> with one field contact. Hail lands many small hits,
	/// so its shards bypass armor (whose per-hit roll would swallow them) and never interrupt or stagger; the ice
	/// wall shoves whoever it hits back out of its ring.
	/// </summary>
	private void Resolve(Node3D[] targets, Vector3 origin, Action<Node3D, DamageResolution> afterContact = null)
	{
		bool hail = _definition.Kind == LingeringAreaKind.Hailstorm;
		var contact = new DamageContext { ExecutionId = new AttackIdentity(_cast.Caster.GetInstanceId(), ++_sequence),
			SourceNode = _source, OwnerNode = _cast.Caster, Faction = HurtBoxComponent.GetDamageSource(_cast.Caster),
			Random = _cast.Random,
			DefenseTraits = AttackDefenseTraits.Dodgeable | AttackDefenseTraits.Guardable, DeliveryKind = DamageDeliveryKind.AreaImpact,
			Element = _definition.Kind == LingeringAreaKind.SolarFlares ? DamageElement.Fire : DamageElement.Water,
			BypassArmor = hail, SuppressHitReaction = hail,
			ForcedPush = _definition.Kind == LingeringAreaKind.IceWall ? _definition.RepelPush : 0 };
		AreaSpellTargets.Resolve(targets, _cast.World, origin, _cast.Facing, contact, _hit, _isActive, afterContact);
	}
	private bool TouchesFlame(Vector3 target, Vector3[] heads)
	{
		for (int i = 0; i < heads.Length; i++)
		{
			Vector2 a = new(_previousHeads[i].X, _previousHeads[i].Z), b = new(heads[i].X, heads[i].Z), point = new(target.X, target.Z);
			Vector2 segment = b - a;
			float t = segment.LengthSquared() > .00001f ? Mathf.Clamp((point - a).Dot(segment) / segment.LengthSquared(), 0, 1) : 0;
			if (point.DistanceTo(a + segment * t) <= _definition.FlameContactRadius) return true;
		}
		return false;
	}
	public void End()
	{
		if (_ended) return;
		_ended = true; Visible = false; SetPhysicsProcess(false);
		var slowed = _slowed.ToArray(); _slowed.Clear();
		foreach (var statuses in slowed) if (IsInstanceValid(statuses)) statuses.RemoveStatuses(this);
		QueueFree();
	}
	public override void _ExitTree() => End();
}