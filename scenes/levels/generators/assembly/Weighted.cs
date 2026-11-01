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

	/// <summary>
	/// Weighted choice balanced over uses: each option scores (uses + a random fraction) / weight and the lowest wins.
	/// </summary>
	public static T PickBalanced<T>(IReadOnlyList<T> options, Func<T, int> getUses, RandomNumberGenerator rng) where T : Placeable
	{
		if (options == null) return null;
		T best = null;
		double bestScore = double.MaxValue;
		foreach (var option in options)
		{
			double fraction = rng.Randf();
			if (option == null || option.Weight <= 0) continue;
			double score = (getUses(option) + fraction) / option.Weight;
			if (score >= bestScore) continue;
			bestScore = score;
			best = option;
		}
		return best;
	}

	/// <summary>Prefers variants not yet heavily placed; ties break by the seeded draw.</summary>
	public static int PickVariant(Prop prop, Func<int, int> getUses, RandomNumberGenerator rng)
	{
		double roll = rng.Randf();
		int count = prop.VariantCount;
		if (count <= 1) return 0;
		Span<double> weights = stackalloc double[count];
		double sum = 0.0;
		for (int i = 0; i < count; i++)
		{
			weights[i] = 1.0 / (1 + 4 * getUses(i));
			sum += weights[i];
		}
		roll *= sum;
		for (int variant = 0; variant < count; variant++)
		{
			roll -= weights[variant];
			if (roll < 0) return variant;
		}
		return count - 1;
	}
}