using Godot;

[GlobalClass]
public partial class EquippableItem : Item
{
	public void Equip(Player player)
	{
		GD.Print($"{Name} is equipped");
		SignalBus.EmitItemEquipped(this);
	}

	public void Unequip(Player player)
	{
		GD.Print($"{Name} is unequipped");
		SignalBus.EmitItemUnequipped(this);
	}
}
