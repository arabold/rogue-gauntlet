using Godot;

[GlobalClass]
public partial class BuffedItem : Item
{
	[Export] public Buff Buff;

	protected void ApplyBuff(Player player)
	{
		if (Buff != null)
		{
			GD.Print($"Applying {Buff.Name} to {player.Name}");
			player.ApplyBuff(Buff);
		}
	}

	protected void RemoveBuff(Player player)
	{
		if (Buff != null)
		{
			GD.Print($"Removing {Buff.Name} from {player.Name}");
			player.RemoveBuff(Buff);
		}
	}
}
