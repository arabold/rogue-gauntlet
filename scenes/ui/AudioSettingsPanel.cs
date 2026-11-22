using Godot;

/// <summary>Applies live audio changes and saves them independently from run save slots.</summary>
[GlobalClass]
public partial class AudioSettingsPanel : Control
{
	[Signal] public delegate void ClosedEventHandler();
	[Export] public HSlider MasterSlider { get; set; }
	[Export] public HSlider MusicSlider { get; set; }
	[Export] public HSlider EffectsSlider { get; set; }
	[Export] public HSlider UiSlider { get; set; }
	[Export] public Label MasterValue { get; set; }
	[Export] public Label MusicValue { get; set; }
	[Export] public Label EffectsValue { get; set; }
	[Export] public Label UiValue { get; set; }
	[Export] public CheckButton MonoToggle { get; set; }
	[Export] public Label StatusLabel { get; set; }
	[Export] public Button CloseButton { get; set; }

	private bool _initializing;
	private bool _dirty;
	private bool _valid;

	public override void _Ready()
	{
		if (MasterSlider == null || MusicSlider == null || EffectsSlider == null || UiSlider == null
			|| MasterValue == null || MusicValue == null || EffectsValue == null || UiValue == null
			|| MonoToggle == null || StatusLabel == null || CloseButton == null)
		{
			GD.PushError("AudioSettingsPanel requires all sliders, readouts, MonoToggle, StatusLabel and CloseButton.");
			return;
		}

		_valid = true;
		BindSlider(MasterSlider, MasterValue, AudioChannel.Master);
		BindSlider(MusicSlider, MusicValue, AudioChannel.Music);
		BindSlider(EffectsSlider, EffectsValue, AudioChannel.Effects);
		BindSlider(UiSlider, UiValue, AudioChannel.UI);
		MonoToggle.Toggled += OnMonoToggled;
		CloseButton.Pressed += Close;
		StatusLabel.Text = "Changes apply immediately and save automatically.";
		Visible = false;
	}

	/// <summary>Refreshes from the live buses without emitting change notifications.</summary>
	public void Open()
	{
		if (!_valid) return;
		_initializing = true;
		SetSlider(MasterSlider, MasterValue, AudioChannel.Master);
		SetSlider(MusicSlider, MusicValue, AudioChannel.Music);
		SetSlider(EffectsSlider, EffectsValue, AudioChannel.Effects);
		SetSlider(UiSlider, UiValue, AudioChannel.UI);
		MonoToggle.ButtonPressed = AudioPreferences.ReadMono();
		_initializing = false;
		Visible = true;
		MasterSlider.GrabFocus();
	}

	/// <summary>Saves pending slider changes and returns to the owning menu.</summary>
	public void Close()
	{
		SaveIfDirty();
		Visible = false;
		EmitSignalClosed();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (Visible && (@event.IsActionReleased("ui_cancel") || @event.IsActionReleased("pause")))
		{
			Close();
			GetViewport().SetInputAsHandled();
		}
	}

	public override void _ExitTree()
	{
		SaveIfDirty();
		base._ExitTree();
	}

	private void BindSlider(HSlider slider, Label valueLabel, AudioChannel channel)
	{
		slider.ValueChanged += value => OnGainChanged(value, valueLabel, channel);
		slider.DragEnded += changed => { if (changed) SaveIfDirty(); };
		slider.FocusExited += SaveIfDirty;
	}

	private void OnGainChanged(double value, Label valueLabel, AudioChannel channel)
	{
		float gain = Mathf.Clamp((float)value, 0f, 1f);
		UpdateReadout(valueLabel, gain);
		if (_initializing) return;

		AudioPreferences.SetGain(channel, gain);
		_dirty = true;
		StatusLabel.Text = "Unsaved changes.";
	}

	private void OnMonoToggled(bool enabled)
	{
		if (_initializing) return;

		AudioPreferences.SetMono(enabled);
		_dirty = true;
		SaveIfDirty();
	}

	private void SetSlider(HSlider slider, Label valueLabel, AudioChannel channel)
	{
		float gain = AudioPreferences.ReadGain(channel);
		slider.Value = gain;
		UpdateReadout(valueLabel, gain);
	}

	private static void UpdateReadout(Label valueLabel, float gain) => valueLabel.Text = $"{Mathf.RoundToInt(gain * 100f)}%";

	private void SaveIfDirty()
	{
		if (!_dirty) return;

		Error error = AudioPreferences.Save();
		StatusLabel.Text = error == Error.Ok
			? "Audio settings saved."
			: "Could not save audio settings; current changes remain active.";
		_dirty = error != Error.Ok;
	}
}