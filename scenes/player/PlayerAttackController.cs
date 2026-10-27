using Godot;

/// <summary>
/// Controls player combat attacks by coordinating equipped weapons with the animation-synchronized AttackController.
/// </summary>
public partial class PlayerAttackController : AttackController
{
	[Export] public Player Player { get; set; }
	[Export] public MeleeAttackProfile SpecialMeleeContact { get; set; }

	/// <summary>Impact of weapon attacks without a custom definition. Custom definitions author their own.</summary>
	[ExportGroup("Default impact")]
	[Export] public float PrimaryPoiseDamage { get; set; } = AttackDefinition.DefaultPoiseDamage;
	[Export] public float PrimaryHitStop { get; set; } = 0.05f;
	[Export] public float SecondaryPoiseDamage { get; set; } = 25f;
	[Export] public float SecondaryHitStop { get; set; } = 0.08f;
	/// <summary>Normalized hyper-armor window of the default secondary technique, so a heavy swing can trade through light hits.</summary>
	[Export(PropertyHint.Range, "0,1,0.01")] public float SecondaryHyperArmorStart { get; set; } = 0.1f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float SecondaryHyperArmorEnd { get; set; } = 0.75f;
	/// <summary>The default melee secondary breaks a monster's unparried guard.</summary>
	[Export] public bool SecondaryBreaksGuard { get; set; } = true;

	/// <summary>
	/// Target locked in at the start of the current attack for aim-assist facing, so the player turns
	/// to face the enemy they are swinging at instead of needing to line up manually. Null when no
	/// target was in range/cone; the input controller then falls back to the movement facing.
	/// </summary>
	private Node3D _aimAssistTarget;
	private Weapon _activeWeapon;

	public override void _Ready()
	{
		base._Ready();
		Player ??= GetParent<Player>();
	}

	/// <summary>
	/// Direction the player should face this frame for attack aim-assist, or <see cref="Vector3.Zero"/>
	/// when there is no locked target. Consumed by <see cref="PlayerInputController"/> while an action
	/// is playing.
	/// </summary>
	public Vector3 GetAimAssistFacing()
	{
		if (HasCommittedFacing && IsAttacking) return CommittedFacing;
		if (!IsAttacking || _aimAssistTarget == null || !GodotObject.IsInstanceValid(_aimAssistTarget))
		{
			return Vector3.Zero;
		}

		Vector3 direction = _aimAssistTarget.GlobalPosition - Player.GlobalPosition;
		direction.Y = 0;
		return direction.LengthSquared() > 0.0001f ? direction.Normalized() : Vector3.Zero;
	}

	public void PerformMeleeAttack()
	{
		PerformWeaponAttack(isSpecial: false);
	}

	public void PerformSpecialAttack()
	{
		PerformWeaponAttack(isSpecial: true);
	}

	public void PerformRangedAttack()
	{
		PerformWeaponAttack(isSpecial: false);
	}

	private void PerformWeaponAttack(bool isSpecial)
	{
		var inventory = Player.Inventory;

		Weapon weapon = null;
		if (inventory.EquippedItems.TryGetValue(EquipmentSlot.WeaponHand, out var equippedItem))
		{
			weapon = equippedItem?.Item as Weapon;
		}

		if (TryPrepareWeaponAttack(weapon, out var attack, isSpecial))
		{
			StartPreparedWeaponAttack(attack, weapon, isSpecial);
		}
	}

	/// <summary>Prepare without mutating resources or starting action cooldowns.</summary>
	public bool TryPrepareWeaponAttack(Weapon weapon, out PreparedAttack attack, bool isSpecial = false, double duration = 0)
	{
		var definition = (!isSpecial ? weapon?.CustomAttackDefinition : weapon?.SecondaryCustomAttackDefinition)
			?? CreateDefaultDefinition(weapon, isSpecial);
		var style = isSpecial ? (weapon?.GetEffectiveSecondaryStyle() ?? "unarmed.kick") : (weapon?.AttackStyle ?? "unarmed.punch");
		if (duration <= 0)
		{
			duration = isSpecial ? (weapon?.SecondaryPerformDuration ?? 0.6) : (weapon?.PerformDuration ?? 0.5);
		}
		var contact = isSpecial ? (weapon?.GetEffectiveSecondaryMeleeContact(SpecialMeleeContact) ?? SpecialMeleeContact) : weapon?.MeleeContact;
		return TryPrepareAttack(definition, style, duration, out attack, contact);
	}

	/// <summary>Start the accepted snapshot with current actor stats and local aim assistance.</summary>
	public bool StartPreparedWeaponAttack(PreparedAttack attack, Weapon weapon = null, bool isSpecial = false, ActorActionLease lease = default)
	{
		if (Player.IsDead || Player.IsHit || IsAttacking) return false;
		_activeWeapon = weapon;
		_aimAssistTarget = FindMeleeAimAssistTarget();
		var stats = Player.Stats;
		float multiplier = isSpecial ? (weapon?.SecondaryDamageMultiplier ?? 1.5f) : 1.0f;
		return StartAttack(attack, stats.MinDamage * multiplier, stats.MaxDamage * multiplier, stats.CritChance, 24, lease);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (IsAttacking && _activeWeapon != null
			&& (!Player.Inventory.EquippedItems.TryGetValue(EquipmentSlot.WeaponHand, out var slot)
				|| !ReferenceEquals(slot?.Item, _activeWeapon)))
		{
			CancelAttack();
		}
		base._PhysicsProcess(delta);
	}

	private AttackDefinition CreateDefaultDefinition(Weapon weapon, bool isSpecial)
	{
		var def = new AttackDefinition
		{
			PoiseDamage = isSpecial ? SecondaryPoiseDamage : PrimaryPoiseDamage,
			HitStopDuration = isSpecial ? SecondaryHitStop : PrimaryHitStop,
			HyperArmorStart = isSpecial ? SecondaryHyperArmorStart : 0,
			HyperArmorEnd = isSpecial ? SecondaryHyperArmorEnd : 0
		};
		if (weapon != null)
		{
			def.AnimationId = isSpecial ? weapon.SecondaryAnimationId : weapon.AnimationId;
			def.AttackStyle = isSpecial ? weapon.GetEffectiveSecondaryStyle() : weapon.AttackStyle;
			float duration = isSpecial ? weapon.SecondaryPerformDuration : weapon.PerformDuration;
			def.HitWindowStart = (isSpecial ? 0.25f : 0.3f) * duration;
			def.HitWindowEnd = (isSpecial ? 0.75f : 0.7f) * duration;
			def.Range = weapon is RangedWeapon ranged ? ranged.Range : 20.0f;
			def.IsRanged = weapon is RangedWeapon;
			if (def.IsRanged) def.DefenseTraits = AttackDefenseTraits.Dodgeable | AttackDefenseTraits.Guardable;
			if (isSpecial && !def.IsRanged && SecondaryBreaksGuard) def.DefenseTraits |= AttackDefenseTraits.BreaksGuard;

			if (weapon is RangedWeapon rangedWeapon)
			{
				def.ProjectileSpeed = rangedWeapon.ProjectileSpeed * (isSpecial ? 1.25f : 1.0f);
				def.AimingAngle = rangedWeapon.AimingAngle;
			}
		}
		else
		{
			def.AnimationId = isSpecial ? "special_attack" : "melee_attack";
			def.AttackStyle = isSpecial ? "unarmed.kick" : "unarmed.punch";
			def.HitWindowStart = 0.2f;
			def.HitWindowEnd = 0.6f;
		}

		return def;
	}
}