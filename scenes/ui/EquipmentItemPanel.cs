using Godot;
using System;

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
			if (_bgTextureRect != null)
			{
				_bgTextureRect.Texture = _placeholderTexture;
			}
		}
	}
	public Preview Preview;

	private TextureRect _bgTextureRect;
	private Texture2D _placeholderTexture;

	public override void _Ready()
	{
		_bgTextureRect = GetNode<TextureRect>("%BgTextureRect");
		_bgTextureRect.Texture = _placeholderTexture;

		if (!Engine.IsEditorHint())
		{
			Preview = GetNode<Preview>("%Preview");
		}
	}

	public void SetItem(Item item)
	{
		Preview?.SetScene(item?.Scene);
		_bgTextureRect.Visible = item == null;
	}
}
