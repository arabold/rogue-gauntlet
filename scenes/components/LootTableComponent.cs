using System.Linq;
using Godot;

public partial class LootTableComponent : Node
{
    [Export] public HealthComponent HealthComponent { get; set; }

    // [Export] public LootTableItem[] Items { get; set; } = new LootTableItem[0];
    public LootTableItem[] Items { get; private set; } = new LootTableItem[0];

    private bool _isDropped = false;

    public override void _Ready()
    {
        base._Ready();

        Items = GetChildren().OfType<LootTableItem>().ToArray();
        GD.Print($"Loot table initialized with {Items.Length} item(s)");

        HealthComponent.Died += DropLoot;
    }

    public void DropLoot()
    {
        if (_isDropped)
        {
            return;
        }

        var selectedItem = PickItem();
        if (selectedItem != null)
        {
            var instance = selectedItem.Scene?.Instantiate<Node3D>();
            if (instance != null)
            {
                GameManager.Instance.Level.AddChild(instance);
                instance.GlobalPosition = GetOwner<Node3D>().GlobalPosition;
            }
        }

        _isDropped = true;
    }

    private LootTableItem PickItem()
    {
        float totalWeight = 0;
        foreach (var item in Items)
        {
            totalWeight += item.Weight;
        }

        float randomValue = (float)GD.RandRange(0, totalWeight);
        float cumulativeWeight = 0;

        foreach (var item in Items)
        {
            cumulativeWeight += item.Weight;
            if (randomValue < cumulativeWeight)
            {
                return item;
            }
        }

        return null; // This should never happen if weights are properly set
    }
}