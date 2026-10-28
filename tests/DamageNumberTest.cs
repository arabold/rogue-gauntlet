namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Floating combat numbers: what each outcome shows, and a real hit spawning one that rises and fades.</summary>
[TestSuite]
[RequireGodotRuntime]
public class DamageNumberTest
{
	[TestCase(DamageOutcome.Damaged, 9.6f, DamageEmphasis.None, "10", DamageNumberKind.Damage)]
	[TestCase(DamageOutcome.Damaged, 0.2f, DamageEmphasis.None, "1", DamageNumberKind.Damage)]
	[TestCase(DamageOutcome.Damaged, 21f, DamageEmphasis.Critical | DamageEmphasis.Rear, "21", DamageNumberKind.Critical)]
	[TestCase(DamageOutcome.Damaged, 14f, DamageEmphasis.Sneak, "14", DamageNumberKind.Emphasized)]
	[TestCase(DamageOutcome.Blocked, 2.4f, DamageEmphasis.None, "2", DamageNumberKind.Defended)]
	[TestCase(DamageOutcome.Blocked, 0f, DamageEmphasis.None, "Block", DamageNumberKind.Defended)]
	[TestCase(DamageOutcome.GuardBroken, 6f, DamageEmphasis.None, "6", DamageNumberKind.Damage)]
	[TestCase(DamageOutcome.Absorbed, 0f, DamageEmphasis.None, "0", DamageNumberKind.Defended)]
	[TestCase(DamageOutcome.Dodged, 0f, DamageEmphasis.None, "Dodge", DamageNumberKind.Evaded)]
	[TestCase(DamageOutcome.Parried, 0f, DamageEmphasis.None, "Parry", DamageNumberKind.Evaded)]
	public void OutcomesShowTheirNumberOrWord(DamageOutcome outcome, float amount, DamageEmphasis emphasis, string text, DamageNumberKind kind)
	{
		AssertBool(DamageNumber.TryDescribe(outcome, amount, emphasis, out string shown, out var shownKind)).IsTrue();
		AssertString(shown).IsEqual(text);
		AssertInt((int)shownKind).IsEqual((int)kind);
	}

	[TestCase]
	public void FilteredContactsShowNothing()
	{
		AssertBool(DamageNumber.TryDescribe(DamageOutcome.Filtered, 5, DamageEmphasis.None, out _, out _)).IsFalse();
	}

	private static System.Collections.Generic.IEnumerable<DamageNumber> Floating(SceneTree tree) =>
		tree.Root.FindChildren("*", "", true, false).OfType<DamageNumber>().Where(n => n.IsProcessing());

	[TestCase]
	public async Task AHitSpawnsANumberThatRisesFadesAndReturnsToThePool()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		var hurtbox = harness.PracticeTarget.GetNode<HurtBoxComponent>("HurtBoxComponent");
		var tree = runner.Scene().GetTree();
		// Numbers from earlier tests may still be floating; only the one this hit spawns counts.
		var floating = Floating(tree).ToHashSet();
		hurtbox.ResolveDamage(new DamageContext
		{
			Amount = 7.4f, Faction = DamageSourceFlags.Player, IncomingDirection = Vector3.Forward,
			DefenseTraits = AttackDefenseTraits.Dodgeable, DeliveryKind = DamageDeliveryKind.DirectMelee, Critical = true
		});
		var number = Floating(tree).Single(n => !floating.Contains(n));
		AssertString(number.Label.Text).IsEqual("7");
		AssertBool(number.Label.Modulate.IsEqualApprox(number.CriticalColor)).IsTrue();
		Vector3 start = number.GlobalPosition;
		AssertBool(start.Y > harness.PracticeTarget.GlobalPosition.Y + 2).IsTrue();

		await runner.SimulateFrames(20);
		AssertBool(number.GlobalPosition.Y > start.Y).IsTrue();
		await runner.SimulateFrames(60, 20);
		// Despawned to the pool (hidden, idle) or freed when the pool is full.
		AssertBool(!GodotObject.IsInstanceValid(number) || (!number.IsProcessing() && !number.Visible)).IsTrue();
	}
}
