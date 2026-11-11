namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>The debug tools create content through the production factories and rolls.</summary>
[TestSuite, RequireGodotRuntime]
public class DebugToolkitTest
{
	private const string WandPath = "res://scenes/items/weapons/wand.tres";
	private const string StaffPath = "res://scenes/items/weapons/staff_common.tres";

	/// <summary>A forced quality and curse roll the weapon without redrawing the spell picked for it.</summary>
	[TestCase]
	public void SpawnedSpellWeaponsKeepThePickedSpellThroughAForcedRoll()
	{
		var tool = new ItemSpawnTool
		{
			CreationPolicy = TestFactories.Load<ItemCreationPolicy>("res://scenes/items/dungeon_item_policy.tres"),
			Quality = ItemQuality.Rare,
			Cursed = true,
		};
		try
		{
			foreach (string path in new[] { WandPath, StaffPath })
			{
				var definition = TestFactories.Load<MagicStaff>(path);
				var spells = definition.SpellPool.Spells.Where(definition.CanBind).ToArray();
				AssertBool(spells.Length > 1).IsTrue();
				foreach (SpellDefinition spell in spells)
				{
					var staff = (MagicStaff)tool.Create(definition, spell);
					AssertObject(staff.BoundSpell).IsSame(spell);
					AssertBool(staff.Quality == ItemQuality.Rare).IsTrue();
					AssertBool(staff.IsCursed).IsTrue();
				}
			}
		}
		finally
		{
			tool.Free();
		}
	}

	/// <summary>
	/// A monster spawned on a generated floor with the game's default settings plays like a regular monster: it chases
	/// the player over the floor's navigation, attacks them and keeps its counterattacks. Invulnerable, it takes hits without losing
	/// health. Killed, it awards its experience and records no cleared entity, so the floor's save never claims a
	/// monster it did not generate.
	/// </summary>
	[TestCase]
	public async Task ASpawnedMonsterPlaysLikeARegularMonsterAndClearsNothingWhenKilled()
	{
		using var runner = TestFactories.LoadScene("res://scenes/main/main.tscn");
		await runner.SimulateFrames(5, 20);
		var scene = runner.Scene();
		var level = scene.GetNode<Level>("Level");
		var player = scene.GetTree().GetNodesInGroup("player").OfType<Player>().Single();
		player.HurtBoxComponent.Invulnerable = true;
		foreach (var door in level.Floor.NavigationGates) door.IsOpen = true;
		// The floor's own monsters would join the chase.
		foreach (var enemy in level.FindChildren("*", "", true, false).OfType<EnemyBase>()) enemy.QueueFree();
		await runner.SimulateFrames(1, 20);
		var cleared = level.Run.CaptureClearedIds();
		var tool = TestFactories.Tool<MonsterTool>(scene);

		var monster = (EnemyBase)tool.Spawn(TestFactories.Load<PackedScene>("res://scenes/enemies/skeleton/skeleton_warrior.tscn"), 7);
		AssertObject(monster).IsNotNull();
		AssertBool(monster.DefenseController.RiposteEnabled).IsTrue();
		AssertFloat(monster.GlobalPosition.HorizontalDistanceTo(player.GlobalPosition)).IsGreater(4);
		// It closes in over the floor's navigation and swings at the player without being told to.
		await TestFactories.AwaitPhysicsUntil(runner, () => monster.AttackController.IsAttacking, 10);
		AssertFloat(monster.GlobalPosition.HorizontalDistanceTo(player.GlobalPosition)).IsLess(4);

		DamageResolution Hit() => monster.ResolveDamage(new DamageContext
		{
			Amount = 5, Faction = DamageSourceFlags.Player, DeliveryKind = DamageDeliveryKind.DirectMelee,
			IncomingDirection = Vector3.Forward,
		});
		float health = monster.HealthComponent.CurrentHealth;
		tool.Invulnerable = true;
		AssertObject(Hit().Outcome).IsEqual(DamageOutcome.Invulnerable);
		AssertFloat(monster.HealthComponent.CurrentHealth).IsEqual(health);
		tool.Invulnerable = false;
		AssertObject(Hit().Outcome).IsEqual(DamageOutcome.Damaged);
		AssertFloat(monster.HealthComponent.CurrentHealth).IsLess(health);

		int xp = player.Stats.Xp;
		tool.KillAll();
		AssertBool(monster.IsDead).IsTrue();
		AssertInt(player.Stats.Xp).IsGreater(xp);
		AssertArray(level.Run.CaptureClearedIds()).IsEqual(cleared);
	}

