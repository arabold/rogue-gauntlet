using System;
using Godot;

[GlobalClass]
public abstract partial class MobFactory : Resource
{
	public abstract PackedScene CreateEnemy(uint dungeonDepth, RandomNumberGenerator rng);
	/// <summary>Default encounter sizing for factories without an authored budget.</summary>
	public virtual Godot.Collections.Array<PackedScene> CreateEncounter(uint depth, RandomNumberGenerator rng)
	{
		var result = new Godot.Collections.Array<PackedScene>();
		uint count = 3 + depth % 5 + (uint)rng.RandiRange(0, 2);
		for (int i = 0; i < count; i++)
		{
			var scene = CreateEnemy(depth, rng);
			if (scene != null) result.Add(scene);
		}
		return result;
	}
	/// <summary>The floor's special encounters, or null when it has none. Decided before generation places anything.</summary>
	public virtual EncounterPlan PlanEncounters(uint depth) => null;
	public virtual void Reset() { }
}