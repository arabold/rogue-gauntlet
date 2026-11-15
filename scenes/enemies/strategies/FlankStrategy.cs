using Godot;

/// <summary>
/// Close in from a bearing behind the target, for any monster: an optional <see cref="EnemyCombatProfile.Flank"/> that
/// reshapes what its movement strategy decides. Whenever that strategy would close in from farther than
/// <see cref="CommitDistance"/> while the monster stands more than <see cref="Tolerance"/> degrees off its ring bearing
/// (spread over <see cref="Arc"/> behind the target's facing, <see cref="AttackSlotsComponent.GetRingBearing"/>), the monster
/// first circles to that bearing at <see cref="Distance"/> without attacking. Once in position, closer than
/// <see cref="CommitDistance"/> or after <see cref="MaximumDuration"/> of flanking, it commits and closes in until its
/// movement strategy stops closing in, such as for a retreat or a wait for an attack slot. A narrow arc sends a pack to
/// the target's back (rats); 360 spreads it all around, so it closes in from every side at once (minions). Shared and
/// stateless: each monster's episode lives in its <see cref="FlankRuntime"/>.
/// </summary>
[Tool]
[GlobalClass]
public partial class FlankStrategy : Resource
{
	/// <summary>Degrees behind the target that flank bearings spread over; 360 surrounds it.</summary>
	[Export(PropertyHint.Range, "0,360,5,suffix:°")] public float Arc { get; set; } = 90;

	/// <summary>Distance of the flank point from the target; keep it outside the primary action's range.</summary>
	[Export(PropertyHint.Range, "0.5,10,0.1,suffix:m")] public float Distance { get; set; } = 3;

	/// <summary>Closer than this, the monster closes in from wherever it stands.</summary>
	[Export(PropertyHint.Range, "0,10,0.1,suffix:m")] public float CommitDistance { get; set; } = 2;

	/// <summary>Degrees off its flank bearing that still count as in position.</summary>
	[Export(PropertyHint.Range, "5,90,5,suffix:°")] public float Tolerance { get; set; } = 35;

	/// <summary>Most degrees the monster swings around the target per flank point, so it circles instead of crossing.</summary>
	[Export(PropertyHint.Range, "10,180,5,suffix:°")] public float OrbitStep { get; set; } = 60;

	/// <summary>Seconds of flanking before the monster closes in anyway, against a target that keeps turning to face it.</summary>
	[Export(PropertyHint.Range, "0,10,0.1,suffix:s")] public float MaximumDuration { get; set; } = 2;

	/// <summary>One monster's flank episodes under these settings.</summary>
	public FlankRuntime CreateRuntime() => new(this);

	public void Validate(MonsterValidationContext context, EnemyCombatProfile combat)
	{
		context.Check(combat.PrimaryOffense?.ReservedSlot != null,
			"Flank: the primary action must take an attack slot, whose ring sets the flank bearings.");
		context.Check(MonsterValidationContext.IsNonnegative(CommitDistance) && Distance > CommitDistance
			&& MonsterValidationContext.IsNonnegative(MaximumDuration) && combat.IsOutsidePrimaryReach(Distance),
			"Flank: the flank point must lie beyond the commit distance and the primary action's range.");
	}
}
