using Godot;

/// <summary>
/// Controls player combat attacks by coordinating equipped weapons with the animation-synchronized AttackController.
/// </summary>
public partial class PlayerAttackController : AttackController
{
	[Export] public Player Player { get; set; }

	/// <summary>
	/// Target locked in at the start of the current attack for aim-assist facing, so the player turns
	/// to face the enemy they are swinging at instead of needing to line up manually. Null when no
	/// target was in range/cone; the input controller then falls back to the movement facing.
	/// </summary>
	private Node3D _aimAssistTarget;

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
		if (_aimAssistTarget == null || !GodotObject.IsInstanceValid(_aimAssistTarget))
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
		var stats = Player.Stats;
		var inventory = Player.Inventory;

		Weapon weapon = null;
		if (inventory.EquippedItems.TryGetValue(EquipmentSlot.WeaponHand, out var equippedItem))
		{
			weapon = equippedItem?.Item as Weapon;
		}

		AttackDefinition def = null;
		if (weapon != null)
		{
			def = weapon.CustomAttackDefinition;
		}

		// Fallback to generate a default definition if none is authored in resource
		if (def == null)
		{
			def = CreateDefaultDefinition(weapon, isSpecial);
		}

		// Target mask is 24 (enemies + props)
		uint targetMask = 24;

		// Lock an aim-assist target so the player turns toward the enemy they are attacking. This is
		// what makes melee forgiving: the swing no longer requires exact manual facing.
		_aimAssistTarget = FindMeleeAimAssistTarget();

		StartAttack(
			def,
			stats.MinDamage,
			stats.MaxDamage,
			stats.Accuracy,
			stats.CritChance,
			targetMask
		);
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

		if (!def.IsRanged && !isSpecial)
		{
			// Forgiving frontal arc anchored to the actor rather than the swinging weapon bone, so the
			// hit volume is consistent frame-to-frame and connects across a wide wedge in front of the
			// player. Aim-assist turns the player toward the target, so this only needs to be generous,
			// not precisely aligned with the weapon.
			def.AttachHitBoxToWeapon = false;
			def.HitBoxSize = new Vector3(2.6f, 2.0f, 2.6f);
			def.HitBoxOffset = new Vector3(0.0f, 0.9f, -1.3f);
		}

		if (isSpecial)
		{
			def.AnimationId = "spin_attack"; // special spin/heavy swing attack
			def.AttachHitBoxToWeapon = false;
			def.HitBoxSize = new Vector3(2.8f, 2.0f, 2.8f); // larger hit area for special spin
			def.HitBoxOffset = new Vector3(0.0f, 0.7f, 0.0f);
			def.HitWindowStart = 0.1f;
			def.HitWindowEnd = 0.4f;
		}

		return def;
	}
}
