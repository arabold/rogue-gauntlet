namespace RogueGauntlet.Tests;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// A run saves when its hero arrives on a floor and when the player leaves the game. The hero's death replaces the save
/// with a record of the fallen run: the game shows it on the death screen, and the menu shows it again instead of
/// continuing the run. Saves go to a scratch folder, never the real slots.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class RunLifecycleTest
{
	private const string MainScene = "res://scenes/main/main.tscn";
	private const int Slot = 1;
	private const ulong Seed = 4242;

	/// <summary>
	/// The first floor saves on arrival, and closing the window saves the run as it stands, even mid-action. Death then
	/// overwrites the save with the fallen run's record, which closing the window no longer replaces, and the game
	/// pauses under the death screen reporting it.
	/// </summary>
	[TestCase]
	public async Task RunSavesUntilDeathRecordsItAndShowsTheDeathScreen()
	{
		using var saves = new ScratchSaves();
		GameSession.Instance.BeginRun(Slot, "barbarian", Seed);
		using ISceneRunner runner = TestFactories.LoadScene(MainScene);
		await runner.SimulateFrames(5, 20);
		var main = (Main)runner.Scene();

		SaveGame arrival = SaveService.Load(Slot);
		AssertObject(arrival).OverrideFailureMessage("The first floor wrote no save").IsNotNull();
		AssertObject(arrival.Outcome).IsEqual(RunOutcome.InProgress);
		AssertInt((int)arrival.DungeonDepth).IsEqual(1);

		Player player = main.GetTree().GetNodesInGroup("player").OfType<Player>().Single();
		player.Stats.Gold = 77;
		player.ActionGate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, _ => { }, out _);
		AssertBool(player.ActionGate.IsBusy).IsTrue();
		GameSession.Instance.Notification((int)Node.NotificationWMCloseRequest);
		SaveGame closed = SaveService.Load(Slot);
		AssertObject(closed.Outcome).IsEqual(RunOutcome.InProgress);
		AssertInt(closed.Player.Stats.Gold).IsEqual(77);

		// Short waits; the delay still outlasts the near-death red's fade-in, as the authored one does.
		ScreenEffect nearDeath = main.ScreenEffects.HealthDrivers[0].Effect;
		main.ScreenEffects.HealthDrivers[0].ResponseTime = 0.05f;
		main.DeathScreen.FadeInSeconds = 0.1f;
		main.DeathScreenDelay = 0.2f;
		TestFactories.Tool<HeroTool>(main).Kill();

		SaveGame record = SaveService.Load(Slot);
		AssertObject(record.Outcome).IsEqual(RunOutcome.Died);
		AssertBool(record.Seed == Seed).IsTrue();
		AssertString(record.CharacterClassId).IsEqual("barbarian");
		AssertInt(record.Player.Stats.Gold).IsEqual(77);

		// Leaving the game after death cannot restore a playable save.
		GameSession.Instance.Notification((int)Node.NotificationWMCloseRequest);
		AssertObject(SaveService.Load(Slot).Outcome).IsEqual(RunOutcome.Died);

		DeathScreen screen = main.DeathScreen;
		await TestFactories.AwaitPhysicsUntil(runner, () => screen.Visible, 3);
		AssertBool(main.GetTree().Paused).IsTrue();
		AssertString(screen.EpitaphLabel.Text).IsEqual("The Barbarian fell on depth 1.");
		AssertString(screen.DetailsLabel.Text).Contains("Gold 77");

		// Under the screen the heartbeat stops while the near-death red holds.
		await TestFactories.AwaitPhysicsUntil(runner, () => nearDeath.Motion == 0, 3);
		AssertFloat(nearDeath.Intensity).IsEqual(1f);
	}

	/// <summary>The menu marks a fallen slot and, asked to load it, shows its record instead of continuing the run.</summary>
	[TestCase]
	public async Task LoadingAFallenRunShowsItsDeathScreen()
	{
		using var saves = new ScratchSaves();
		var fallen = new SaveGame { SlotId = 2, Outcome = RunOutcome.Died, CharacterClassId = "barbarian", Seed = Seed, DungeonDepth = 4 };
		fallen.Player.Stats.XpLevel = 3;
		AssertBool(SaveService.Save(fallen)).IsTrue();

		using ISceneRunner runner = TestFactories.LoadScene("res://scenes/menu/main_menu.tscn");
		await runner.SimulateFrames(2, 20);
		var menu = (MainMenu)runner.Scene();
		menu.GetNode<Button>("%LoadGameButton").EmitSignal(BaseButton.SignalName.Pressed);
		var slot = menu.GetNode<Button>("%Slot2Button");
		AssertString(slot.Text).StartsWith("Slot 2 - Barbarian (fallen)");

		slot.EmitSignal(BaseButton.SignalName.Pressed);
		AssertBool(menu.DeathScreen.Visible).IsTrue();
		AssertString(menu.DeathScreen.EpitaphLabel.Text).IsEqual("The Barbarian fell on depth 4.");
		AssertString(menu.DeathScreen.DetailsLabel.Text).Contains("Level 3");
		AssertBool(GameSession.Instance.IsGameActive).IsFalse();

		menu.DeathScreen.DismissButton.EmitSignal(BaseButton.SignalName.Pressed);
		AssertBool(menu.DeathScreen.Visible).IsFalse();
		AssertBool(slot.IsVisibleInTree()).IsTrue();
	}

	/// <summary>Points saves at a scratch folder, then ends any run and removes the folder, so no state leaks into later suites.</summary>
	private sealed class ScratchSaves : IDisposable
	{
		private readonly string _previous = SaveService.SaveDirectory;
		private readonly string _directory = $"user://test-saves-{Guid.NewGuid():N}";

		public ScratchSaves() => SaveService.SaveDirectory = _directory;

		public void Dispose()
		{
			GameSession.Instance.ClearSession();
			((SceneTree)Engine.GetMainLoop()).Paused = false;
			SaveService.SaveDirectory = _previous;
			string path = ProjectSettings.GlobalizePath(_directory);
			if (Directory.Exists(path)) Directory.Delete(path, true);
		}
	}
}
