using Godot;
using System;
using System.Collections.Generic;

public enum LogChannel
{
	Player,
	Internal,
	Debug
}

public enum LogSeverity
{
	Info,
	Discovery,
	Warning,
	Error,
	ItemPickup,
	Gold,
	Experience,
	Equipment
}

public enum LogCategory
{
	General,
	Combat,
	Ai,
	Items,
	Level,
	Save,
	Ui
}

public readonly struct LogEntry
{
	public LogChannel Channel { get; init; }
	public LogSeverity Severity { get; init; }
	public LogCategory Category { get; init; }
	public string Message { get; init; }
	public string EventId { get; init; }
	public ulong PhysicsFrame { get; init; }
	public double GameTime { get; init; }

	public LogEntry(
		LogChannel channel,
		string message,
		LogSeverity severity = LogSeverity.Info,
		LogCategory category = LogCategory.General,
		string eventId = "")
	{
		Channel = channel;
		Message = message ?? "";
		Severity = severity;
		Category = category;
		EventId = eventId ?? "";
		PhysicsFrame = GameLog.CurrentPhysicsFrame;
		GameTime = GameLog.CurrentGameTime;
	}
}

public interface ILogSink
{
	void Log(in LogEntry entry);
}

/// <summary>
/// Console log sink that formats and prints logs to the engine console/stdout.
/// Strips BBCode tags so terminal and headless logs remain plain and machine-parsable.
/// </summary>
public sealed class ConsoleLogSink : ILogSink
{
	private static readonly System.Text.RegularExpressions.Regex BbcodeRegex =
		new(@"\[/?.*?\]", System.Text.RegularExpressions.RegexOptions.Compiled);

	public void Log(in LogEntry entry)
	{
		string cleanMessage = string.IsNullOrEmpty(entry.Message)
			? ""
			: (entry.Message.IndexOf('[') >= 0 ? BbcodeRegex.Replace(entry.Message, "") : entry.Message);

		string formatted = entry.Channel switch
		{
			LogChannel.Player => $"[Player] {cleanMessage}",
			LogChannel.Internal => $"[Internal:{entry.Category}] {cleanMessage}",
			LogChannel.Debug => $"[Debug:{entry.Category}] ({entry.EventId}) {cleanMessage}",
			_ => cleanMessage
		};

		if (entry.Severity == LogSeverity.Error)
		{
			PrintErr(formatted);
		}
		else
		{
			Print(formatted);
		}
	}

	private static void Print(string message)
	{
		try { GD.Print(message); } catch { Console.WriteLine(message); }
	}

	private static void PrintErr(string message)
	{
		try { GD.PrintErr(message); } catch { Console.Error.WriteLine(message); }
	}
}

/// <summary>
/// Central facade for gameplay and diagnostic logging. Dispatches messages across
/// Player, Internal, and Debug channels to registered sinks without owning gameplay state.
/// </summary>
public static class GameLog
{
	private static readonly object Lock = new();
	private static readonly List<ILogSink> Sinks = new();
	private static ILogSink[] _sinksSnapshot = [];
	private static readonly ConsoleLogSink DefaultConsoleSink = new();

	public static bool IsPlayerEnabled { get; set; } = true;
	public static bool IsInternalEnabled { get; set; } = false;
	public static bool IsDebugEnabled { get; set; } = false;
	public static bool IsConsoleSinkEnabled { get; set; } = true;

	public static Func<ulong> FrameProvider { get; set; }
	public static Func<double> TimeProvider { get; set; }

	public static ulong CurrentPhysicsFrame => FrameProvider != null
		? FrameProvider()
		: (Engine.GetMainLoop() != null ? Engine.GetPhysicsFrames() : 0);

	public static double CurrentGameTime => TimeProvider != null
		? TimeProvider()
		: (Engine.GetMainLoop() != null ? Time.GetTicksMsec() / 1000.0 : 0.0);

	static GameLog()
	{
		ResetToDefaults();
	}

	public static void ResetToDefaults()
	{
		lock (Lock)
		{
			Sinks.Clear();
			Sinks.Add(DefaultConsoleSink);
			_sinksSnapshot = Sinks.ToArray();
			IsPlayerEnabled = true;
			IsInternalEnabled = false;
			IsDebugEnabled = false;
			IsConsoleSinkEnabled = true;
		}
	}

	public static void AddSink(ILogSink sink)
	{
		if (sink == null)
		{
			return;
		}

		lock (Lock)
		{
			if (!Sinks.Contains(sink))
			{
				Sinks.Add(sink);
				_sinksSnapshot = Sinks.ToArray();
			}
		}
	}

	public static void RemoveSink(ILogSink sink)
	{
		if (sink == null)
		{
			return;
		}

		lock (Lock)
		{
			if (Sinks.Remove(sink))
			{
				_sinksSnapshot = Sinks.ToArray();
			}
		}
	}

	public static void ClearSinks()
	{
		lock (Lock)
		{
			Sinks.Clear();
			_sinksSnapshot = [];
		}
	}

	public static void Player(string message, LogSeverity severity = LogSeverity.Info)
	{
		if (!IsPlayerEnabled || string.IsNullOrEmpty(message))
		{
			return;
		}

		Dispatch(new LogEntry(LogChannel.Player, message, severity, LogCategory.General));
	}

	public static void Internal(string message, LogCategory category = LogCategory.General, LogSeverity severity = LogSeverity.Info)
	{
		if (!IsInternalEnabled || string.IsNullOrEmpty(message))
		{
			return;
		}

		Dispatch(new LogEntry(LogChannel.Internal, message, severity, category));
	}

	public static void Debug(string eventId, string message = "", LogCategory category = LogCategory.General, LogSeverity severity = LogSeverity.Info)
	{
		if (!IsDebugEnabled)
		{
			return;
		}

		Dispatch(new LogEntry(LogChannel.Debug, message, severity, category, eventId));
	}

	private static void Warn(string message)
	{
		try { GD.PushWarning(message); } catch { Console.WriteLine($"Warning: {message}"); }
	}

	private static void Dispatch(in LogEntry entry)
	{
		ILogSink[] activeSinks = _sinksSnapshot;
		List<ILogSink> failedSinks = null;

		for (int i = 0; i < activeSinks.Length; i++)
		{
			ILogSink sink = activeSinks[i];
			if (sink == DefaultConsoleSink && !IsConsoleSinkEnabled)
			{
				continue;
			}

			try
			{
				sink.Log(in entry);
			}
			catch (Exception ex)
			{
				failedSinks ??= new List<ILogSink>();
				failedSinks.Add(sink);
				Warn($"GameLog: sink {sink.GetType().Name} threw an exception and was disabled: {ex.Message}");
			}
		}

		if (failedSinks != null)
		{
			lock (Lock)
			{
				foreach (ILogSink failed in failedSinks)
				{
					Sinks.Remove(failed);
				}
				_sinksSnapshot = Sinks.ToArray();
			}
		}
	}
}