namespace RogueGauntlet.Tests;

using System.Linq;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The Forsaken Warden's reward bridges into the next realm: every drop is a never-cursed tier-2 weapon, through its own
/// loot table and the gatekeeper creation policy wired on its loot component. The gatekeeper ladder never rolls below
/// Magic and introduces Rare at the ordinary ladder's depth, so the depth-3 warden drops Magic and deeper gatekeepers
/// grow their Rare share.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class GatekeeperRewardTest
{
	private const uint WardenDepth = 3;

	[TestCase]
	public void TheWardenDropsAMagicTierTwoWeaponThatIsNeverCursed()
	{
		var definition = GD.Load<MonsterDefinition>("res://scenes/enemies/forsaken_warden/forsaken_warden_definition.tres");
		ItemCreationPolicy policy = definition.Loot?.CreationPolicy;
		AssertObject(policy).IsNotNull();

		using var rng = GenerationRandom.Create(42, "warden-reward");
		for (int i = 0; i < 200; i++)
		{
			var weapon = ItemFactory.Create(definition.Loot.Table.PickEntry(WardenDepth, rng).Item, policy, WardenDepth, rng) as Weapon;
			AssertObject(weapon).IsNotNull();
			AssertInt(weapon.Tier).IsEqual(2);
			AssertBool(weapon.Quality == ItemQuality.Magic).IsTrue();
			AssertBool(weapon.IsCursed).IsFalse();
		}
	}

	[TestCase]
	public void GatekeeperRareSharesFollowTheOrdinaryLadderIntroduction()
	{
		var gatekeeper = GD.Load<QualityLadder>("res://scenes/items/quality/quality_ladder_gatekeeper.tres");
		var ordinary = GD.Load<QualityLadder>("res://scenes/items/quality/quality_ladder.tres");
		uint rareDepth = ordinary.Level(ItemQuality.Rare).MinDepth;
		float RareShare(uint depth) => gatekeeper.Level(ItemQuality.Rare).WeightAt(depth)
			/ gatekeeper.Levels.Sum(level => level.WeightAt(depth));

		AssertFloat(RareShare(rareDepth - 1)).IsEqual(0f);
		AssertFloat(RareShare(rareDepth)).IsGreater(0f);
		AssertFloat(RareShare(12)).IsGreater(RareShare(6));
		AssertFloat(RareShare(24)).IsGreater(RareShare(12));
		for (uint depth = 1; depth <= 24; depth++)
		{
			foreach (var quality in new[] { ItemQuality.Poor, ItemQuality.Normal, ItemQuality.Superior })
				AssertFloat(gatekeeper.Level(quality).WeightAt(depth)).IsEqual(0f);
		}
	}
}
