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
		await TestFactories.AwaitPhysicsFrames(runner, 5);
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
		AssertString(screen.EpitaphLabel.Text).IsEqual("Barbarian · Depth 1");
		AssertString(screen.GoldLabel.Text).IsEqual("77");

		// Under the screen the heartbeat stops while the near-death red holds.
		await TestFactories.AwaitPhysicsUntil(runner, () => nearDeath.Motion == 0, 3);
		AssertFloat(nearDeath.Intensity).IsEqual(1f);
	}

	/// <summary>The menu's card marks a fallen run and, pressed, shows its record instead of continuing the run.</summary>
	[TestCase]
	public async Task LoadingAFallenRunShowsItsDeathScreen()
	{
		using var saves = new ScratchSaves();
		var fallen = new SaveGame { SlotId = 2, Outcome = RunOutcome.Died, CharacterClassId = "barbarian", Seed = Seed, DungeonDepth = 4 };
		fallen.Player.Stats.XpLevel = 3;
		AssertBool(SaveService.Save(fallen)).IsTrue();

		using ISceneRunner runner = TestFactories.LoadScene("res://scenes/menu/main_menu.tscn");
		await TestFactories.AwaitPhysicsFrames(runner, 2);
		var menu = (MainMenu)runner.Scene();
		menu.RunsButton.EmitSignal(BaseButton.SignalName.Pressed);
		var card = menu.CardRow.GetChild<RunCard>(1);
		AssertString(card.NameLabel.Text).IsEqual("Barbarian");
		AssertString(card.LineLabel.Text).IsEqual("Fell on Depth 4");

		card.EmitSignal(BaseButton.SignalName.Pressed);
		AssertBool(menu.DeathScreen.Visible).IsTrue();
		AssertString(menu.DeathScreen.EpitaphLabel.Text).IsEqual("Barbarian · Depth 4");
		AssertString(menu.DeathScreen.LevelLabel.Text).IsEqual("3");
		AssertBool(GameSession.Instance.IsGameActive).IsFalse();

		menu.DeathScreen.DismissButton.EmitSignal(BaseButton.SignalName.Pressed);
		AssertBool(menu.DeathScreen.Visible).IsFalse();
		AssertBool(card.IsVisibleInTree()).IsTrue();
	}

	/// <summary>With every slot taken a new run still begins with its hero, and only then asks which run to replace.</summary>
	[TestCase]
	public async Task ANewRunPicksItsHeroBeforeTheRunItReplaces()
	{
		using var saves = new ScratchSaves();
		for (int slot = 1; slot <= SaveService.SlotCount; slot++)
		{
			AssertBool(SaveService.Save(new SaveGame { SlotId = slot, CharacterClassId = "barbarian", Seed = Seed, DungeonDepth = 2 })).IsTrue();
		}

		using ISceneRunner runner = TestFactories.LoadScene("res://scenes/menu/main_menu.tscn");
		await TestFactories.AwaitPhysicsFrames(runner, 2);
		var menu = (MainMenu)runner.Scene();
		menu.NewRunButton.EmitSignal(BaseButton.SignalName.Pressed);
		AssertBool(menu.HeroScreen.Visible).IsTrue();
		AssertBool(menu.RunsScreen.Visible).IsFalse();

		menu.HeroScreen.StartButton.EmitSignal(BaseButton.SignalName.Pressed);
		AssertBool(menu.RunsScreen.Visible).IsTrue();
		AssertString(menu.RunsTitle.Text).IsEqual("Replace a Run");
		AssertBool(GameSession.Instance.IsGameActive).IsFalse();

		// Choosing a run only asks; backing out of the choice returns to the hero.
		menu.CardRow.GetChild<RunCard>(0).EmitSignal(BaseButton.SignalName.Pressed);
		AssertBool(menu.ReplaceDialog.Visible).IsTrue();
		AssertBool(GameSession.Instance.IsGameActive).IsFalse();
		menu.ReplaceDialog.Hide();
		menu.RunsBackButton.EmitSignal(BaseButton.SignalName.Pressed);
		AssertBool(menu.HeroScreen.Visible).IsTrue();
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
