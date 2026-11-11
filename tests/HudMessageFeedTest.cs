namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public partial class HudMessageFeedTest
{
	[TestCase]
	public void ConsecutiveIdenticalMessagesCoalesceWithRepeatCount()
	{
		var feed = TestFactories.AddToSceneTree(new HudMessageFeed());
		feed.AddMessage("Picked up 10 Gold", LogSeverity.Info);
		feed.AddMessage("Picked up 10 Gold", LogSeverity.Info);

		AssertInt(feed.ActiveMessageCount).IsEqual(1);
		AssertInt(feed.GetChildCount()).IsEqual(1);

		var active = feed.GetActiveMessages();
		AssertString(active[0]).IsEqual("Picked up 10 Gold (x2)");

		feed.AddMessage("Picked up 10 Gold", LogSeverity.Info);
		active = feed.GetActiveMessages();
		AssertString(active[0]).IsEqual("Picked up 10 Gold (x3)");

		feed.QueueFree();
	}

	[TestCase]
	public void DistinctMessagesDoNotCoalesce()
	{
		var feed = TestFactories.AddToSceneTree(new HudMessageFeed());
		feed.AddMessage("Drank Small Potion of Healing", LogSeverity.Info);
		feed.AddMessage("Wielded Iron Broadsword", LogSeverity.Info);

		AssertInt(feed.ActiveMessageCount).IsEqual(2);
		AssertInt(feed.GetChildCount()).IsEqual(2);

		var active = feed.GetActiveMessages();
		AssertString(active[0]).IsEqual("Drank Small Potion of Healing");
		AssertString(active[1]).IsEqual("Wielded Iron Broadsword");
		AssertString(feed.GetChild<RichTextLabel>(0).Text).IsEqual("Drank Small Potion of Healing");

		feed.QueueFree();
	}

	[TestCase]
	public void FormatsBoldKeywordsWithConfiguredKeywordColor()
	{
		var feed = TestFactories.AddToSceneTree(new HudMessageFeed
		{
			KeywordColor = new Color(1f, 1f, 0f)
		});

		feed.AddMessage("Drank [b]Potion of Healing[/b]", LogSeverity.Info);
		var rtl = feed.GetChild<RichTextLabel>(0);
		AssertString(rtl.Text).IsEqual("Drank [b][color=#ffff00]Potion of Healing[/color][/b]");

		feed.QueueFree();
	}

	[TestCase]
	public void MaxVisibleMessagesEnforcesBoundedQueue()
	{
		var feed = TestFactories.AddToSceneTree(new HudMessageFeed { MaxVisibleMessages = 3 });

		feed.AddMessage("Message 1");
		feed.AddMessage("Message 2");
		feed.AddMessage("Message 3");
		feed.AddMessage("Message 4");

		AssertInt(feed.ActiveMessageCount).IsEqual(3);

		// Oldest was removed, active messages should be 2, 3, 4
		var active = feed.GetActiveMessages();
		AssertInt(active.Count).IsEqual(3);
		AssertString(active[0]).IsEqual("Message 2");
		AssertString(active[1]).IsEqual("Message 3");
		AssertString(active[2]).IsEqual("Message 4");

		feed.QueueFree();
	}

	[TestCase]
	public void MessagesFadeAndExpireOverTime()
	{
		var feed = TestFactories.AddToSceneTree(new HudMessageFeed
		{
			MessageDurationSeconds = 2.0f,
			FadeDurationSeconds = 1.0f
		});

		feed.AddMessage("Temporary message");
		var label = feed.GetChild<RichTextLabel>(0);
		AssertFloat(label.Modulate.A).IsEqual(1.0f);

		// Advance 1.5 seconds: into fade window (0.5s remaining / 1.0s fade = 0.5 alpha)
		feed._Process(1.5);
		AssertFloat(label.Modulate.A).IsLess(0.9f);
		AssertFloat(label.Modulate.A).IsGreater(0.1f);

		// Advance another 0.6 seconds: expired and removed
		feed._Process(0.6);
		AssertInt(feed.ActiveMessageCount).IsEqual(0);

		feed.QueueFree();
	}

	[TestCase]
	public void GamePausedFreezesDecay()
	{
		var feed = TestFactories.AddToSceneTree(new HudMessageFeed
		{
			MessageDurationSeconds = 2.0f,
			FadeDurationSeconds = 1.0f
		});

		feed.AddMessage("Paused message");
		var label = feed.GetChild<RichTextLabel>(0);

		// Signal paused
		SignalBus.EmitGamePaused(true);

		// Advance time while paused: should not decay
		feed._Process(1.5);
		AssertFloat(label.Modulate.A).IsEqual(1.0f);
		AssertInt(feed.ActiveMessageCount).IsEqual(1);

		// Unpause
		SignalBus.EmitGamePaused(false);
		feed._Process(1.5);
		AssertFloat(label.Modulate.A).IsLess(0.9f);

		feed.QueueFree();
	}
}