using Godot;
using Godot.Collections;

[GlobalClass]
public partial class InventoryItemSlot : Resource
{
    [Export] public Item Item { get; set; }
    [Export] public int Quantity { get; set; } = 1;

    public bool IsEmpty => Item == null;
    public bool IsStackable => Item.IsStackable;
}