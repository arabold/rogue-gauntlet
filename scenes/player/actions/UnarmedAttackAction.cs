using Godot;

[GlobalClass]
public partial class UnarmedAttackAction : Resource, ICombatAttackAction
{
	public static UnarmedAttackAction Instance { get; } = new();

	[Export] public string AnimationId { get; set; } = "melee_attack";
	[Export] public float Delay { get; set; } = 0f;
	[Export] public float PerformDuration { get; set; } = 0.5f;
	[Export] public float CooldownDuration { get; set; } = 0f;
	public string CooldownKey => "unarmed.punch";

	public bool TryPrepare(Player player, out PreparedAttack attack)
	{
		return player.AttackController.TryPrepareWeaponAttack(null, out attack, isSpecial: false);
	}

	public bool StartPrepared(Player player, PreparedAttack attack)
	{
		return player.AttackController.StartPreparedWeaponAttack(attack, null);
	}

	public void PerformAction(Player player)
	{
		player.MeleeAttack();
	}
}