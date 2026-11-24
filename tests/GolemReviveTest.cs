namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The stone golem's revive through the real monster in the combat harness: the first blow that would kill it collapses
/// it instead, no hit can stop it rising with part of its health, and the next lethal blow kills it for good.
/// </summary>
[TestSuite, RequireGodotRuntime]
public partial class GolemReviveTest
{
	private const string Golem = "res://scenes/enemies/golem/stone_golem.tscn";
	/// <summary>Short stand-ins for the authored downed and rise times, so the suite waits about a second.</summary>
	private const float DownedFor = 0.4f, RiseFor = 0.3f;

	[TestCase]
	public async Task ItRisesOnceWhateverTheHeroDoesAndTheNextLethalBlowIsFinal()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		harness.Player.InputController.SetPhysicsProcess(false);
		var golem = TestFactories.SpawnHarnessMonster(harness, Golem, 6);
		var revive = golem.GetNode<ReviveComponent>("ReviveComponent");
		revive.DownedDuration = DownedFor;
		revive.RiseDuration = RiseFor;
		await runner.SimulateFrames(2, 20);
		AssertBool(golem.DefinitionValid).OverrideFailureMessage("the golem must pass monster validation").IsTrue();
		var health = golem.HealthComponent;
		bool died = false;
		health.Died += () => died = true;

		health.TakeDamage(health.MaxHealth * 2);
		AssertBool(revive.IsDowned).IsTrue();
		AssertBool(golem.ActionGate.IsStatusLocked).OverrideFailureMessage("a downed golem cannot act").IsTrue();
		// Striking the rubble, or anything that bypasses the hurtbox, cannot finish it.
		golem.TakeDamage(health.MaxHealth, Vector3.Forward, attackerFaction: DamageSourceFlags.Player);
		health.TakeDamage(health.MaxHealth);
		AssertBool(died).IsFalse();
		AssertBool(health.IsDead).IsFalse();

		await TestFactories.AwaitPhysicsUntil(runner, () => !revive.IsDowned, 3);
		AssertFloat(health.CurrentHealth).IsEqualApprox(health.MaxHealth * revive.ReviveHealthShare, 0.01f);
		AssertBool(golem.ActionGate.IsStatusLocked).IsFalse();

		golem.TakeDamage(health.MaxHealth, Vector3.Forward, attackerFaction: DamageSourceFlags.Player);
		AssertBool(died).OverrideFailureMessage("the lethal blow after its revive destroys it for good").IsTrue();
		AssertBool(revive.IsDowned).IsFalse();
	}
}
