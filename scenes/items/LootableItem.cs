using Godot;

/// <summary>
/// Represents an item that can be picked up by the player.
/// </summary>
public partial class LootableItem : Node3D
{
	[Export] public Item Item;
	[Export] public int Quantity = 1;

	public override void _Ready()
	{
		base._Ready();

		if (!Engine.IsEditorHint())
		{
			var trigger = GetNode<TriggerComponent>("TriggerComponent");
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

		// Instantiate the item scene
		var itemScene = Item.Scene.Instantiate<Node>();
		UpdateItemNodeProperties(itemScene);
		AddChild(itemScene);
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
			if (!Item.UsesSlot || player.PickupItem(Item, Quantity))
			{
				Item.OnPickup(player, Quantity);
				QueueFree();
			}
		}
	}
}
