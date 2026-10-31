using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Seeded weighted choices for room assembly. Every call draws exactly once, keeping streams stable.</summary>
public static class Weighted
{
	public static T Pick<T>(IReadOnlyList<T> options, Func<T, float> weight, RandomNumberGenerator rng) where T : class
	{
		double roll = rng.Randf();
		var valid = options.Where(option => option != null && weight(option) > 0).ToArray();
		if (valid.Length == 0) return null;
		roll *= valid.Sum(option => weight(option));
		foreach (var option in valid)
		{
			roll -= weight(option);
			if (roll < 0) return option;
		}
		return valid[^1];
	}

	/// <summary>Weighted choice from an item-name map, as authored on surface Resources.</summary>
	public static string Pick(Godot.Collections.Dictionary<string, float> items, RandomNumberGenerator rng) =>
		Pick(items.Keys.ToArray(), key => items[key], rng);
}