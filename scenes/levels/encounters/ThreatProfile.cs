using Godot;

/// <summary>
/// How a monster's <see cref="MonsterDefinition.ThreatLevel"/> becomes the experience it pays and what it costs a floor's
/// encounter budget. The values come from the balance model (<c>scripts/balance/params.py</c>) and are written by
/// <c>simulate.py derive</c>; change them there, so the model and the game stay in step.
/// </summary>
[Tool]
[GlobalClass]
public partial class ThreatProfile : Resource
{
	[ExportGroup("Experience")]
	/// <summary>XP a monster at threat level 1 pays.</summary>
	[Export(PropertyHint.Range, "0,1000,0.1,or_greater")] public float StandardXp { get; set; } = 10;
	/// <summary>XP factor per threat level. Below <see cref="CostGrowth"/>, so tougher monsters pay more but not exponentially more.</summary>
	[Export(PropertyHint.Range, "1,2,0.001")] public float XpGrowth { get; set; } = 1.15f;
	/// <summary>Extra XP factor for a gatekeeper (<see cref="MonsterArchetype.Boss"/>), the bump each realm ends with.</summary>
	[Export(PropertyHint.Range, "1,10,0.1")] public float GatekeeperXpMultiplier { get; set; } = 2;

	[ExportGroup("Encounter budget")]
	/// <summary>Factor by which a monster's fight cost falls for each depth the expected hero has grown past its threat level.</summary>
	[Export(PropertyHint.Range, "1,2,0.0001")] public float CostGrowth { get; set; } = 1.3f;
	/// <summary>Standard fights a floor spends at depth 1; the budget grows linearly to <see cref="LastFloorBudget"/>.</summary>
	[Export(PropertyHint.Range, "0,100,0.1")] public float FirstFloorBudget { get; set; } = 8;
	/// <summary>Standard fights a floor spends at <see cref="LastDepth"/> and below.</summary>
	[Export(PropertyHint.Range, "0,100,0.1")] public float LastFloorBudget { get; set; } = 11;
	[Export(PropertyHint.Range, "2,100")] public int LastDepth { get; set; } = 24;
	/// <summary>Share of the budget a gatekeeper floor spends on ordinary monsters, besides its gatekeeper.</summary>
	[Export(PropertyHint.Range, "0,1,0.01")] public float GatekeeperFloorShare { get; set; } = 0.6f;

	/// <summary>Experience for killing <paramref name="monster"/>.</summary>
	public int Xp(MonsterDefinition monster)
	{
		float bonus = monster.Archetype == MonsterArchetype.Boss ? GatekeeperXpMultiplier : 1;
		return Mathf.RoundToInt(StandardXp * Mathf.Pow(XpGrowth, monster.ThreatLevel - 1) * bonus);
	}

	/// <summary>
	/// Budget a monster spends on a floor at <paramref name="depth"/>, in standard fights: one on the depth of its threat
	/// level, less on deeper floors where the expected hero is stronger, more on shallower ones.
	/// </summary>
	public float Cost(float threatLevel, uint depth) => Mathf.Pow(CostGrowth, threatLevel - depth);

	/// <summary>
	/// How much more a pack costs than its members fought alone. The hero kills members one by one while the rest keep
	/// attacking, so the k-th to fall attacks through k kills: (N + 1) / 2 for N members (the balance model's
	/// <c>group_factor</c>).
	/// </summary>
	public static float GroupFactor(int size) => (size + 1) / 2f;

	/// <summary>Budget a pack of monsters with these threat levels spends on a floor at <paramref name="depth"/>.</summary>
	public float PackCost(System.Collections.Generic.IReadOnlyCollection<float> threatLevels, uint depth)
	{
		float sum = 0;
		foreach (float threat in threatLevels) sum += Cost(threat, depth);
		return GroupFactor(threatLevels.Count) * sum;
	}

	/// <summary>Standard fights a floor at <paramref name="depth"/> spends on ordinary monsters.</summary>
	public float FloorBudget(uint depth, bool gatekeeperFloor)
	{
		float budget = Mathf.Lerp(FirstFloorBudget, LastFloorBudget, Mathf.Clamp((depth - 1f) / (LastDepth - 1), 0, 1));
		return gatekeeperFloor ? budget * GatekeeperFloorShare : budget;
	}
}
