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
	/// A monster spawned on a generated floor with AI on chases the player over the floor's navigation, and killing it
	/// records no cleared entity, so the floor's save never claims a monster it did not generate.
	/// </summary>
	[TestCase]
	public async Task ASpawnedMonsterChasesThePlayerAndClearsNothingWhenKilled()
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
		var tool = scene.FindChildren("*", "HBoxContainer", true, false).OfType<DebugMenu>().Single().GetTool<MonsterTool>();
		tool.AIEnabled = true;

		var monster = (EnemyBase)tool.Spawn(TestFactories.Load<PackedScene>("res://scenes/enemies/skeleton/skeleton_minion.tscn"), 7);
		AssertObject(monster).IsNotNull();
		AssertFloat(monster.GlobalPosition.HorizontalDistanceTo(player.GlobalPosition)).IsGreater(4);
		await TestFactories.AwaitPhysicsUntil(runner, () => monster.GlobalPosition.HorizontalDistanceTo(player.GlobalPosition) < 2.5f, 10);

		tool.KillAll();
		AssertBool(monster.IsDead).IsTrue();
		AssertArray(level.Run.CaptureClearedIds()).IsEqual(cleared);
	}
}
