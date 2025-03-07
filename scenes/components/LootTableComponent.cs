using System.Linq;
using Godot;

public partial class LootTableComponent : Node
{
    /// <summary>
    /// The chance of dropping loot from this table.
    /// </summary>
    [Export] public float DropChance { get; private set; } = 1.0f;
    [Export] public LootTableItem[] Items { get; private set; } = [];

    private bool _isDropped = false;

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

            // TODO: Avoid hardcoding the path to the lootable item scene
            var scene = GD.Load<PackedScene>("res://scenes/items/lootable_item.tscn");
            var lootableItem = scene.Instantiate<LootableItem>();
            lootableItem.Item = selectedItem.Item;
            lootableItem.Quantity = selectedItem.Quantity;
            lootableItem.GlobalPosition = GetOwner<Node3D>().GlobalPosition;

            GameManager.Instance.Level.AddChild(lootableItem);
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