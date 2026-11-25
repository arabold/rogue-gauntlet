using Godot;
using Godot.Collections;

[Tool] // Exported by the tool CharacterClass script: a tool script's exports must be tool types in the editor.
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

	/// <summary>Lets an inventory transaction publish the quantity change with the rest of its commit.</summary>
	internal void SetQuantityWithoutNotification(int quantity) => _quantity = quantity;

	public InventoryItemSlot CreateRuntimeCopy()
	{
		return new InventoryItemSlot
		{
			Item = ItemFactory.CopyDefinition(Item),
			Quantity = Quantity
		};
	}
}