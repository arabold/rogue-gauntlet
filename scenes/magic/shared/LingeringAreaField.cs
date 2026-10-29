using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Resolves a lingering field's contacts: moving flame ribbons touch each enemy once; a hailstorm slows everyone
/// inside it and drops evenly spread shards, each hitting whoever stands where it lands; an ice wall stays where
/// it was cast and hits and shoves back out every enemy inside its ring, again while one keeps pushing in.
/// </summary>
public partial class LingeringAreaField : Node3D
{
	private LingeringAreaSpell _source;
	private AreaCastLifecycle.Cast _cast;
	private LingeringAreaDefinition _definition;
	private Node3D _visual;
	private int _level;
	private readonly HashSet<Node3D> _contacted = [];
	private readonly HashSet<BuffController> _slowed = [];
	private readonly Queue<(Vector3 Point, double Due)> _shards = new();
	private readonly Dictionary<Node3D, double> _repelled = [];
	private HailPattern _hail;
	private Vector3[] _previousHeads;
	private ulong _sequence;
	private double _clock;
	private bool _ended;

	public void Configure(LingeringAreaSpell source, AreaCastLifecycle.Cast cast, LingeringAreaDefinition definition, int level, Node3D visual)
	{
		_source = source; _cast = cast; _definition = definition; _level = level; _visual = visual;
		if (definition.Kind == LingeringAreaKind.Hailstorm) _hail = new HailPattern(cast.Random, definition.ShardsPerSecondByLevel[level - 1]);
	}
	public override void _PhysicsProcess(double delta)
	{
		if (_ended || !IsInstanceValid(_source) || !_source.IsInsideTree() || !IsInstanceValid(_cast.Caster) || !_cast.Caster.IsInsideTree()
			|| !IsInstanceValid(_visual) || _visual.IsQueuedForDeletion()) { End(); return; }
		_clock += delta;
		float radius = _definition.RadiusByLevel[_level - 1];
		var targets = (AreaSpellTargets.Capture(_source, _cast.Caster, _cast.Origin, radius, _cast.World) ?? [])
			.Where(target => AreaSpellTargets.IsVisible(target, _cast.World, _cast.Origin)).ToArray();
		if (_definition.Kind == LingeringAreaKind.Hailstorm) Hail(targets, (float)delta, radius);
		else if (_definition.Kind == LingeringAreaKind.IceWall) Repel(targets);
		else Sweep(targets);
	}
	private void Sweep(Node3D[] targets)
	{
		Vector3[] heads = _visual is FireSpellVisual fire ? fire.Sweep.Heads.Take(_level).Select(head => head.GlobalPosition).ToArray() : [];
		_previousHeads ??= heads;
		foreach (var target in targets)
		{
			if (!TouchesFlame(target.GlobalPosition, heads) || !_contacted.Add(target)) continue;
			var contact = Contact(target.GlobalPosition - _cast.Origin, false);
			var result = ((ICombatDamageable)target).ResolveDamage(in contact);
			if (_ended) return;
			if (result.Outcome == DamageOutcome.Damaged && IsInstanceValid(target) && target.IsInsideTree()) _source.Ignite(_cast.Caster, target, _cast.Random, _level);
		}
		_previousHeads = heads;
	}
	private void Hail(Node3D[] targets, float delta, float radius)
	{
		if (_visual is WaterSpellVisual { Hailstorm: { IsFalling: true } storm })
			for (int count = _hail.Advance(delta); count > 0; count--)
			{
				Vector3 point = _hail.Next(_cast.Origin, radius);
				if (WorldSight.IsClear(_cast.World, _cast.Origin + Vector3.Up, point + Vector3.Up)) _shards.Enqueue((point, _clock + storm.DropShard(point)));
			}
		while (_shards.Count > 0 && _shards.Peek().Due <= _clock)
		{
			Vector3 point = _shards.Dequeue().Point;
			foreach (var target in targets)
			{
				Vector3 offset = target.GlobalPosition - point; offset.Y = 0;
				if (offset.Length() > _definition.ShardHitRadius || !IsInstanceValid(target) || !target.IsInsideTree()) continue;
				var contact = Contact(offset, true);
				((ICombatDamageable)target).ResolveDamage(in contact);
				if (_ended) return;
			}
		}
		var present = new HashSet<BuffController>();
		foreach (var target in targets)
		{
			if (!IsInstanceValid(target) || target is not IStatusActor { BuffController: { } statuses } || target is not ISlowActor { SlowStatus: not null }
				|| statuses.Health?.IsDead == true) continue;
			if (!statuses.HasStatusFrom(this))
			{
				_slowed.Add(statuses);
				if (!statuses.SetPersistentStatus(_definition.SlowConditions[_level - 1], this, true)) _slowed.Remove(statuses);
				if (_ended) return;
			}
			if (_slowed.Contains(statuses)) present.Add(statuses);
		}
		foreach (var statuses in _slowed.Where(value => !present.Contains(value)).ToArray())
		{
			_slowed.Remove(statuses); if (IsInstanceValid(statuses)) statuses.RemoveStatuses(this);
		}
	}
	private void Repel(Node3D[] targets)
	{
		foreach (var target in targets)
		{
			if (_repelled.TryGetValue(target, out double next) && _clock < next) continue;
			_repelled[target] = _clock + _definition.RepelSeconds;
			var contact = Contact(target.GlobalPosition - _cast.Origin, false, _definition.RepelPush);
			((ICombatDamageable)target).ResolveDamage(in contact);
			if (_ended) return;
		}
	}
	/// <summary>
	/// A field contact. Hail lands many small hits, so its shards bypass armor (whose per-hit roll would swallow
	/// them) and never interrupt or stagger; the storm's slow is its control effect.
	/// </summary>
	private DamageContext Contact(Vector3 direction, bool shard, float push = 0)
	{
		direction.Y = 0;
		return new DamageContext { ExecutionId = new AttackIdentity(_cast.Caster.GetInstanceId(), ++_sequence),
			SourceNode = _source, OwnerNode = _cast.Caster, Faction = HurtBoxComponent.GetDamageSource(_cast.Caster),
			Amount = _definition.DamageByLevel[_level - 1], Random = _cast.Random, IncomingDirection = direction.Normalized(),
			DefenseTraits = AttackDefenseTraits.Dodgeable | AttackDefenseTraits.Guardable, DeliveryKind = DamageDeliveryKind.AreaImpact,
			Element = _definition.Kind == LingeringAreaKind.SolarFlares ? DamageElement.Fire : DamageElement.Water,
			BypassArmor = shard, SuppressHitReaction = shard, ForcedPush = push };
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