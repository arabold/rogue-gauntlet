using Godot;
using Godot.Collections;

[GlobalClass]
public partial class InventoryItemSlot : Resource
{
    [Export]
    public Item Item
    {
        get => _item;
        set
        {
            _item = value;
            EmitChanged();
        }
    }

    [Export]
    public int Quantity
    {
        get => _quantity;
        set
        {
            _quantity = value;
            EmitChanged();
        }
    }

    private Item _item;
    private int _quantity = 1;

    public bool IsEmpty => Item == null;
    public bool IsStackable => Item.IsStackable;
}