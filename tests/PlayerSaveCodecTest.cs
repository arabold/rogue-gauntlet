namespace RogueGauntlet.Tests;

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Same-version player capture and restore through the codec the session uses for saves and stairs.</summary>
[TestSuite, RequireGodotRuntime]
public class PlayerSaveCodecTest
{
	[TestCase]
	public async Task CapturedPlayerRestoresExactlyAndARejectedRecordChangesNothing()
	{
		using var runner = TestFactories.LoadScene("res://scenes/main/main.tscn");
		await runner.SimulateFrames(5);
		var player = runner.Scene().GetTree().GetNodesInGroup("player").OfType<Player>().Single();
		player.Stats.Gold = 37;
		player.Stats.Xp = 5;
		player.Stats.BaseStrength = 13;
		var saved = PlayerSaveCodec.Capture(player, includeTransform: true);
		var data = JsonSerializer.Deserialize<PlayerSaveData>(JsonSerializer.Serialize(saved));
		Vector3 position = player.GlobalPosition;

		player.Stats.Gold = 0;
		player.Stats.BaseStrength = 1;
		player.GlobalPosition += new Vector3(4, 0, 0);
		PlayerSaveCodec.Apply(player, data);
		AssertInt(player.Stats.Gold).IsEqual(37);
		AssertInt(player.Stats.Xp).IsEqual(5);
		AssertFloat(player.Stats.BaseStrength).IsEqual(13);
		AssertVector(player.GlobalPosition).IsEqual(position);
		AssertString(JsonSerializer.Serialize(PlayerSaveCodec.Capture(player, includeTransform: true)))
			.IsEqual(JsonSerializer.Serialize(saved));

		// Level travel leaves placement to the next floor's arrival.
		AssertBool(PlayerSaveCodec.Capture(player, includeTransform: false).ApplyTransform).IsFalse();

		data.Inventory.Capacity = 0;
		player.Stats.Gold = 5;
		bool rejected = false;
		try { PlayerSaveCodec.Apply(player, data); } catch (InvalidOperationException) { rejected = true; }
		AssertBool(rejected).IsTrue();
		AssertInt(player.Stats.Gold).IsEqual(5);
		await runner.SimulateFrames(1);
	}

	[TestCase]
	public void RuntimeCopyKeepsEverySavedValue()
	{
		var stats = new PlayerStats { Health = 12, Xp = 3, XpLevel = 2, Gold = 9, BaseDexterity = 14, BaseArmor = 2, GoldModifier = 1.5f };
		var copy = stats.CreateRuntimeCopy();
		AssertBool(copy != stats).IsTrue();
		AssertString(JsonSerializer.Serialize(copy.CreateSaveData())).IsEqual(JsonSerializer.Serialize(stats.CreateSaveData()));
	}
}