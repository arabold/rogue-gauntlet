using Godot;

[GlobalClass]
public partial class SpecialAttackAction : PlayerAction
{
	public SpecialAttackAction()
	{
		AnimationId = "special_attack";
		Delay = 0f;
		PerformDuration = 1f;
		CooldownDuration = 0.1f;
	}

	public override void Trigger(Player player)
	{
		player.SpecialAttack();
	}
}
