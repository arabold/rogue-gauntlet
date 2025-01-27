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

	[Export]
	public string ActionBinding
	{
		get => _actionBinding;
		set
		{
			_actionBinding = value;
			if (IsNodeReady() && _keyBinding != null)
			{
				_keyBinding.ActionBinding = _actionBinding;
			}
		}
	}

	private string _text = "Text";
	private string _actionBinding = "action_1";
	private Label _label;
	private KeyBinding _keyBinding;

	public override void _Ready()
	{
		_keyBinding = GetNode<KeyBinding>("%KeyBinding");
		if (_actionBinding != null && _actionBinding != "")
		{
			_keyBinding.ActionBinding = _actionBinding;
		}
		else
		{
			_keyBinding.QueueFree();
		}

		_label = GetNode<Label>("%Label");
		_label.Text = _text;
	}
}
