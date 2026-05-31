using Godot;

/// <summary>
/// Controls player combat attacks by coordinating equipped weapons with the animation-synchronized AttackController.
/// </summary>
public partial class PlayerAttackController : AttackController
{
	[Export] public Player Player { get; set; }

	public override void _Ready()
	{
		base._Ready();
		Player ??= GetParent<Player>();
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

		// Target mask is 24 (detect enemies and damageables)
		uint targetMask = 24;

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
			def.AttachHitBoxToWeapon = true;
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
