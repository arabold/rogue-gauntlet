using Godot;

[Tool]
public partial class FloatingLabel : Sprite3D
{
	public const int LabelRenderPriority = 20;

	/// <summary>
	/// The text to display.
	/// </summary>
	[Export]
	public string Text
	{
		get;
		set
		{
			field = value;
			if (IsNodeReady())
			{
				_label.Text = value;
			}
		}
	} = "Text";

	[Export]
	public string ActionBinding
	{
		get;
		set
		{
			field = value;
			if (IsNodeReady())
			{
				_keyBinding.ActionBinding = value;
				_keyBinding.Visible = !string.IsNullOrEmpty(value);
			}
		}
	}

	private Label _label;
	private KeyBinding _keyBinding;

	public override void _EnterTree()
	{
		base._EnterTree();
		RenderPriority = LabelRenderPriority;
		NoDepthTest = true;
		var subViewport = GetNode<SubViewport>("SubViewport");
		Texture = subViewport.GetTexture();
	}

	public override void _ExitTree()
	{
		base._ExitTree();
		Texture = null;
	}

	public override void _Ready()
	{
		Visible = false;
		_label = GetNode<Label>("%Label");
		_label.Text = Text;

		_keyBinding = GetNode<KeyBinding>("%KeyBinding");
		_keyBinding.ActionBinding = ActionBinding;
		_keyBinding.Visible = !string.IsNullOrEmpty(ActionBinding);
	}
}
