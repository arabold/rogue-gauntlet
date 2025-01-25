using Godot;
using Godot.Collections;

[GlobalClass]
public partial class InventoryItemSlot : Resource
{
    [Signal] public delegate void ItemChangedEventHandler();

    [Export]
    public Item Item
    {
        get => _item;
        set
        {
            _item = value;
            EmitSignal(SignalName.ItemChanged);
        }
    }

    [Export]
    public int Quantity
    {
        get => _quantity;
        set
        {
            _quantity = value;
            EmitSignal(SignalName.ItemChanged);
        }
    }

    private Item _item;
    private int _quantity = 1;

    public bool IsEmpty => Item == null;
    public bool IsStackable => Item.IsStackable;
}