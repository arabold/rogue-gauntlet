using Godot;

/// <summary>A focus-owned magic guard; the hurtbox owns at most one active shield.</summary>
public partial class MagicGuardSpell : Node, ICombatSpell
{
	[Export] public MagicGuardDefinition Definition { get; set; }
	[Export] public PackedScene VisualScene { get; set; }
	public string DisplayName => Definition?.SpellName ?? "";
	public int LevelCount => Definition?.LevelCount ?? 0;
	private Guard _guard;

	public bool Cast(Node3D caster, int level, RandomNumberGenerator random)
	{
		if (Definition?.IsValidLevel(level) != true || VisualScene == null || random == null
			|| !IsInsideTree() || !IsInstanceValid(caster) || !caster.IsInsideTree()
			|| caster is not IShieldOwner { ShieldHurtBox: { } hurtbox }
			|| !hurtbox.IsInsideTree() || hurtbox.HealthComponent?.IsDead == true) return false;
		Node node = VisualScene.Instantiate();
		if (node is not Node3D visual || node is not IGuardVisual effect
			|| !effect.ConfigureGuard(level, GetInstanceId() + (ulong)level))
		{ node?.Free(); return false; }
		caster.AddChild(visual);
		var guard = new Guard(this, caster, hurtbox, visual, effect,
			Definition.CapacityByLevel[level - 1], Definition.ReflectionByLevel[level - 1], Definition.Element);
		Cancel();
		_guard = guard;
		hurtbox.InstallShield(guard);
		return true;
	}
	public void Cancel() { var guard = _guard; _guard = null; guard?.End(); }
	public override void _ExitTree() => Cancel();

	private sealed class Guard : ICombatShield
	{
		private readonly MagicGuardSpell _source;
		private readonly Node3D _owner, _visual;
		private readonly HurtBoxComponent _hurtbox;
		private readonly IGuardVisual _effect;
		private readonly float _reflection;
		private readonly DamageElement _element;
		private float _remaining;
		private bool _ended, _reflectAfterDeath;
		public Guard(MagicGuardSpell source, Node3D owner, HurtBoxComponent hurtbox, Node3D visual,
			IGuardVisual effect, float capacity, float reflection, DamageElement element)
		{
			_source = source; _owner = owner; _hurtbox = hurtbox; _visual = visual;
			_effect = effect; _remaining = capacity; _reflection = reflection; _element = element;
			_owner.TreeExiting += End;
			if (_hurtbox.HealthComponent != null) _hurtbox.HealthComponent.Died += EndForDeath;
		}
		public float Absorb(float damage)
		{
			if (_ended || damage <= 0) return 0;
			float absorbed = Mathf.Min(_remaining, damage);
			_remaining -= absorbed; _effect.FlashGuard();
			return absorbed;
		}
		public void Reflect(float absorbed, in DamageContext contact)
		{
			if (_ended && !_reflectAfterDeath) return;
			_reflectAfterDeath = false;
			if (_remaining <= 0) End();
			if (contact.IsReflected || _reflection <= 0 || !IsInstanceValid(_owner) || !_owner.IsInsideTree()
				|| contact.OwnerNode is not Node3D attacker || !IsInstanceValid(attacker) || !attacker.IsInsideTree()
				|| attacker is not ICombatDamageable damageable || attacker is not IShieldOwner { ShieldHurtBox: { } target }
				|| target.HealthComponent?.IsDead == true) return;
			var ownerFaction = HurtBoxComponent.GetDamageSource(_owner);
			var attackerFaction = HurtBoxComponent.GetDamageSource(attacker);
			bool hostile = ownerFaction == DamageSourceFlags.Player
				? attackerFaction is DamageSourceFlags.Enemy or DamageSourceFlags.Boss
				: ownerFaction is DamageSourceFlags.Enemy or DamageSourceFlags.Boss && attackerFaction == DamageSourceFlags.Player;
			if (!hostile) return;
			Vector3 direction = attacker.GlobalPosition - _owner.GlobalPosition; direction.Y = 0;
			var reflected = new DamageContext { SourceNode = _source, OwnerNode = _owner, Faction = ownerFaction,
				Amount = absorbed * _reflection, Random = contact.Random, IncomingDirection = direction.Normalized(),
				DefenseTraits = AttackDefenseTraits.Dodgeable | AttackDefenseTraits.Guardable,
				DeliveryKind = DamageDeliveryKind.AreaImpact, Element = _element, IsReflected = true };
			damageable.ResolveDamage(in reflected);
		}
		private void EndForDeath()
		{
			// The in-flight contact finishes after death; its absorbed portion still reflects.
			_reflectAfterDeath = true;
			End();
		}
		public void End()
		{
			if (_ended) return;
			_ended = true;
			if (IsInstanceValid(_owner)) _owner.TreeExiting -= End;
			if (IsInstanceValid(_hurtbox))
			{
				_hurtbox.RemoveShield(this);
				if (IsInstanceValid(_hurtbox.HealthComponent)) _hurtbox.HealthComponent.Died -= EndForDeath;
			}
			if (IsInstanceValid(_visual)) { _visual.Visible = false; _visual.QueueFree(); }
			if (IsInstanceValid(_source) && ReferenceEquals(_source._guard, this)) _source._guard = null;
		}
	}
}
