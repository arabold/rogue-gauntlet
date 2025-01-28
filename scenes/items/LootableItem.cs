using Godot;
using System;


/// <summary>
/// Represents an item that can be picked up by the player.
/// </summary>
public partial class LootableItem : Node3D
{
	[Export] public Item Item;
	[Export] public int Quantity = 1;
	/// <summary>
	/// Waits for the player to exit first before triggering a pickup.
	/// This is useful when a player just dropped an item and is still within
	/// the trigger zone. In that case we want to wait until the player leaves
	/// before listening for new trigger events.
	/// 
	/// This must be set before the LootableItem is added to the tree!
	/// </summary>
	public bool WaitForPlayerExited = false;

	public Node3D Pivot;

	public override void _Ready()
	{
		base._Ready();

		if (!Engine.IsEditorHint())
		{
			var trigger = GetNode<TriggerComponent>("TriggerComponent");
			trigger.WaitForBodyExited = WaitForPlayerExited;
			trigger.Triggered += OnTriggered;
		}

		if (Item == null)
		{
			GD.PrintErr("LootableItem has no item assigned");
			QueueFree();
			return;
		}
		if (Item.Scene == null)
		{
			GD.PrintErr("LootableItem's item has no scene assigned");
			QueueFree();
			return;
		}

		Pivot = GetNode<Node3D>("Pivot");

		// Instantiate the item scene
		var itemScene = Item.Scene.Instantiate<Node>();
		UpdateItemNodeProperties(itemScene);
		Pivot.AddChild(itemScene);
		Pivot.RotateY((float)(GD.Randf() * 2 * Math.PI));
	}

	private void UpdateItemNodeProperties(Node node)
	{
		if (node is VisualInstance3D visu)
		{
			visu.Layers = 0;
			visu.SetLayerMaskValue(6, true);
		}

		foreach (Node child in node.GetChildren())
		{
			UpdateItemNodeProperties(child);
		}
	}

	private void OnTriggered(Node3D body)
	{
		if (body is Player player)
		{
			if (player.PickupItem(Item, Quantity))
			{
				QueueFree();
			}
		}
	}
}
