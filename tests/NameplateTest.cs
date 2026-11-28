namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using System.Linq;
using System.Threading.Tasks;
using static GdUnit4.Assertions;

/// <summary>Names show only near the player, above what they name, never for the dead or the hidden, and never under fog.</summary>
[TestSuite, RequireGodotRuntime]
public class NameplateTest
{
	private static Nameplate NewPlate() => TestFactories.Load<PackedScene>("res://scenes/components/nameplate.tscn").Instantiate<Nameplate>();

	/// <summary>A plate on a fresh actor, with its health bound and an optional health bar on it.</summary>
	private static (Nameplate Plate, HealthComponent Health) AddActor(FloatingHealthBar bar = null)
	{
		var health = new HealthComponent();
		var plate = NewPlate();
		plate.Health = health;
		if (bar != null)
		{
			bar.HealthComponent = health;
			bar.ShowWithName = plate;
			plate.AddChild(bar);
		}
		var actor = new Node3D();
		actor.AddChild(health);
		actor.AddChild(plate);
		TestFactories.AddToSceneTree(actor);
		return (plate, health);
	}

	private static ISceneRunner LoadHarness() => TestFactories.LoadScene("res://scenes/debug/effects/world_effect_harness.tscn");

	[TestCase]
	public async Task NameShowsOnlyWhileThePlayerIsNearAndTheActorIsAlive()
	{
		using var runner = LoadHarness();
		await TestFactories.AwaitPhysicsFrames(runner, 2);
		var player = TestFactories.AddStandInPlayer();
		var (plate, health) = AddActor();
		Label3D label = plate.NameLabel;
		float edge = plate.ShowDistance;

		player.GlobalPosition = new Vector3(edge + 2, 0, 0);
		await TestFactories.AwaitPhysicsFrames(runner, 5);
		AssertBool(label.Visible).IsFalse();

		// Only horizontal distance counts, so a tall actor's raised plate doesn't shrink its range.
		player.GlobalPosition = new Vector3(edge - 1, 5, 0);
		await TestFactories.AwaitPhysicsSeconds(runner, .3f);
		AssertBool(label.Visible).IsTrue();
		AssertFloat(label.Modulate.A).IsEqualApprox(1f, 0.01f);

		// Just past the edge keeps it, so a player standing there doesn't make it flicker; clearly past fades it out.
		player.GlobalPosition = new Vector3(edge + 0.25f, 0, 0);
		await TestFactories.AwaitPhysicsFrames(runner, 5);
		AssertBool(plate.IsShown).IsTrue();
		player.GlobalPosition = new Vector3(edge + 1, 0, 0);
		await TestFactories.AwaitPhysicsSeconds(runner, .3f);
		AssertBool(label.Visible).IsFalse();

		player.GlobalPosition = Vector3.Zero;
		plate.Enabled = false;
		await TestFactories.AwaitPhysicsFrames(runner, 5);
		AssertBool(plate.IsShown).IsFalse();
		plate.Enabled = true;
		await TestFactories.AwaitPhysicsFrames(runner, 5);
		AssertBool(plate.IsShown).IsTrue();

		health.SetHealth(0, health.MaxHealth);
		await TestFactories.AwaitPhysicsSeconds(runner, .3f);
		AssertBool(label.Visible).IsFalse();
	}

	[TestCase]
	public async Task PlateStaysUnderFogUntilItsOwnTileIsExplored()
	{
		using var runner = LoadHarness();
		await TestFactories.AwaitPhysicsFrames(runner, 2);
		var (level, map) = TestFactories.UngeneratedLevel();
		var plate = NewPlate();
		plate.Position = new Vector3(8, 3, 8);
		level.AddWorldNode(plate);
		TestFactories.AddToSceneTree(level);
		var floor = TestFactories.StartEmptyFloor(level, map);
		var player = TestFactories.AddStandInPlayer();
		player.GlobalPosition = plate.GlobalPosition with { Y = 0 } + Vector3.Right;

		// Right next to the player, but the plate draws over the fog caps, so it must check fog itself.
		await TestFactories.AwaitPhysicsSeconds(runner, .2f);
		AssertBool(plate.Visible).IsFalse();
		AssertBool(plate.NameLabel.IsVisibleInTree()).IsFalse();
		TestFactories.RevealTileAt(level.Discovery, floor.Map, player.GlobalPosition + Vector3.Right * 4);
		await TestFactories.AwaitPhysicsSeconds(runner, .2f);
		AssertBool(plate.Visible).IsFalse();
		TestFactories.RevealTileAt(level.Discovery, floor.Map, plate.GlobalPosition);
		await TestFactories.AwaitPhysicsSeconds(runner, .3f);
		AssertBool(plate.NameLabel.IsVisibleInTree()).IsTrue();
	}

