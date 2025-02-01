using Godot;

[Tool]
public partial class EquipmentItemPanel : PanelContainer
{
	[Export]
	public Texture2D PlaceholderTexture
	{
		get;
		set
		{
			field = value;
			if (IsNodeReady())
			{
				_placeholderTextureRect.Texture = value;
			}
		}
	}
	public Preview Preview;

	private TextureRect _placeholderTextureRect;
	private ItemSlotPanel _itemSlotPanel;

	public override void _Ready()
	{
		_placeholderTextureRect = GetNode<TextureRect>("%PlaceholderTextureRect");
		_placeholderTextureRect.Texture = PlaceholderTexture;

		if (!Engine.IsEditorHint())
		{
			_itemSlotPanel = GetNode<ItemSlotPanel>("%ItemSlotPanel");
		}
	}

	public void SetItem(Item item)
	{
		_placeholderTextureRect.Visible = item == null;
		_itemSlotPanel.SetItem(new InventoryItemSlot { Item = item }, false);
	}
}
