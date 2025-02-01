using Godot;

[GlobalClass]
public partial class MeleeAttackAction : PlayerAction
{
	public MeleeAttackAction()
	{
		AnimationId = "melee_attack";
		Delay = 0f;
		PerformDuration = 0.5f;
		CooldownDuration = 0f;
	}

	public override void Trigger(Player player)
	{
		player.MeleeAttack();
	}
}
