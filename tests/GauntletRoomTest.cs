namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Trap gauntlet rooms, as the room assembler builds them and as the arena stages them for play.</summary>
[TestSuite]
[RequireGodotRuntime]
public partial class GauntletRoomTest
{
	private const string GauntletRooms = "res://scenes/levels/rooms/room_types/gauntlets";
	private static readonly ulong[] Seeds = [1, 2, 3, 4, 5, 6];

	private static IEnumerable<Node> Descendants(Node node) =>
		node.GetChildren().SelectMany(child => Descendants(child).Prepend(child));

	/// <summary>
	/// Every gauntlet room puts all its hazards under one gauntlet, in rows that span the room: spouts hang on the side
	/// walls firing straight across, blades swing within the walls and never into each other, and no blocking furniture
	/// stands in a row.
	/// </summary>
	[TestCase]
	public void EveryGauntletRoomLaysRowsTheHeroMustCross()
	{
		var types = DebugCatalog.Resources<RoomType>(GauntletRooms).Select(entry => entry.Resource).ToArray();
		AssertArray(types).IsNotEmpty();
		foreach (var type in types)
		foreach (ulong seed in Seeds)
		{
			string at = $"{type.Id} seed {seed}";
			var blueprint = RoomAssembler.Build(type, seed, new RoomAssemblyOptions { Realm = type.Realms.First() });
			try
			{
				var gauntlets = Descendants(blueprint.Room).OfType<TrapGauntlet>().ToArray();
				AssertInt(gauntlets.Length).OverrideFailureMessage($"{at}: one gauntlet").IsEqual(1);
				var hazards = gauntlets[0].GetChildren().OfType<Node3D>().ToArray();
				AssertBool(hazards.All(hazard => hazard is ITrapHazard)).OverrideFailureMessage($"{at}: only hazards").IsTrue();
				var interior = blueprint.Interior;
				foreach (var spout in hazards.OfType<WallSpout>())
				{
					Vector3 across = spout.Transform.Basis.Z;
					AssertFloat(Mathf.Abs(across.X)).OverrideFailureMessage($"{at}: a spout fires across").IsGreater(.99f);
					float wall = across.X > 0 ? interior.Position.X : interior.End.X;
					AssertFloat(Mathf.Abs(spout.Position.X - wall)).OverrideFailureMessage($"{at}: a spout hangs on a side wall").IsLess(.8f);
				}
				foreach (var blade in hazards.OfType<PendulumBlade>())
				{
					float reach = blade.Sweep + blade.BladeHalfWidth;
					AssertBool(blade.Position.X - reach > interior.Position.X && blade.Position.X + reach < interior.End.X)
						.OverrideFailureMessage($"{at}: a blade swings into a wall").IsTrue();
					foreach (var other in hazards.OfType<PendulumBlade>().Where(other => other != blade
						&& Mathf.IsEqualApprox(other.Position.Z, blade.Position.Z) && other.Position.X > blade.Position.X))
						AssertBool(other.Position.X - other.Sweep - other.BladeHalfWidth >= blade.Position.X + reach - .01f)
							.OverrideFailureMessage($"{at}: neighbouring blades collide").IsTrue();
				}
				var rows = hazards.Select(hazard => hazard.Position.Z).Distinct().ToArray();
				AssertInt(rows.Length).OverrideFailureMessage($"{at}: at least two rows").IsGreaterEqual(2);
				var blockers = blueprint.Pieces.Where(piece => piece.Prop.Blocking && piece.Prop.Mount == PropMount.Floor);
				foreach (var piece in blockers)
				foreach (float row in rows)
					AssertBool(piece.Envelope.Position.Z > row + .5f || piece.Envelope.End.Z < row - .5f)
						.OverrideFailureMessage($"{at}: {piece.Prop.Id} stands in a row").IsTrue();
			}
			finally
			{
				blueprint.Room.Free();
			}
		}
	}

	/// <summary>A room staged in the arena puts its entrance ahead of the hero, and its gauntlet wakes for them.</summary>
	[TestCase]
	public async Task AStagedRoomWakesItsGauntletForTheHeroAtItsEntrance()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 4);
		harness.Player.InputController.SetPhysicsProcess(false);
		TestFactories.MovePracticeTargetAway(harness);
		var traps = TestFactories.Tool<TrapTool>(runner.Scene());
		var room = traps.StageRoom(TestFactories.Load<RoomType>($"{GauntletRooms}/pendulum_hall.tres"));
		AssertObject(room).IsNotNull();
		var gauntlet = Descendants(room).OfType<TrapGauntlet>().Single();
		await TestFactories.AwaitPhysicsUntil(runner, () => gauntlet.IsAwake, 2);
		Vector3 ahead = harness.Player.MovementComponent.LookAtDirection with { Y = 0 };
		var nearest = gauntlet.GetChildren().OfType<Node3D>().MinBy(hazard => hazard.GlobalPosition.DistanceTo(harness.Player.GlobalPosition));
		AssertFloat((nearest.GlobalPosition - harness.Player.GlobalPosition).Dot(ahead.Normalized())).IsGreater(0);
	}
}
