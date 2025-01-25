using Godot;

public partial class ItemSlotPanel : PanelContainer
{
    public Texture Texture;

    public override void _Ready()
    {
        Texture = GetNode<TextureRect>("TextureRect").Texture;
    }

    public void SetItem(InventoryItemSlot slot)
    {
        Texture = slot.Item.Icon;
    }
}
