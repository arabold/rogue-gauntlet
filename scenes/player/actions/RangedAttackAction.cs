using Godot;

public partial class RangedAttackAction : ActionBase
{
	public override string Id => "ranged_attack";
	public override float PerformDuration => 0.5f;
	public override float CooldownDuration => 0.0f;

	private IWeapon _weapon;

	public RangedAttackAction(IWeapon weapon)
	{
		_weapon = weapon;
	}

	public override async void Execute(Player player)
	{
		// Wait for half the perform duration before shooting the projectile
		await ToSignal(player.GetTree().CreateTimer(PerformDuration / 2), "timeout");

		GD.Print($"{player.Name} performing ranged attack!");
		_weapon.Attack();
	}
}
