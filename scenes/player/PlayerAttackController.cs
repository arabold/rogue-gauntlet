using Godot;

/// <summary>
/// Controls player combat attacks by coordinating equipped weapons with the animation-synchronized AttackController.
/// </summary>
public partial class PlayerAttackController : AttackController
{
	[Export] public Player Player { get; set; }
	[Export] public MeleeAttackProfile SpecialMeleeContact { get; set; }

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
			StartPreparedWeaponAttack(attack, weapon);
		}
	}

	/// <summary>Prepare without mutating resources or starting action cooldowns.</summary>
	public bool TryPrepareWeaponAttack(Weapon weapon, out PreparedAttack attack, bool isSpecial = false)
	{
		var definition = weapon?.CustomAttackDefinition ?? CreateDefaultDefinition(weapon, isSpecial);
		return TryPrepareAttack(definition, weapon?.AttackStyle ?? "unarmed.punch",
			weapon?.PerformDuration ?? 0.5, out attack, isSpecial ? SpecialMeleeContact : weapon?.MeleeContact);
	}

	/// <summary>Start the accepted snapshot with current actor stats and local aim assistance.</summary>
	public bool StartPreparedWeaponAttack(PreparedAttack attack, Weapon weapon = null)
	{
		if (Player.IsDead || Player.IsHit || IsAttacking) return false;
		_activeWeapon = weapon;
		_aimAssistTarget = FindMeleeAimAssistTarget();
		var stats = Player.Stats;
		return StartAttack(attack, stats.MinDamage, stats.MaxDamage, stats.Accuracy, stats.CritChance, 24);
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
		var def = new AttackDefinition();
		if (weapon != null)
		{
			def.AnimationId = weapon.AnimationId;
			def.HitWindowStart = 0.3f * weapon.PerformDuration;
			def.HitWindowEnd = 0.7f * weapon.PerformDuration;
			def.Range = weapon is RangedWeapon ranged ? ranged.Range : 20.0f;
			def.IsRanged = weapon is RangedWeapon;

			if (weapon is RangedWeapon rangedWeapon)
			{
				def.ProjectileSpeed = rangedWeapon.ProjectileSpeed;
				def.AimingAngle = rangedWeapon.AimingAngle;
			}
		}

		if (isSpecial)
		{
			def.AttackStyle = "sword.2h.spin";
			def.AnimationId = "spin_attack"; // special spin/heavy swing attack
			def.HitWindowStart = 0.1f;
			def.HitWindowEnd = 0.4f;
		}

		return def;
	}
}