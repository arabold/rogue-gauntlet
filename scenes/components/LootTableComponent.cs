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

        // Draw the whole drop from the run's seeded loot sequence; fall back to an unseeded
        // RNG outside an active session (editor/tests).
        RandomNumberGenerator rng = GameSession.Instance?.CreateLootRng();
        if (rng == null)
        {
            rng = new RandomNumberGenerator();
            rng.Randomize();
        }

        if (rng.Randf() <= DropChance && Items.Length > 0)
        {
            var selectedItem = PickItem(rng);

            if (LootableItemScene == null)
            {
                GD.PrintErr($"{Name} has no lootable item scene assigned.");
                _isDropped = true;
                return;
            }

            // Equipables drop as rolled instances (rarity + affixes); other items pass through.
            uint depth = GameSession.Instance?.ActiveDungeonDepth ?? 1;
            Item item = LootRoller.Roll(selectedItem.Item, depth, rng);
            GD.Print($"Dropping {selectedItem.Quantity}x {item.Name}");

            var lootableItem = LootableItemScene.Instantiate<LootableItem>();
            lootableItem.Item = item;
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

    private LootTableItem PickItem(RandomNumberGenerator rng)
    {
        if (Items.Length == 0)
        {
            return null;
        }

        var weights = Items.Select(i => i.Weight).ToArray();
        return Items[rng.RandWeighted(weights)];
    }
}
