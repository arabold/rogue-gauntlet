using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>Resolves one contact per enemy as a blizzard or moving flame ribbons touch it.</summary>
public partial class LingeringAreaField : Node3D
{
	private LingeringAreaSpell _source;
	private AreaCastLifecycle.Cast _cast;
	private LingeringAreaDefinition _definition;
	private Node3D _visual;
	private int _level;
	private readonly HashSet<Node3D> _contacted = [];
	private readonly HashSet<Node3D> _successful = [];
	private readonly HashSet<BuffController> _slowed = [];
	private Vector3[] _previousHeads;
	private ulong _sequence;
	private bool _ended;

	public void Configure(LingeringAreaSpell source, AreaCastLifecycle.Cast cast, LingeringAreaDefinition definition, int level, Node3D visual)
	{
		_source = source; _cast = cast; _definition = definition; _level = level; _visual = visual;
	}
	public override void _PhysicsProcess(double delta)
	{
		if (_ended || !IsInstanceValid(_source) || !_source.IsInsideTree() || !IsInstanceValid(_cast.Caster) || !_cast.Caster.IsInsideTree()
			|| !IsInstanceValid(_visual) || _visual.IsQueuedForDeletion()) { End(); return; }
		float radius = _definition.RadiusByLevel[_level - 1];
		var targets = AreaSpellTargets.Capture(_source, _cast.Caster, _cast.Origin, radius, _cast.World) ?? [];
		var present = new HashSet<BuffController>();
		Vector3[] heads = _visual is FireSpellVisual fire ? fire.Sweep.Heads.Take(_level).Select(head => head.GlobalPosition).ToArray() : [];
		_previousHeads ??= heads;
		foreach (var target in targets)
		{
			if (!AreaSpellTargets.IsVisible(target, _cast.World, _cast.Origin)) continue;
			bool touches = _definition.Kind == LingeringAreaKind.Snowstorm || TouchesFlame(target.GlobalPosition, heads);
			if (!touches) continue;
			if (_contacted.Add(target))
			{
				Vector3 direction = target.GlobalPosition - _cast.Origin; direction.Y = 0;
				var contact = new DamageContext { ExecutionId = new AttackIdentity(_cast.Caster.GetInstanceId(), ++_sequence),
					SourceNode = _source, OwnerNode = _cast.Caster, Faction = HurtBoxComponent.GetDamageSource(_cast.Caster),
					Amount = _definition.DamageByLevel[_level - 1], Random = _cast.Random, IncomingDirection = direction.Normalized(),
					DefenseTraits = AttackDefenseTraits.Dodgeable | AttackDefenseTraits.Guardable, DeliveryKind = DamageDeliveryKind.AreaImpact,
					Element = _definition.Kind == LingeringAreaKind.SolarFlares ? DamageElement.Fire : DamageElement.Water };
				var result = ((ICombatDamageable)target).ResolveDamage(in contact);
				if (_ended) return;
				if (result.Outcome == DamageOutcome.Damaged)
				{
					_successful.Add(target);
					if (_definition.Kind == LingeringAreaKind.SolarFlares && IsInstanceValid(target) && target.IsInsideTree()) _source.Ignite(_cast.Caster, target, _cast.Random, _level);
				}
			}
			if (_definition.Kind == LingeringAreaKind.Snowstorm && _successful.Contains(target)
				&& target is IStatusActor { BuffController: { } active } && target is ISlowActor { SlowStatus: not null }
				&& active.Health?.IsDead != true)
			{
				if (!active.HasStatusFrom(this))
				{
					_slowed.Add(active);
					if (!active.SetPersistentStatus(_definition.SlowConditions[_level - 1], this, true)) _slowed.Remove(active);
					if (_ended) return;
				}
				if (_slowed.Contains(active)) present.Add(active);
			}
		}
		foreach (var statuses in _slowed.Where(value => !present.Contains(value)).ToArray())
		{
			_slowed.Remove(statuses); if (IsInstanceValid(statuses)) statuses.RemoveStatuses(this);
		}
		_previousHeads = heads;
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
