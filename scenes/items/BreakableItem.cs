using Godot;

[GlobalClass]
public partial class BreakableItem : Item
{
	public void Damage(int amount)
	{
		GD.Print($"{Name} is damaged by {amount}");
		Durability -= amount;
		if (Durability <= 0)
		{
			Break();
		}
	}

	public void Repair(int amount)
	{
		GD.Print($"{Name} is repaired by {amount}");
		Durability += amount;
	}

	public void Break()
	{
		GD.Print($"{Name} is broken");
		SignalBus.EmitItemDestroyed(this);
	}
}
