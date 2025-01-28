using Godot;

[GlobalClass]
public partial class Item : Resource
{
	[Export] public string Name { get; private set; }
	[Export] public PackedScene Scene { get; private set; }
	[Export] public float Quality { get; private set; } = 1.0f;
	[Export] public int Value { get; private set; } = 0;
	[Export] public float Weight { get; private set; } = 0.0f;
	[Export] public bool UsesSlot { get; private set; } = true;
	[Export] public bool IsStackable { get; private set; } = false;
	/// <summary>
	/// Quest items cannot be dropped or destroyed.
	/// </summary>
	[Export] public bool IsQuestItem { get; private set; } = false;

	public virtual void OnPickup(Player player, int quantity)
	{ }

	public virtual void OnDropped(Player player, int quantity)
	{ }

	public virtual void OnDestroyed(Player player, int quantity)
	{ }
}
