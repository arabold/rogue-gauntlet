using Godot;

public partial class HeavyAttackAction : ActionBase
{
	public override string Id => "heavy_attack";
	public override float PerformDuration => 1.0f;
	public override float CooldownDuration => 0.0f;

	private IWeapon _weapon;

	public HeavyAttackAction(IWeapon weapon)
	{
		_weapon = weapon;
	}

	public override void Execute(Player player)
	{
		GD.Print($"{player.Name} performing heavy attack!");
		_weapon.Attack();
	}
}
