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
			if (field == value) return;
			field = value;
			if (IsNodeReady())
			{
				_label.Text = value;
				QueueLayout();
			}
		}
	} = "Text";

	[Export]
	public string ActionBinding
	{
		get;
		set
		{
			if (field == value) return;
			field = value;
			if (IsNodeReady())
			{
				_keyBinding.ActionBinding = value;
				RefreshAvailability();
				QueueLayout();
			}
		}
	}

	/// <summary>Unavailable requirements are red and do not advertise an actionable input.</summary>
	public bool IsAvailable
	{
		get;
		set
		{
			if (field == value) return;
			field = value;
			if (IsNodeReady()) { RefreshAvailability(); QueueLayout(); }
		}
	} = true;
	private SubViewport _viewport;
	private HBoxContainer _content;
	private bool _layoutQueued;
	private Label _label;
	private KeyBinding _keyBinding;

	public override void _EnterTree()
	{
		base._EnterTree();
		// The editor keeps the scene's own viewport texture. Runtime changes made there would be saved as overrides into
		// every scene that instances this label, with a viewport path that only resolves inside this scene.
		if (Engine.IsEditorHint()) return;
		RenderPriority = LabelRenderPriority;
		NoDepthTest = true;
		var subViewport = GetNode<SubViewport>("SubViewport");
		Texture = subViewport.GetTexture();
	}

	public override void _ExitTree()
	{
		base._ExitTree();
		if (!Engine.IsEditorHint()) Texture = null;
	}

	public override void _Ready()
	{
		if (!Engine.IsEditorHint()) Visible = false;
		_label = GetNode<Label>("%Label");
		_label.Text = Text;

		_keyBinding = GetNode<KeyBinding>("%KeyBinding");
		_keyBinding.ActionBinding = ActionBinding;
		_viewport = GetNode<SubViewport>("SubViewport");
		_content = GetNode<HBoxContainer>("SubViewport/HFlowContainer");
		RefreshAvailability();
		QueueLayout();
	}

	private void RefreshAvailability()
	{
		_keyBinding.Visible = IsAvailable && !string.IsNullOrEmpty(ActionBinding);
		_keyBinding.IsDisabled = !IsAvailable;
		_label.Modulate = IsAvailable ? Colors.White : new Color(1, 0.35f, 0.35f);
	}
	private void QueueLayout()
	{
		if (_layoutQueued) return;
		_layoutQueued = true;
		Callable.From(UpdateLayout).CallDeferred();
	}
	private void UpdateLayout()
	{
		_layoutQueued = false;
		if (!IsInsideTree() || _content == null) return;
		var minimum = _content.GetCombinedMinimumSize();
		_viewport.Size = new Vector2I(Mathf.Max(64, Mathf.CeilToInt(minimum.X) + 32), Mathf.Max(64, Mathf.CeilToInt(minimum.Y) + 16));
	}
}