using Godot;

[Tool]
public partial class FloatingLabel : Sprite3D
{
	/// <summary>
	/// The text to display.
	/// </summary>
	[Export]
	public string Text
	{
		get => _text;
		set
		{
			_text = value;
			if (IsNodeReady() && _label != null)
			{
				_label.Text = _text;
			}
		}
	}

	private string _text = "Text";
	private Label _label;
	private SubViewport _subViewport;

	public override void _Ready()
	{
		_label = GetNode<Label>("%Label");
		_label.Text = _text;
	}
}
