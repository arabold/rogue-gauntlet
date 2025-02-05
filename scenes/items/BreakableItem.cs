using Godot;

[GlobalClass]
public partial class BreakableItem : Item
{
	[Signal] public delegate void BrokenEventHandler();

	[Export] public float Durability { get => _durability; private set => SetValue(ref _durability, value); }

	private float _durability = 1.0f;

	public virtual void Damage(int amount)
	{
		GD.Print($"{Name} is damaged by {amount}");
		Durability -= amount;
		if (Durability <= 0)
		{
			GD.Print($"{Name} is broken");
			OnBroken();
		}
	}

	public virtual void Repair(int amount)
	{
		GD.Print($"{Name} is repaired by {amount}");
		Durability += amount;
	}

	public virtual void OnBroken()
	{
		EmitSignal(nameof(BrokenEventHandler));
	}
}
