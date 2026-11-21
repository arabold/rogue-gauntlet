using Godot;

[Tool]
[GlobalClass]
public partial class FloatingLabel : Sprite3D
{
	/// <summary>Draw order for world labels (loot names, encounter titles), which stay under the fog.</summary>
	public const int LabelRenderPriority = 20;
	/// <summary>Draw order for this interaction prompt: above the fog and the door/chest x-rays. Prompts only show for
	/// an interactive in range and line of sight, so drawing over fog reveals nothing.</summary>
	public const int PromptRenderPriority = FloorDiscovery.AboveFogRenderPriority + 1;

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
				LabelNode.Text = value;
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
				KeyBindingNode.ActionBinding = value;
				RefreshAvailability();
				QueueLayout();
			}
		}
	}

	/// <summary>Unavailable requirements are red and do not advertise an actionable input.</summary>
	[Export]
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
	private bool _layoutQueued;
	private SubViewport _viewport;
	private HBoxContainer _content;
	private Label _label;
	private KeyBinding _keyBinding;

	// The label's own scene parts, looked up on first use instead of in _Ready: when the editor reloads C# scripts it
	// recreates this object without running _Ready again and restores the exports through their setters.
	private SubViewport ContentViewport => _viewport ??= GetNode<SubViewport>("SubViewport");
	private HBoxContainer Content => _content ??= GetNode<HBoxContainer>("SubViewport/HFlowContainer");
	private Label LabelNode => _label ??= GetNode<Label>("%Label");
	private KeyBinding KeyBindingNode => _keyBinding ??= GetNode<KeyBinding>("%KeyBinding");

	public override void _EnterTree()
	{
		base._EnterTree();
		// The editor keeps the scene's own viewport texture. Runtime changes made there would be saved as overrides into
		// every scene that instances this label, with a viewport path that only resolves inside this scene.
		if (Engine.IsEditorHint()) return;
		RenderPriority = PromptRenderPriority;
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
		LabelNode.Text = Text;
		KeyBindingNode.ActionBinding = ActionBinding;
		RefreshAvailability();
		QueueLayout();
	}

	private void RefreshAvailability()
	{
		KeyBindingNode.Visible = IsAvailable && !string.IsNullOrEmpty(ActionBinding);
		KeyBindingNode.IsDisabled = !IsAvailable;
		LabelNode.Modulate = IsAvailable ? Colors.White : new Color(1, 0.35f, 0.35f);
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
		if (!IsInsideTree()) return;
		var minimum = Content.GetCombinedMinimumSize();
		ContentViewport.Size = new Vector2I(Mathf.Max(64, Mathf.CeilToInt(minimum.X) + 32), Mathf.Max(64, Mathf.CeilToInt(minimum.Y) + 16));
	}
}