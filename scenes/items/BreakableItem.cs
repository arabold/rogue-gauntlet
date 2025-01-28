using Godot;

[GlobalClass]
public partial class BreakableItem : Item
{
	[Export] public float Durability = 1.0f;

	public void Damage(int amount)
	{
		GD.Print($"{Name} is damaged by {amount}");
		Durability -= amount;
		if (Durability <= 0)
		{
			GD.Print($"{Name} is broken");
		}
		EmitChanged();
	}

	public void Repair(int amount)
	{
		GD.Print($"{Name} is repaired by {amount}");
		Durability += amount;
		EmitChanged();
	}

	public void OnBroken()
	{ }
}
