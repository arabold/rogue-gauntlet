using Godot;

[Tool]
public partial class EquipmentItemPanel : PanelContainer
{
	[Export]
	public Texture2D PlaceholderTexture
	{
		get => _placeholderTexture;
		set
		{
			_placeholderTexture = value;
			if (_placeholderTextureRect != null)
			{
				_placeholderTextureRect.Texture = _placeholderTexture;
			}
		}
	}
	public Preview Preview;

	private TextureRect _placeholderTextureRect;
	private Texture2D _placeholderTexture;
	private ItemSlotPanel _itemSlotPanel;

	public override void _Ready()
	{
		_placeholderTextureRect = GetNode<TextureRect>("%PlaceholderTextureRect");
		_placeholderTextureRect.Texture = _placeholderTexture;
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
