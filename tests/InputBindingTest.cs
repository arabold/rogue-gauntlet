namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using System.Collections.Generic;
using System.Linq;
using static GdUnit4.Assertions;

/// <summary>The project's Input Map gives every in-play action its own keyboard key and gamepad input.</summary>
[TestSuite, RequireGodotRuntime]
public class InputBindingTest
{
	/// <summary>Actions read while the run plays; menu actions may reuse their buttons because menus pause the run.</summary>
	private static readonly string[] GameplayActions =
	[
		"move_up", "move_down", "move_left", "move_right",
		"primary_action", "secondary_action", "offhand_action", "dodge", "interact",
		"quick_item_1", "quick_item_2", "quick_item_3", "quick_item_4",
		"inventory", "pause", "map_overlay",
		"camera_rotate_left", "camera_rotate_right", "camera_zoom_in", "camera_zoom_out",
	];

	[TestCase]
	public void EveryGameplayActionHasItsOwnKeyAndGamepadInput()
	{
		var owners = new Dictionary<string, List<string>>();
		foreach (string action in GameplayActions)
		{
			AssertBool(InputMap.HasAction(action)).OverrideFailureMessage($"{action} is missing from the Input Map").IsTrue();
			var inputs = InputMap.ActionGetEvents(action).Select(Describe).Where(input => input != null).ToList();
			AssertBool(inputs.Any(input => input.StartsWith("key"))).OverrideFailureMessage($"{action} has no key").IsTrue();
			AssertBool(inputs.Any(input => input.StartsWith("joy"))).OverrideFailureMessage($"{action} has no gamepad input").IsTrue();
			foreach (string input in inputs)
			{
				if (!owners.TryGetValue(input, out var actions)) owners[input] = actions = [];
				actions.Add(action);
			}
		}

		var shared = owners.Where(pair => pair.Value.Count > 1).Select(pair => $"{pair.Key}: {string.Join(", ", pair.Value)}");
		AssertArray(shared.ToArray()).IsEmpty();
	}

	private static string Describe(InputEvent input) => input switch
	{
		InputEventKey key => $"key {(key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode)}",
		InputEventJoypadButton button => $"joy button {button.ButtonIndex}",
		InputEventJoypadMotion motion => $"joy axis {motion.Axis} {(motion.AxisValue < 0 ? "-" : "+")}",
		_ => null,
	};
}