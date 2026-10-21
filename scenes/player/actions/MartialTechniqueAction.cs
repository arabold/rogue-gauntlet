using Godot;

public partial class MartialTechniqueAction : RefCounted, ICombatAttackAction
{
	public Weapon Weapon { get; }

	public MartialTechniqueAction(Weapon weapon)
	{
		Weapon = weapon;
	}

	public string AnimationId => Weapon?.SecondaryAnimationId ?? "special_attack";
	public float Delay => Weapon?.SecondaryDelay ?? 0.1f;
	public float PerformDuration => Weapon?.SecondaryPerformDuration ?? 0.6f;
	public float CooldownDuration => Weapon?.SecondaryCooldownDuration ?? 1.0f;
	public string CooldownKey => Weapon != null ? Weapon.GetEffectiveSecondaryStyle().ToString() : "special.martial";

	public bool TryPrepare(Player player, out PreparedAttack attack)
	{
		if (Weapon == null)
		{
			attack = null;
			return false;
		}
		return player.AttackController.TryPrepareWeaponAttack(Weapon, out attack, isSpecial: true, duration: PerformDuration);
	}

	public bool StartPrepared(Player player, PreparedAttack attack)
	{
		return player.AttackController.StartPreparedWeaponAttack(attack, Weapon, isSpecial: true);
	}

	public void PerformAction(Player player)
	{
		player.SpecialAttack();
	}
}