namespace RogueGauntlet.Tests;

using System.Linq;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

/// <summary>The hero on display wears what its inventory has equipped and shows off gear as it is put on.</summary>
[TestSuite]
[RequireGodotRuntime]
public partial class HeroShowcaseTest
{
	[TestCase]
	public void TheShowcaseWearsItsInventoryAndShowsOffNewGear()
	{
		var showcase = TestFactories.AddToSceneTree(
			TestFactories.Load<PackedScene>("res://scenes/player/showcase/hero_showcase.tscn").Instantiate<HeroShowcase>());
		CharacterClass knight = TestFactories.Load<CharacterCatalog>(CharacterCatalog.DefaultCatalogPath).FindById("knight");
		Inventory gear = HeroShowcase.StartingGear(knight);

		showcase.Show(knight, gear);
		InventoryItemSlot sword = gear.EquippedItems[EquipmentSlot.WeaponHand];
		AssertObject(sword).OverrideFailureMessage("The knight's starting kit wields no weapon").IsNotNull();
		AssertObject(gear.EquippedItems[EquipmentSlot.ShieldHand]).IsNotNull();
		int worn = showcase.FindChildren("*", "", true, false).Count(node => node.HasMeta("equipped_attachment"));
		AssertInt(worn).OverrideFailureMessage("Worn gear shows no models on the hero").IsGreater(0);
		AssertString(showcase.CurrentClip).IsEqual(knight.PreviewAnimation);

		// Taking the sword up again shows it off with its own attack.
		gear.Unequip(sword);
		gear.Equip(sword);
		AssertString(showcase.CurrentClip).Contains("Melee_Attack");

		showcase.QueueFree();
	}

	[TestCase]
	public async System.Threading.Tasks.Task AWaitingHeroStrikesItsPosesInsteadOfAttackingTheAir()
	{
		using ISceneRunner runner = TestFactories.LoadScene("res://scenes/player/showcase/hero_showcase.tscn");
		var showcase = (HeroShowcase)runner.Scene();
		CharacterClass knight = TestFactories.Load<CharacterCatalog>(CharacterCatalog.DefaultCatalogPath).FindById("knight");
		showcase.FlourishInterval = 0.3f;
		showcase.Poses = [new ShowcasePose { Clips = ["Sit_Floor_Down", "Sit_Floor_StandUp"] }];
		showcase.Show(knight, HeroShowcase.StartingGear(knight));

		// The pose plays its clips in turn, then the hero idles again: armed, it never swings.
		var played = new System.Collections.Generic.List<string>();
		for (int frame = 0; frame < 300 && (played.Count < 4 || played[^1] != knight.PreviewAnimation); frame++)
		{
			await TestFactories.AwaitPhysicsFrames(runner, 1);
			string clip = showcase.CurrentClip;
			if (played.Count == 0 || played[^1] != clip)
			{
				played.Add(clip);
			}
		}
		AssertArray(played.ToArray()).ContainsExactly(knight.PreviewAnimation, "Sit_Floor_Down", "Sit_Floor_StandUp", knight.PreviewAnimation);
	}
}
