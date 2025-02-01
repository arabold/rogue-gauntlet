using Godot;
using System.Collections.Generic;

[GlobalClass]
public partial class Item : ObservableResource
{
	[Export] public string Name { get; private set => SetValue(ref field, value); }
	[Export] public PackedScene Scene { get; private set => SetValue(ref field, value); }
	[Export] public float Quality { get; private set => SetValue(ref field, value); } = 1.0f;
	[Export] public int Value { get; private set => SetValue(ref field, value); } = 0;
	[Export] public float Weight { get; private set => SetValue(ref field, value); } = 0.0f;
	[Export] public bool UsesSlot { get; private set => SetValue(ref field, value); } = true;
	[Export] public bool IsStackable { get; private set => SetValue(ref field, value); } = false;
	/// <summary>
	/// Quest items cannot be dropped or destroyed.
	/// </summary>
	[Export] public bool IsQuestItem { get; private set => SetValue(ref field, value); } = false;

	public virtual void OnPickup(Player player, int quantity)
	{ }

	public virtual void OnDropped(Player player, int quantity)
	{ }

	public virtual void OnDestroyed(Player player, int quantity)
	{ }
}
