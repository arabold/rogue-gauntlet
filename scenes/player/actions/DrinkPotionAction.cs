using Godot;

public partial class DrinkPotionAction : ActionBase
{
    public override string Id => "drink_potion";
    public override float PerformDuration => 0.5f;
    public override float CooldownDuration => 0.0f;

    public DrinkPotionAction()
    {
    }

    public override void Execute(Player player)
    {
        GD.Print($"{player.Name} drinking potion!");
    }
}
