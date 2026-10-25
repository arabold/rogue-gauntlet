using Godot;
using System.Collections.Generic;

[Tool]
[GlobalClass]
public partial class Item : ObservableResource
{
	[Export] public string Name { get; protected set => SetValue(ref field, value); }
	[Export] public PackedScene Scene { get; protected set => SetValue(ref field, value); }
	[Export] public int Value { get; protected set => SetValue(ref field, value); } = 0;
	[Export] public bool ShowInInventory { get; protected set => SetValue(ref field, value); } = true;
	[Export] public bool IsStackable { get; protected set => SetValue(ref field, value); } = false;
	[Export(PropertyHint.Range, "1,999")] public int MaxStackSize { get; protected set; } = 1;
	/// <summary>Runtime provenance and identity; definitions have neither.</summary>
	public string SourceDefinitionPath { get; internal set; } = "";
	public string InstanceId { get; internal set; } = "";
	public bool IsRuntimeInstance => !string.IsNullOrEmpty(InstanceId);
	public bool CanStack => IsStackable && this is not (EquipableItem or FoodItem) && MaxStackSize > 1;
	public string DefinitionPath => IsRuntimeInstance ? SourceDefinitionPath : ResourcePath;
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