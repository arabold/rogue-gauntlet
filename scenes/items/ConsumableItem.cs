using Godot;

[GlobalClass]
public partial class ConsumableItem : Item
{
	[Export] public Buff Buff;

	public void Consume(Player player)
	{
		GD.Print($"Consuming {Name}");
		if (Buff != null)
		{
			player.ApplyBuff(Buff);
		}
		player.RemoveItem(this);
		SignalBus.EmitItemConsumed(this);
	}
}
