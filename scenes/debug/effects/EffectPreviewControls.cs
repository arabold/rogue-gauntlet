using Godot;
using System;
using System.Collections.Generic;

/// <summary>Small shared controls for the visual workbenches, including playback and camera instructions.</summary>
public static class EffectPreviewControls
{
	public static OptionButton Choice(IEnumerable<string> values, int selected, Action<int> callback)
	{
		var choice = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		foreach (string value in values) choice.AddItem(value);
		choice.Selected = selected; choice.ItemSelected += i => callback((int)i); return choice;
	}
	public static Control Row(Container parent, string text, Control control, float labelWidth = 100)
	{
		var row = new HBoxContainer(); row.AddChild(new Label { Text = text, CustomMinimumSize = new Vector2(labelWidth, 0) });
		control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(control); parent.AddChild(row); return row;
	}
	public static Button Button(Container parent, string text, Action callback)
	{
		var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
		button.Pressed += callback; parent.AddChild(button); return button;
	}
	public static OptionButton Playback(Container parent, Action<float> callback)
	{
		var speeds = new[] { 0f, .25f, .5f, 1f };
		var choice = Choice(new[] { "Paused", "0.25×", "0.5×", "1×" }, 3, i => callback(speeds[i]));
		Row(parent, "Playback", choice); return choice;
	}
	public static void CameraHelp(Container parent)
	{
		parent.AddChild(new Label { Text = "Right-drag to orbit · wheel zoom\nCamera keys follow game bindings.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
	}
	/// <summary>Add the common bright/dark lighting toggle.</summary>
	public static void DarkLighting(Container parent, WorldEnvironment lighting, DirectionalLight3D keyLight)
	{
		var dark = new CheckBox { Text = "Dark lighting" };
		dark.Toggled += enabled => { lighting.Environment.AmbientLightEnergy = enabled ? .06f : .65f; keyLight.LightEnergy = enabled ? .12f : .9f; };
		parent.AddChild(dark);
	}
}
