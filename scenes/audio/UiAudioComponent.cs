using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Sends UI activation cues before the owning screen handles navigation or scene changes.
/// </summary>
[GlobalClass]
public partial class UiAudioComponent : Node
{
	[Export] public Control ButtonRoot { get; set; }
	[Export] public SoundCueProfile PressedCue { get; set; }

	private readonly Dictionary<BaseButton, Action> _pressedHandlers = [];

	public override void _Ready()
	{
		if (ButtonRoot == null)
		{
			GD.PushError("UiAudioComponent requires an explicitly wired ButtonRoot.");
			return;
		}

		BindButtons(ButtonRoot);
	}

	public override void _ExitTree()
	{
		foreach (KeyValuePair<BaseButton, Action> binding in _pressedHandlers)
		{
			if (GodotObject.IsInstanceValid(binding.Key))
			{
				binding.Key.Pressed -= binding.Value;
			}
		}
		_pressedHandlers.Clear();
		base._ExitTree();
	}

	private void BindButtons(Node node)
	{
		if (node is BaseButton button && !_pressedHandlers.ContainsKey(button))
		{
			Action handler = () => SignalBus.EmitUiSoundEmitted(PressedCue, button);
			_pressedHandlers.Add(button, handler);
			button.Pressed += handler;
		}

		foreach (Node child in node.GetChildren())
		{
			BindButtons(child);
		}
	}
}
