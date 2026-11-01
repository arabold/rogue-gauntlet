namespace RogueGauntlet.Tests;

using System.Linq;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Production catalogs offer furnished compact and medium rooms, alongside their larger landmark spaces.</summary>
[TestSuite, RequireGodotRuntime]
public class RoomSizeMixTest
{
	[TestCase("garrison")]
	[TestCase("sewer")]
	[TestCase("jungle")]
	public void EveryRealmProducesSmallAndMediumRoomsAfterFurnishing(string realm)
	{
		using var factory = (AssembledRoomFactory)TestFactories.RoomPool().ForRealm(realm);
		int small = 0, medium = 0, large = 0;
		foreach (var type in factory.RoomTypes)
		for (ulong seed = 1; seed <= 12; seed++)
		{
			RoomBlueprint blueprint = null;
			try
			{
				blueprint = RoomAssembler.Build(type, seed, new RoomAssemblyOptions { Realm = realm });
				AssertBool(blueprint.IsAcceptable).OverrideFailureMessage($"{type.Id} seed {seed}").IsTrue();
				if (blueprint.Footprint.Count <= 9) small++;
				else if (blueprint.Footprint.Count <= 16) medium++;
				else large++;
			}
			finally { blueprint?.Room.Free(); }
		}
		int total = small + medium + large;
		string label = $"{realm}: small={small}, medium={medium}, large={large}";
		AssertFloat(small / (float)total).OverrideFailureMessage(label).IsGreaterEqual(.2f);
		AssertFloat(medium / (float)total).OverrideFailureMessage(label).IsGreaterEqual(.25f);
		AssertFloat(large / (float)total).OverrideFailureMessage(label).IsLessEqual(.4f);
	}

	/// <summary>The crypt's required tombs and lights get space before optional altar decoration.</summary>
	[TestCase]
	public void CompactCryptKeepsItsSarcophagiAndCandles()
	{
		var type = TestFactories.Load<RoomType>("res://scenes/levels/rooms/room_types/crypt.tres");
		foreach (ulong seed in new ulong[] { 1, 3, 64, 310, 424272928054322227 })
		{
			var blueprint = RoomAssembler.Build(type, seed, new RoomAssemblyOptions { Realm = "garrison" });
			try
			{
				AssertBool(blueprint.IsAcceptable).OverrideFailureMessage($"crypt seed {seed}").IsTrue();
				AssertInt(blueprint.Pieces.Count(piece => piece.Prop.Id is "sarcophagus" or "sarcophagus_robbed")).IsGreaterEqual(1);
				AssertInt(blueprint.Pieces.Count(piece => piece.Prop.Id is "candle_pile" or "candelabra" or "brazier")).IsGreaterEqual(2);
			}
			finally { blueprint.Room.Free(); }
		}
	}
}