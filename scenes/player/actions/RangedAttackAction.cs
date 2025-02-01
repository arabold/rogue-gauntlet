using Godot;

[GlobalClass]
public partial class RangedAttackAction : PlayerAction
{
	public RangedAttackAction()
	{
		AnimationId = "ranged_attack";
		Delay = 0.3f;
		PerformDuration = 0.5f;
		CooldownDuration = 0f;
	}

	public override void Trigger(Player player)
	{
		player.RangedAttack();
	}
}
