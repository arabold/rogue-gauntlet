using Godot;

[GlobalClass]
public partial class Gold : Item
{
    public override void OnPickup(Player player, int quantity)
    {
        player.Stats.AddGold(Value * quantity);
        GD.Print($"{player.Name} picked up {Value} gold");
    }
}
