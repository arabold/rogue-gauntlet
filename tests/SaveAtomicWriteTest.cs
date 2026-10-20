namespace RogueGauntlet.Tests;

using System;
using System.IO;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Exercises the same file replacement used by saves, in an isolated temporary directory.</summary>
[TestSuite]
public class SaveAtomicWriteTest
{
	[TestCase]
	public void SuccessfulReplacementWritesTheCompleteNewPayload()
	{
		string directory = Path.Combine(Path.GetTempPath(), "rogue-save-test-" + Guid.NewGuid());
		Directory.CreateDirectory(directory);
		try
		{
			string path = Path.Combine(directory, "slot.json");
			File.WriteAllText(path, "old payload");
			SaveService.WriteAtomically(path, "complete new payload");
			AssertString(File.ReadAllText(path)).IsEqual("complete new payload");
			AssertBool(File.Exists(path + ".tmp")).IsFalse();
		}
		finally { Directory.Delete(directory, true); }
	}

	[TestCase]
	public void FailedPreparationPreservesTheLastValidSave()
	{
		string directory = Path.Combine(Path.GetTempPath(), "rogue-save-test-" + Guid.NewGuid());
		Directory.CreateDirectory(directory);
		try
		{
			string path = Path.Combine(directory, "slot.json");
			File.WriteAllText(path, "last valid payload");
			Directory.CreateDirectory(path + ".tmp");
			bool failed = false;
			try { SaveService.WriteAtomically(path, "replacement"); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { failed = true; }
			AssertBool(failed).IsTrue();
			AssertString(File.ReadAllText(path)).IsEqual("last valid payload");
		}
		finally { Directory.Delete(directory, true); }
	}
}