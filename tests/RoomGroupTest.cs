namespace RogueGauntlet.Tests;

using System.Linq;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

[TestSuite, RequireGodotRuntime]
public class RoomGroupTest
{
	[TestCase]
	public void ValidateCatchesBadIndicesCyclesDepthOverflowAndRealmMismatches()
	{
		var barracks = TestFactories.Load<RoomType>("res://scenes/levels/rooms/room_types/barracks.tres");

		var emptyGroup = new RoomGroup { Id = "empty" };
		AssertArray(emptyGroup.Validate()).IsNotEmpty();

		var badRoot = new RoomGroup
		{
			Id = "bad_root",
			Members = [new RoomGroupMember { AttachToIndex = 0 }],
		};
		AssertArray(badRoot.Validate()).IsNotEmpty();

		var forwardAttach = new RoomGroup
		{
			Id = "forward",
			Members =
			[
				new RoomGroupMember { AttachToIndex = -1 },
				new RoomGroupMember { AttachToIndex = 1 },
			],
		};
		AssertArray(forwardAttach.Validate()).IsNotEmpty();

		var cyclic = new RoomGroup { Id = "cycle" };
		cyclic.Members = [new RoomGroupMember { AttachToIndex = -1, Options = [cyclic] }];
		AssertArray(cyclic.Validate()).IsNotEmpty();

		var depth3 = new RoomGroup
		{
			Id = "d3",
			Members =
			[
				new RoomGroupMember
				{
					AttachToIndex = -1,
					Options =
					[
						new RoomGroup
						{
							Id = "d2",
							Members =
							[
								new RoomGroupMember
								{
									AttachToIndex = -1,
									Options =
									[
										new RoomGroup
										{
											Id = "d1",
											Members =
											[
												new RoomGroupMember
												{
													AttachToIndex = -1,
													Options = [new RoomGroup { Id = "d0", Members = [new RoomGroupMember { AttachToIndex = -1, Options = [barracks] }] }],
												},
											],
										},
									],
								},
							],
						},
					],
				},
			],
		};
		AssertArray(depth3.Validate()).IsNotEmpty();

		var realmMismatch = new RoomGroup
		{
			Id = "mismatch",
			Realms = ["sewer"],
			Members = [new RoomGroupMember { AttachToIndex = -1, Options = [barracks] }],
		};
		AssertArray(realmMismatch.Validate()).IsNotEmpty();

		var bossExit = TestFactories.Load<RoomGroup>("res://scenes/levels/rooms/room_groups/boss_exit.tres");
		AssertArray(bossExit.Validate()).IsEmpty();
	}

	[TestCase]
	public void BossExitGroupAssemblesAtomicLockedExitClusterAcrossAllThreeRealms()
	{
		var pool = TestFactories.RoomPool();
		var bossExit = TestFactories.Load<RoomGroup>("res://scenes/levels/rooms/room_groups/boss_exit.tres");
		var fit = TestFactories.Load<LargeActorFitProfile>("res://scenes/enemies/forsaken_warden/forsaken_warden_fit.tres");
		AssertObject(fit).IsNotNull();

		foreach (string realm in new[] { "garrison", "sewer", "jungle" })
		{
			var realmPool = pool.ForRealm(realm);
			for (ulong seed = 1; seed <= 6; seed++)
			{
				using var rng = new RandomNumberGenerator { Seed = seed * 1009 };
				var cluster = realmPool.CreateFrom(bossExit, rng, new RoomAssemblyOptions { Realm = realm, RequiredFit = fit });
				try
				{
					string label = $"{realm} seed {seed}";
					AssertInt(cluster.Members.Count).OverrideFailureMessage(label).IsEqual(2);

					var boss = cluster.Members[0];
					var exit = cluster.Members[1];
					AssertString(boss.SlotId).IsEqual("boss");
					AssertBool(boss.Obligations == (RoomObligations.MustExist | RoomObligations.MustTraverse)).IsTrue();
					AssertBool(boss.ReserveAccess).IsFalse();

					AssertString(exit.SlotId).IsEqual("exit");
					AssertBool(exit.Obligations == RoomObligations.MustExist).IsTrue();
					AssertBool(exit.ReserveAccess).IsTrue();
					AssertInt(exit.Room.Doors.Count).OverrideFailureMessage($"{label}: exit must have 1 physical door").IsEqual(1);
					var (_, exitDoor) = exit.Room.Doors.Single();
					AssertBool(exitDoor.Secret?.IsSecret != true).IsTrue();
					AssertInt(exit.Room.Map.Tiles.Length > 0 ? 1 : 0).IsEqual(1);

					// Exactly one 1-tile Corridor bridge with sealed perpendicular walls connects Boss and Exit.
					int bridgeTiles = 0;
					for (int x = 0; x < cluster.Map.Width; x++)
					for (int z = 0; z < cluster.Map.Height; z++)
					{
						if (cluster.Map.IsCorridor(x, z)) bridgeTiles++;
					}
					AssertInt(bridgeTiles).OverrideFailureMessage($"{label}: expected 1-tile bridge").IsEqual(1);

					// Only Boss carries external doorways into the cluster; Exit has none.
					var external = cluster.GetExternalConnectors();
					AssertInt(external.Count).OverrideFailureMessage($"{label}: Boss needs >=1 external doorway").IsGreaterEqual(1);
					foreach (var (connTile, _) in external)
					{
						var localInExit = connTile - exit.Offset;
						bool insideExit = exit.Room.Map.IsWithinBounds(localInExit.X, localInExit.Y)
							&& exit.Room.Map.IsConnector(localInExit.X, localInExit.Y);
						AssertBool(insideExit).OverrideFailureMessage($"{label}: Exit must not expose external connectors").IsFalse();
					}

					// 4 quarter turns preserve cluster dimensions and external connector count.
					int beforeCount = external.Count;
					var beforeSize = new Vector2I(cluster.Map.Width, cluster.Map.Height);
					cluster.Rotate(4);
					AssertBool(new Vector2I(cluster.Map.Width, cluster.Map.Height) == beforeSize).IsTrue();
					AssertInt(cluster.GetExternalConnectors().Count).IsEqual(beforeCount);
				}
				finally
				{
					var rooms = cluster.Members.Select(m => m.Room).ToArray();
					cluster.Free();
					foreach (var room in rooms)
						AssertBool(GodotObject.IsInstanceValid(room)).IsFalse();
				}
			}
		}
	}
}
