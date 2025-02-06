using Godot;
using System.Collections.Generic;

[GlobalClass]
public partial class Item : ObservableResource
{
	[Export] public string Name { get; protected set => SetValue(ref field, value); }
	[Export] public PackedScene Scene { get; protected set => SetValue(ref field, value); }
	[Export] public float Quality { get; protected set => SetValue(ref field, value); } = 1.0f;
	[Export] public int Value { get; protected set => SetValue(ref field, value); } = 0;
	[Export] public bool ShowInInventory { get; protected set => SetValue(ref field, value); } = true;
	[Export] public bool IsStackable { get; protected set => SetValue(ref field, value); } = false;
	/// <summary>
	/// Quest items cannot be dropped or destroyed.
	/// </summary>
	[Export] public bool IsQuestItem { get; protected set => SetValue(ref field, value); } = false;

	public virtual void OnPickup(Player player, int quantity)
	{ }

	public virtual void OnDropped(Player player, int quantity)
	{ }

	public virtual void OnDestroyed(Player player, int quantity)
	{ }
}
