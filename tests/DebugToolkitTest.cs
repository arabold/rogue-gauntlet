namespace RogueGauntlet.Tests;

using System.Linq;
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
}
