using Godot;

/// <summary>A key glyph and a word in the run menu's key row. Clicking it does what the key does.</summary>
public partial class MenuPromptButton : HBoxContainer
{
	[Signal] public delegate void PressedEventHandler(StringName action);

	[Export] public KeyBinding FirstKey { get; set; }
	/// <summary>The dash and last key of a range of actions ("1–4").</summary>
	[Export] public Label Dash { get; set; }
	[Export] public KeyBinding LastKey { get; set; }
	[Export] public Label Label { get; set; }
	[Export] public Color HoverTint { get; set; } = new(1.25f, 1.2f, 1.1f);

	private MenuPrompt _prompt;

	public override void _Ready()
	{
		MouseEntered += () => Modulate = IsClickable ? HoverTint : Colors.White;
		MouseExited += () => Modulate = Colors.White;
	}

	/// <summary>A range has no single thing to click: its keys each mean a different slot.</summary>
	private bool IsClickable => _prompt is { Actions.Length: 1 };

	public void Display(MenuPrompt prompt)
	{
		_prompt = prompt;
		bool isRange = prompt.Actions.Length > 1;
		FirstKey.ActionBinding = prompt.Actions[0];
		Dash.Visible = isRange;
		LastKey.Visible = isRange;
		LastKey.ActionBinding = prompt.Actions[^1];
		Label.Text = prompt.Label;
		MouseDefaultCursorShape = IsClickable ? CursorShape.PointingHand : CursorShape.Arrow;
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (IsClickable && @event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
		{
			EmitSignalPressed(_prompt.Actions[0]);
			AcceptEvent();
		}
	}
}