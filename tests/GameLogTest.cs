namespace RogueGauntlet.Tests;

using System;
using System.Collections.Generic;
using GdUnit4;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public class GameLogTest
{
	private class TestSink : ILogSink
	{
		public readonly List<LogEntry> Entries = new();
		public bool ShouldThrow { get; set; }

		public void Log(in LogEntry entry)
		{
			if (ShouldThrow)
			{
				throw new InvalidOperationException("Sink failed");
			}

			Entries.Add(entry);
		}
	}

	[TestCase]
	public void PlayerLogDispatchesToSinkWhenEnabled()
	{
		GameLog.ResetToDefaults();
		GameLog.IsConsoleSinkEnabled = false;
		var sink = new TestSink();
		GameLog.AddSink(sink);

		GameLog.Player("Drank Small Potion of Healing", LogSeverity.Info);

		AssertInt(sink.Entries.Count).IsEqual(1);
		AssertString(sink.Entries[0].Message).IsEqual("Drank Small Potion of Healing");
		AssertInt((int)sink.Entries[0].Channel).IsEqual((int)LogChannel.Player);
		AssertInt((int)sink.Entries[0].Severity).IsEqual((int)LogSeverity.Info);

		GameLog.ResetToDefaults();
	}

	[TestCase]
	public void DisabledPlayerChannelIgnoresMessages()
	{
		GameLog.ResetToDefaults();
		GameLog.IsConsoleSinkEnabled = false;
		GameLog.IsPlayerEnabled = false;
		var sink = new TestSink();
		GameLog.AddSink(sink);

		GameLog.Player("Hidden message");

		AssertInt(sink.Entries.Count).IsEqual(0);

		GameLog.ResetToDefaults();
	}

	[TestCase]
	public void InternalAndDebugRespectEnableToggles()
	{
		GameLog.ResetToDefaults();
		GameLog.IsConsoleSinkEnabled = false;
		var sink = new TestSink();
		GameLog.AddSink(sink);

		GameLog.Internal("AI decision", LogCategory.Ai);
		GameLog.Debug("event.123", "detail", LogCategory.Combat);
		AssertInt(sink.Entries.Count).IsEqual(0);

		GameLog.IsInternalEnabled = true;
		GameLog.Internal("AI active", LogCategory.Ai);
		AssertInt(sink.Entries.Count).IsEqual(1);
		AssertString(sink.Entries[0].Message).IsEqual("AI active");

		GameLog.IsDebugEnabled = true;
		GameLog.Debug("event.456", "detail", LogCategory.Combat);
		AssertInt(sink.Entries.Count).IsEqual(2);
		AssertString(sink.Entries[1].EventId).IsEqual("event.456");

		GameLog.ResetToDefaults();
	}

	[TestCase]
	public void FailingSinkIsDisabledGracefullyWithoutCrashing()
	{
		GameLog.ResetToDefaults();
		GameLog.IsConsoleSinkEnabled = false;
		var brokenSink = new TestSink { ShouldThrow = true };
		var healthySink = new TestSink();
		GameLog.AddSink(brokenSink);
		GameLog.AddSink(healthySink);

		// First dispatch: brokenSink throws, should be removed
		GameLog.Player("First message");

		AssertInt(healthySink.Entries.Count).IsEqual(1);

		// Second dispatch: brokenSink is already removed
		GameLog.Player("Second message");

		AssertInt(healthySink.Entries.Count).IsEqual(2);

		GameLog.ResetToDefaults();
	}
}