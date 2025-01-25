using Godot;

[GlobalClass]
public partial class Item : Resource
{
	[Export] public PackedScene Scene;
	[Export] public Texture Icon;
	[Export] public string Name;
	[Export] public float Durability = 1.0f;
	[Export] public float Quality = 1.0f;
	[Export] public int Value = 0;
	[Export] public float Weight = 0.0f;
	[Export] public bool UsesSlot = true;
	[Export] public bool IsStackable = false;
	[Export] public bool IsConsumable = false;
	[Export] public bool IsEquippable = false;
	// [Export] public bool IsKey = false;
	// [Export] public bool IsQuestItem = false;
	// [Export] public bool IsUnique = false;

	public virtual void OnPickup(Player player, int quantity)
	{
	}

	public virtual void OnDrop(Player player, int quantity)
	{
	}
}
