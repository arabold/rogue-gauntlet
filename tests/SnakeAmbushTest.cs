namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The snake lies coiled and still, out of the hero's targeting, until the hero comes within striking distance or hits
/// it from afar; then it uncoils and strikes.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SnakeAmbushTest
{
	private const string Snake = "res://scenes/enemies/snake/snake.tscn";

	[TestCase]
	public async Task ItLiesCoiledUntilTheHeroComesCloseThenStrikes()
	{
		var (runner, harness, snake) = await Stage(6);
		using var cleanup = runner;
		Vector3 start = snake.GlobalPosition;
		await runner.SimulateFrames(50, 20);
		AssertBool(snake.AmbushComponent.IsDormant).IsTrue();
		AssertBool(snake.IsInGroup("enemy")).IsFalse();
		AssertFloat(snake.GlobalPosition.HorizontalDistanceTo(start)).IsLess(.05f);

		var hero = harness.Player;
		hero.GlobalPosition = start + Vector3.Back * 3;
		hero.ResetPhysicsInterpolation();
		await TestFactories.AwaitPhysicsUntil(runner, () => snake.AmbushComponent.IsAwake, 1);
		float health = hero.HealthComponent.CurrentHealth;
		await TestFactories.AwaitPhysicsUntil(runner, () => hero.HealthComponent.CurrentHealth < health, 4);
	}

	[TestCase]
	public async Task ShootingItFromAfarWakesIt()
	{
		var (runner, harness, snake) = await Stage(6);
		using var cleanup = runner;
		AssertBool(harness.ExecuteCommand("equip", "res://scenes/items/weapons/crossbow_common.tres")).IsTrue();
		await runner.SimulateFrames(30, 20);
		AssertBool(snake.AmbushComponent.IsDormant).IsTrue();
		harness.Attack();
		await TestFactories.AwaitPhysicsUntil(runner, () => snake.AmbushComponent.IsAwake, 3);
	}

	/// <summary>A hero standing still with a snake <paramref name="distance"/> ahead, its monster AI on.</summary>
	private static async Task<(ISceneRunner Runner, CombatHarness Harness, EnemyBase Snake)> Stage(float distance)
	{
		var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 4);
		harness.Player.InputController.SetPhysicsProcess(false);
		var snake = TestFactories.SpawnHarnessMonster(harness, Snake, distance);
		harness.ExecuteCommand("automation", value: 1);
		await runner.SimulateFrames(2, 20);
		return (runner, harness, snake);
	}
}
