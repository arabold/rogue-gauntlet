using Godot;

public partial class QuickAttackAction : ActionBase
{
	public override string Id => "quick_attack";
	public override float PerformDuration => 0.5f;
	public override float CooldownDuration => 0.0f;

	private IWeapon _weapon;

	public QuickAttackAction(IWeapon weapon)
	{
		_weapon = weapon;
	}

	public override void Execute(Player player)
	{
		GD.Print($"{player.Name} performing quick attack!");
		_weapon.Attack();
	}
}
