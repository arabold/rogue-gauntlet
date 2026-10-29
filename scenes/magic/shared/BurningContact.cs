using Godot;

/// <summary>A focus-owned burn activation that ticks without staggering or rerolling defenses.</summary>
public partial class BurningContact : Node
{
	public event System.Action<BurningContact> Ended;
	private BuffController _statuses;
	private Node3D _caster, _target;
	private RandomNumberGenerator _random;
	private StatusCondition _condition;
	private float _damage, _interval, _remaining, _untilTick;
	private bool _ended;
	public override void _Ready() => ProcessPhysicsPriority = CombatPhysics.StatusDamage;
	public bool Begin(Node3D caster, Node3D target, BuffController statuses, StatusCondition condition,
		RandomNumberGenerator random, float duration, float interval, float damage)
	{
		_target = target; _statuses = statuses; _condition = condition;
		return Refresh(caster, random, duration, interval, damage);
	}
	public bool Refresh(Node3D caster, RandomNumberGenerator random, float duration, float interval, float damage)
	{
		if (_ended || !IsInstanceValid(_statuses) || _statuses.Health?.IsDead != false
			|| !_statuses.ApplyStatus(_condition, this, duration) || _ended) return false;
		_caster = caster; _random = random; _remaining = duration; _interval = interval; _damage = damage; _untilTick = interval;
		return true;
	}
	public override void _PhysicsProcess(double delta)
	{
		if (_ended || !IsInstanceValid(_caster) || !_caster.IsInsideTree() || !IsInstanceValid(_target) || !_target.IsInsideTree()
			|| !IsInstanceValid(_statuses) || !_statuses.IsInsideTree() || _statuses.Health?.IsDead != false
			|| !_statuses.HasStatusFrom(this)) { End(); return; }
		float step = Mathf.Min((float)delta, _remaining); _remaining -= step; _untilTick -= step;
		while (_untilTick <= .000001f && !_ended && IsInstanceValid(_statuses) && IsInstanceValid(_statuses.Health) && !_statuses.Health.IsDead)
		{
			Vector3 direction = _target.GlobalPosition - _caster.GlobalPosition; direction.Y = 0;
			var contact = new DamageContext { SourceNode = this, OwnerNode = _caster, Faction = HurtBoxComponent.GetDamageSource(_caster),
				Amount = _damage, Random = _random, IncomingDirection = direction.Normalized(), DeliveryKind = DamageDeliveryKind.Hazard,
				Element = DamageElement.Fire, BypassArmor = true, SuppressHitReaction = true };
			((ICombatDamageable)_target).ResolveDamage(in contact); _untilTick += _interval;
		}
		if (!_ended && (_remaining <= 0 || !IsInstanceValid(_statuses) || !IsInstanceValid(_statuses.Health) || _statuses.Health.IsDead)) End();
	}
	public void End()
	{
		if (_ended) return;
		_ended = true; SetPhysicsProcess(false);
		Ended?.Invoke(this);
		if (IsInstanceValid(_statuses)) _statuses.RemoveStatuses(this);
		QueueFree();
	}
	public override void _ExitTree() => End();
}