	[TestCase]
	public async Task HealthBarFadesWithTheNameAtFullHealth()
	{
		using var runner = LoadHarness();
		await TestFactories.AwaitPhysicsFrames(runner, 2);
		var player = TestFactories.AddStandInPlayer();
		var bar = TestFactories.Load<PackedScene>("res://scenes/components/floating_health_bar.tscn").Instantiate<FloatingHealthBar>();
		var (plate, health) = AddActor(bar);

		player.GlobalPosition = new Vector3(plate.ShowDistance + 3, 0, 0);
		await TestFactories.AwaitPhysicsFrames(runner, 5);
		AssertBool(bar.Visible).IsFalse();
		player.GlobalPosition = Vector3.Zero;
		await TestFactories.AwaitPhysicsFrames(runner, 2);
		AssertBool(bar.Visible).IsTrue();
		AssertFloat(bar.Modulate.A).IsBetween(0.01f, 0.99f);
		await TestFactories.AwaitPhysicsSeconds(runner, .2f);
		AssertFloat(bar.Modulate.A).IsEqualApprox(1f, 0.01f);
		player.GlobalPosition = new Vector3(plate.ShowDistance + 3, 0, 0);
		await TestFactories.AwaitPhysicsFrames(runner, 2);
		AssertBool(bar.Visible).IsTrue();
		AssertFloat(bar.Modulate.A).IsLess(1f);
		await TestFactories.AwaitPhysicsSeconds(runner, .2f);
		AssertBool(bar.Visible).IsFalse();
		// Once hurt, the bar stays up wherever the player is, as before.
		health.SetHealth(health.MaxHealth - 1, health.MaxHealth);
		await TestFactories.AwaitPhysicsSeconds(runner, .24f);
		AssertBool(bar.Visible).IsTrue();
		AssertFloat(bar.Modulate.A).IsEqualApprox(1f, 0.01f);
	}

	[TestCase]
	public async Task BossHealthDrainsThroughPhaseBoundariesAndTracksPoiseSeparately()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		var boss = TestFactories.SpawnHarnessMonster(harness, "res://scenes/enemies/forsaken_warden/forsaken_warden.tscn");
		var bar = boss.Nameplate.GetNode<FloatingHealthBar>("FloatingHealthBar");
		var segments = bar.HealthTrack.GetChildren().OfType<ProgressBar>().ToArray();
		AssertInt(segments.Length).IsEqual(3);

		boss.HealthComponent.SetHealth(65, 100);
		AssertInt(boss.HealthPhases.CurrentPhase).IsEqual(1);
		AssertFloat((float)segments[0].Ratio).IsEqualApprox(1, .001f);
		AssertFloat((float)segments[1].Ratio).IsEqualApprox(1, .001f);
		AssertFloat((float)segments[2].Ratio).IsEqualApprox(0, .001f);

		boss.HealthComponent.SetHealth(47.5f, 100);
		AssertFloat((float)segments[1].Ratio).IsEqualApprox(.5f, .001f);

		boss.HealthComponent.SetHealth(30, 100);
		AssertInt(boss.HealthPhases.CurrentPhase).IsEqual(2);
		AssertFloat((float)segments[0].Ratio).IsEqualApprox(1, .001f);
		AssertFloat((float)segments[1].Ratio).IsEqualApprox(0, .001f);

		boss.HealthComponent.SetHealth(15, 100);
		bar.Poise.Apply(bar.Poise.Maximum * .5f);
		AssertFloat((float)bar.PoiseBar.Ratio).IsEqualApprox(.5f, .001f);
		AssertFloat((float)segments[0].Ratio).IsEqualApprox(.5f, .001f);
	}

	[TestCase]
	public async Task PickupNamesItselfInItsLootColorOnTopOfItsModel()
	{
		using var runner = LoadHarness();
		await TestFactories.AwaitPhysicsFrames(runner, 2);
		var potion = TestFactories.Pickup("res://scenes/items/potions/potion_healing_small.tres", 3);
		var sword = TestFactories.Pickup("res://scenes/items/weapons/sword_rare.tres");
		potion.Animate = sword.Animate = false;
		runner.Scene().AddChild(potion);
		runner.Scene().AddChild(sword);
		await TestFactories.AwaitPhysicsFrames(runner, 2);

		AssertString(potion.Nameplate.NameLabel.Text).IsEqual($"{ItemIdentity.ResolveDisplayName(potion.Item)} (3)");
		AssertString(sword.Nameplate.NameLabel.Text).IsEqual(ItemIdentity.ResolveDisplayName(sword.Item));
		Color color = LootVisuals.ResolveColor(sword.Item);
		AssertBool(new Color(sword.Nameplate.NameLabel.Modulate, 1f).IsEqualApprox(new Color(color, 1f))).IsTrue();
		// A sword stands far taller than a potion, so its plate sits higher, and both clear the resting pivot.
		AssertBool(sword.Nameplate.Position.Y > potion.Nameplate.Position.Y).IsTrue();
		AssertBool(potion.Nameplate.Position.Y > potion.Pivot.Position.Y).IsTrue();
	}
}
