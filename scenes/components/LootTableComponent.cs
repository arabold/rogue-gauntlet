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
	/// <summary>Minimum number of separate drops rolled from <see cref="Table"/> per event.</summary>
	[Export] public int DropCountMin { get; private set; } = 1;
	/// <summary>Maximum number of separate drops rolled from <see cref="Table"/> per event.</summary>
	[Export] public int DropCountMax { get; private set; } = 1;

    private bool _isDropped = false;

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

        uint depth = GameSession.Instance?.ActiveDungeonDepth ?? 1;
        int dropCount = 0;

        if (rng.Randf() <= DropChance && Table != null)
        {
            int rolls = rng.RandiRange(Mathf.Min(DropCountMin, DropCountMax), Mathf.Max(DropCountMin, DropCountMax));
            for (int i = 0; i < rolls; i++)
            {
                LootTableItem selectedItem = Table.PickEntry(depth, rng);
                if (selectedItem == null)
                {
                    continue;
                }

                if (LootableItemScene == null)
                {
                    GD.PrintErr($"{Name} has no lootable item scene assigned.");
                    continue;
                }

                // Equipables drop as rolled instances (rarity + affixes); other items pass through.
                Item item = LootRoller.Roll(selectedItem.Item, depth, rng);
                int quantity = selectedItem.QuantityAt(depth);
                GD.Print($"Dropping {quantity}x {item.Name}");

                var lootableItem = LootableItemScene.Instantiate<LootableItem>();
                lootableItem.Item = item;
                lootableItem.Quantity = quantity;

                Level ??= this.GetAncestorOrNull<Level>();
                Level.AddWorldNode(lootableItem, GetOwner<Node3D>().GlobalPosition);
                dropCount++;
            }
        }

        if (dropCount == 0)
        {
            GD.Print("No loot dropped");
        }

        _isDropped = true;
    }
}
