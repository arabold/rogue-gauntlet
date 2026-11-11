namespace RogueGauntlet.Tests;

using System.Linq;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Compact rooms still fit their defining content. The realms' mix of compact, medium and large rooms is checked over
/// every assembled room in <see cref="RoomAssemblyTest"/>.
/// </summary>
[TestSuite, RequireGodotRuntime]
public class RoomSizeMixTest
{
	/// <summary>The crypt's required tombs and lights get space before optional altar decoration.</summary>
	[TestCase]
	public void CompactCryptKeepsItsSarcophagiAndCandles()
	{
		var type = TestFactories.Load<RoomType>("res://scenes/levels/rooms/room_types/crypt.tres");
		var candles = type.Steps.Single(step => step.Id == "candles").Content.OfType<Prop>().ToHashSet();
		foreach (ulong seed in new ulong[] { 1, 3, 64, 310, 424272928054322227 })
		{
			var blueprint = RoomAssembler.Build(type, seed, new RoomAssemblyOptions { Realm = "garrison" });
			try
			{
				AssertBool(blueprint.IsAcceptable).OverrideFailureMessage($"crypt seed {seed}").IsTrue();
				AssertInt(blueprint.Pieces.Count(piece => piece.Prop.Id is "sarcophagus" or "sarcophagus_robbed")).IsGreaterEqual(1);
				AssertInt(blueprint.Pieces.Count(piece => candles.Contains(piece.Prop))).IsGreaterEqual(2);
			}
			finally { blueprint.Room.Free(); }
		}
	}
}