using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Runs one of several steps, chosen by their <see cref="FurnishingStep.Weight"/>: layout variants within one room
/// type (a corner bay, a wall bay or a divider) without a room type per variant.
/// </summary>
[Tool, GlobalClass]
public partial class ChoiceStep : FurnishingStep
{
	[Export] public FurnishingStep[] Options { get; set; } = [];

	public override int Furnish(RoomBlueprint room, RandomNumberGenerator rng) =>
		Weighted.Pick(Options, option => option.Weight, rng)?.Furnish(room, rng) ?? 0;

	public override string[] Validate()
	{
		var errors = new List<string>();
		if (string.IsNullOrWhiteSpace(Id)) errors.Add("ChoiceStep: assign a stable ID.");
		if (Options.Length == 0 || Options.Any(option => option == null)) errors.Add($"Step {Id}: assign options.");
		errors.AddRange(Options.Where(option => option != null).SelectMany(option => option.Validate()));
		return errors.ToArray();
	}
}
