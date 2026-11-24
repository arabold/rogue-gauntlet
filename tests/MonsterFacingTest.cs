namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// After biting a hero who stands still, a monster that faces its travel turns and slithers away head first, while one
/// that keeps its target in front backs off facing the hero.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MonsterFacingTest
{
	[TestCase]
	public async Task ASnakeSlithersAwayHeadFirst() => await AssertRetreatFacing("res://scenes/enemies/snake/snake.tscn", facingAway: true);

	[TestCase]
	public async Task ABatBacksOffFacingTheHero() => await AssertRetreatFacing("res://scenes/enemies/bat/giant_bat.tscn", facingAway: false);

	private static async Task AssertRetreatFacing(string scenePath, bool facingAway)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 4);
		var player = harness.Player;
		player.InputController.SetPhysicsProcess(false);
		player.HurtBoxComponent.Invulnerable = true;
		var monster = TestFactories.SpawnHarnessMonster(harness, scenePath, 2.5f);
		harness.ExecuteCommand("automation", value: 1);

		// Sample the facing once the monster has backed 1.8 m away from the closest it came, well into its retreat.
		float closest = float.MaxValue, facing = 0;
		await TestFactories.AwaitPhysicsUntil(runner, () =>
		{
			Vector3 toHero = (player.GlobalPosition - monster.GlobalPosition).Horizontal();
			closest = Mathf.Min(closest, toHero.Length());
			if (toHero.Length() < closest + 1.8f) return false;
			facing = monster.MovementComponent.LookAtDirection.Horizontal().Normalized().Dot(toHero.Normalized());
			return true;
		}, 8);
		if (facingAway) AssertFloat(facing).IsLess(-0.5f);
		else AssertFloat(facing).IsGreater(0.5f);
	}
}
