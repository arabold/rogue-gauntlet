using Godot;
using System;
using System.Text;

/// <summary>
/// Derives repeatable, independent streams without touching Godot's global RNG.
/// Source keys make lazy rewards independent of the order sources are activated.
/// Output stability is required within a build, not across generator changes.
/// </summary>
public static class GenerationRandom
{
	public const string Layout = "layout";
	public const string Monsters = "monsters";
	public const string Items = "items";
	public const string Arrangements = "arrangements";
	public const string Decoration = "decoration";
	public const string Loot = "loot";
	/// <summary>Rolls made during play without a stable source (enchanting, summons), keyed by a saved counter.</summary>
	public const string GameplayLoot = "gameplay-loot";

	/// <summary>Hashes length-delimited inputs using FNV-1a, independently of process hash seeds.</summary>
	public static ulong DeriveSeed(ulong levelSeed, string domain, string sourceId = "")
	{
		ArgumentNullException.ThrowIfNull(domain);
		ArgumentNullException.ThrowIfNull(sourceId);
		ulong hash = 14695981039346656037UL;
		AddNumber(ref hash, levelSeed);
		foreach (string part in new[] { domain, sourceId })
		{
			byte[] bytes = Encoding.UTF8.GetBytes(part);
			AddNumber(ref hash, (ulong)bytes.Length);
			foreach (byte value in bytes)
			{
				hash = unchecked((hash ^ value) * 1099511628211UL);
			}
		}
		return hash;
	}

	public static RandomNumberGenerator Create(ulong levelSeed, string domain, string sourceId = "")
	{
		return new RandomNumberGenerator { Seed = DeriveSeed(levelSeed, domain, sourceId) };
	}

	private static void AddNumber(ref ulong hash, ulong value)
	{
		for (int i = 0; i < 8; i++)
		{
			hash = unchecked((hash ^ (byte)value) * 1099511628211UL);
			value >>= 8;
		}
	}
}