	/// <summary>
	/// A hero build equips its items at the authored quality, curse and spell, packs its backpack and takes its class's
	/// attributes; applied again with the same seed it gives the same affixes, even over its own cursed staff. A build
	/// that cannot apply leaves the hero untouched, and every authored preset applies.
	/// </summary>
	[TestCase]
	public async Task AHeroBuildEquipsExactItemsAndRepeatsItsAffixesForTheSameSeed()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		var mage = TestFactories.Load<CharacterClass>("res://scenes/player/classes/class_mage.tres");
		var meteor = TestFactories.Load<SpellDefinition>("res://scenes/magic/spells/meteor.tres");
		var potion = TestFactories.Load<Item>("res://scenes/items/potions/potion_healing_small.tres");
		var build = new HeroBuildProfile
		{
			CharacterClass = mage,
			Equipment =
			[
				new HeroBuildItem
				{
					Definition = TestFactories.Load<MagicStaff>("res://scenes/items/weapons/staff/staff_archmage.tres"),
					Quality = ItemQuality.Rare, Cursed = true, Spell = meteor,
				},
				new HeroBuildItem
				{
					Definition = TestFactories.Load<Item>("res://scenes/items/jewelry/ring/ring_mind.tres"),
					Quality = ItemQuality.Magic, Slot = EquipmentSlot.LeftRing,
				},
			],
			Backpack = [new HeroBuildItem { Definition = potion, Quantity = 3 }],
		};
		var inventory = harness.Player.Inventory;
		string[] Rolls() => inventory.EquippedItems.Values.Where(entry => entry != null).Select(entry => (EquipableItem)entry.Item)
			.Select(item => $"{item.Name} {item.MagnitudeScale}: " + string.Join(", ", item.Affixes.SelectMany(affix => affix.Modifiers)
				.Select(modifier => $"{modifier.Stat} {modifier.Op} {modifier.Value}"))).ToArray();

		AssertBool(harness.Hero.ApplyBuild(build)).IsTrue();
		var staff = (MagicStaff)inventory.EquippedItems[EquipmentSlot.WeaponHand].Item;
		AssertObject(staff.BoundSpell).IsSame(meteor);
		AssertBool(staff.Quality == ItemQuality.Rare && staff.IsCursed).IsTrue();
		AssertBool(((EquipableItem)inventory.EquippedItems[EquipmentSlot.LeftRing].Item).Quality == ItemQuality.Magic).IsTrue();
		AssertInt(inventory.Items.Count).IsEqual(3);
		AssertInt(inventory.Items.Single(entry => entry.Item.Name == potion.Name).Quantity).IsEqual(3);
		AssertFloat(harness.Player.Stats.BaseIntelligence).IsEqual(mage.BaseIntelligence);
		AssertFloat(harness.Player.HealthComponent.CurrentHealth).IsEqual(harness.Player.Stats.MaxHealth);
		string[] first = Rolls();
		AssertBool(first.Any(roll => roll.Contains(" Flat ") || roll.Contains(" Percent"))).IsTrue();

		AssertBool(harness.Hero.ApplyBuild(build)).IsTrue();
		AssertArray(Rolls()).IsEqual(first);

		var invalid = new HeroBuildProfile { Equipment = [new HeroBuildItem { Definition = potion, Slot = EquipmentSlot.WeaponHand }] };
		AssertBool(harness.Hero.ApplyBuild(invalid)).IsFalse();
		AssertArray(Rolls()).IsEqual(first);

		foreach (var (preset, path) in DebugCatalog.Resources<HeroBuildProfile>(harness.Hero.BuildDirectory))
		{
			AssertBool(harness.Hero.ApplyBuild(preset)).OverrideFailureMessage(path).IsTrue();
			AssertInt(inventory.Items.Count).OverrideFailureMessage(path).IsEqual(preset.Equipment.Length + preset.Backpack.Length);
		}
	}
}
