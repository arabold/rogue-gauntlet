using System.Linq;
using Godot;

public partial class LootTableComponent : Node
{
	/// <summary>
	/// The chance of dropping loot from this table. Kept on the component (not the
	/// table) so the same shared <see cref="LootTable"/> can drop at different rates
	/// for different spawners.
	/// </summary>
	[Export] public float DropChance { get; private set; } = 1.0f;
	[Export] public Level Level { get; set; }
	[Export] public PackedScene LootableItemScene { get; private set; }
	/// <summary>The shared, weighted pool this spawner can drop from.</summary>
	[Export] public LootTable Table { get; private set; }

    private bool _isDropped = false;

    private LootTableItem[] Items => Table?.Items ?? [];

    public void DropLoot()
    {
        if (_isDropped)
        {
            return;
        }

        if (GD.Randf() <= DropChance && Items.Length > 0)
        {
            var selectedItem = PickItem();
            GD.Print($"Dropping {selectedItem.Quantity}x {selectedItem.Item.Name}");

			if (LootableItemScene == null)
			{
				GD.PrintErr($"{Name} has no lootable item scene assigned.");
				_isDropped = true;
				return;
			}

			var lootableItem = LootableItemScene.Instantiate<LootableItem>();
			lootableItem.Item = selectedItem.Item;
			lootableItem.Quantity = selectedItem.Quantity;

			Level ??= this.GetAncestorOrNull<Level>();
			Level.AddWorldNode(lootableItem, GetOwner<Node3D>().GlobalPosition);
        }
        else
        {
            GD.Print("No loot dropped");
        }

        _isDropped = true;
    }

    private LootTableItem PickItem()
    {
        if (Items.Length == 0)
        {
            return null;
        }

        var weights = Items.Select(i => i.Weight).ToArray();
        var random = new RandomNumberGenerator();
        random.Seed = GD.Randi();
        var item = Items[random.RandWeighted(weights)];
        return item;
    }
}
