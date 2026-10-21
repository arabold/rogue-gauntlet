using Godot;

/// <summary>
/// Owns one generation's streams. Never stored on shared strategy Resources;
/// regenerating a floor constructs a new context at the same initial seeds.
/// </summary>
public sealed class LevelGenerationContext
{
	public ulong LevelSeed { get; }
	public RandomNumberGenerator Layout { get; }
	public RandomNumberGenerator Encounters { get; }
	public RandomNumberGenerator Items { get; }
	public RandomNumberGenerator Decoration { get; }

	public LevelGenerationContext(ulong levelSeed, int layoutAttempt = 0)
	{
		LevelSeed = levelSeed;
		Layout = GenerationRandom.Create(levelSeed, GenerationRandom.Layout,
			layoutAttempt == 0 ? "" : $"fit-attempt:{layoutAttempt}");
		Encounters = GenerationRandom.Create(levelSeed, GenerationRandom.Monsters);
		Items = GenerationRandom.Create(levelSeed, GenerationRandom.Items);
		Decoration = GenerationRandom.Create(levelSeed, GenerationRandom.Decoration);
	}
}