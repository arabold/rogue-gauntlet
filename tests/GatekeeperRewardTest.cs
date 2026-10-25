namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The Forsaken Warden's reward bridges into the next realm: every drop is a tier-2 weapon of Magic or Rare quality and
/// never cursed, through its own loot table and the gatekeeper creation policy wired on its loot component.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class GatekeeperRewardTest
{
	[TestCase]
	public void TheWardenDropsAMagicOrRareTierTwoWeaponThatIsNeverCursed()
	{
		var definition = GD.Load<MonsterDefinition>("res://scenes/enemies/forsaken_warden/forsaken_warden_definition.tres");
		Node warden = GD.Load<PackedScene>("res://scenes/enemies/forsaken_warden/forsaken_warden.tscn").Instantiate();
		ItemCreationPolicy policy = warden.GetNode<LootTableComponent>("LootTableComponent").CreationPolicy;
		warden.Free();
		AssertObject(policy).IsNotNull();

		using var rng = GenerationRandom.Create(42, "warden-reward");
		for (int i = 0; i < 200; i++)
		{
			var weapon = ItemFactory.Create(definition.LootTable.PickEntry(3, rng).Item, policy, 3, rng) as Weapon;
			AssertObject(weapon).IsNotNull();
			AssertInt(weapon.Tier).IsEqual(2);
			AssertBool(weapon.Quality is ItemQuality.Magic or ItemQuality.Rare).IsTrue();
			AssertBool(weapon.IsCursed).IsFalse();
		}
	}
}
