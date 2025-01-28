using Godot;

[GlobalClass]
public partial class ConsumableItem : BuffedItem
{
	public void OnConsumed(Player player)
	{
		GD.Print($"Consuming {Name}");
		ApplyBuff(player);
	}
}
