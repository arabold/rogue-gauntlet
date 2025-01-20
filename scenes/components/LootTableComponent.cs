using System.Linq;
using Godot;

public partial class LootTableComponent : Node
{
    [Export] public HealthComponent HealthComponent { get; set; }

    public LootTableItem[] Items { get; private set; } = [];

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
        if (Items.Length == 0)
        {
            return null;
        }

        var weights = Items.Select(i => i.Weight).ToArray();
        var random = new RandomNumberGenerator();
        var item = Items[random.RandWeighted(weights)];
        return item;
    }
}