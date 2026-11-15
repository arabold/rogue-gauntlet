using Godot;

/// <summary>
/// A full stop for a decision that cannot be undone: a short question, one line naming what is at stake, and two
/// buttons. It covers the screen, takes the focus and reports the answer; the asker acts on it.
/// </summary>
public partial class ConfirmDialog : Control
{
	[Signal] public delegate void ConfirmedEventHandler();
	[Signal] public delegate void CanceledEventHandler();

	[Export] public Label QuestionLabel { get; set; }
	[Export] public Label DetailLabel { get; set; }
	[Export] public Button ConfirmButton { get; set; }
	[Export] public Button CancelButton { get; set; }

	private static readonly StringName Cancel = "ui_cancel";

	public override void _Ready()
	{
		ConfirmButton.Pressed += () => Answer(SignalName.Confirmed);
		CancelButton.Pressed += () => Answer(SignalName.Canceled);
		Visible = false;
	}

	/// <summary>Asks <paramref name="question"/> about <paramref name="detail"/>; the safe answer holds the focus.</summary>
	public void Ask(string question, string detail, string confirm)
	{
		QuestionLabel.Text = question;
		DetailLabel.Text = detail;
		DetailLabel.Visible = !string.IsNullOrEmpty(detail);
		ConfirmButton.Text = confirm;
		Visible = true;
		CancelButton.GrabFocus();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (Visible && @event.IsActionReleased(Cancel))
		{
			GetViewport().SetInputAsHandled();
			Answer(SignalName.Canceled);
		}
	}

	private void Answer(StringName signal)
	{
		Visible = false;
		EmitSignal(signal);
	}
}
