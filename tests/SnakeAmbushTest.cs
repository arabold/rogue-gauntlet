namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The snake lies coiled and still, concealed (no nameplate, monster x-ray or aim-assist), until the hero comes within
/// striking distance or hits it from afar; then it uncoils and strikes.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SnakeAmbushTest
{
	private const string Snake = "res://scenes/enemies/snake/snake.tscn";

	[TestCase]
	public async Task ItLiesCoiledUntilTheHeroComesCloseThenStrikes()
	{
		var (runner, harness, snake) = await TestFactories.StageHarnessMonster(Snake, 6);
		using var cleanup = runner;
		Vector3 start = snake.GlobalPosition;
		await TestFactories.AwaitPhysicsSeconds(runner, 1f);
		AssertBool(snake.IsConcealed).IsTrue();
		AssertBool(snake.Nameplate.IsShown || snake.Xray.IsShown).IsFalse();
		AssertFloat(snake.GlobalPosition.HorizontalDistanceTo(start)).IsLess(.05f);

		var hero = harness.Player;
		hero.GlobalPosition = start + Vector3.Back * 3;
		hero.ResetPhysicsInterpolation();
		await TestFactories.AwaitPhysicsUntil(runner, () => snake.AmbushComponent.IsAwake, 1);
		float health = hero.HealthComponent.CurrentHealth;
		await TestFactories.AwaitPhysicsUntil(runner, () => hero.HealthComponent.CurrentHealth < health, 4);
		AssertBool(snake.Nameplate.IsShown && snake.Xray.IsShown).IsTrue();
	}

	[TestCase]
	public async Task ShootingItFromAfarWakesIt()
	{
		var (runner, harness, snake) = await TestFactories.StageHarnessMonster(Snake, 6);
		using var cleanup = runner;
		AssertBool(harness.ExecuteCommand("equip", "res://scenes/items/weapons/crossbow_common.tres")).IsTrue();
		await TestFactories.AwaitPhysicsSeconds(runner, .6f);
		AssertBool(snake.IsConcealed).IsTrue();
		harness.Attack();
		await TestFactories.AwaitPhysicsUntil(runner, () => snake.AmbushComponent.IsAwake, 3);
	}

	[TestCase]
	public async Task ABlastAroundTheHeroStillHitsItAndWakesIt()
	{
		var (runner, harness, snake) = await TestFactories.StageHarnessMonster(Snake, 5);
		using var cleanup = runner;
		var frost = TestFactories.GetSpell<FrostNovaSpell>(harness, "Frost Nova");
		float health = snake.HealthComponent.CurrentHealth;
		AssertBool(frost.Cast(TestFactories.CastContext(harness.Player, TestFactories.SpellPower(1)), new RandomNumberGenerator { Seed = 2 })).IsTrue();
		await TestFactories.AwaitPhysicsUntil(runner, () => snake.HealthComponent.CurrentHealth < health, 3);
		AssertBool(snake.AmbushComponent.IsAwake).IsTrue();
	}
}
