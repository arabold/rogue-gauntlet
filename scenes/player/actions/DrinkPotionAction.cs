using Godot;

[GlobalClass]
public partial class DrinkPotionAction : PlayerAction
{
	public DrinkPotionAction()
	{
		AnimationId = "drink_potion";
	}

	public override void ApplyEffect(Player player)
	{
	}
}
