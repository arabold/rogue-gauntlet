namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Trap gauntlet rooms, as the room assembler builds them and as the room harness stands them for play.</summary>
[TestSuite]
[RequireGodotRuntime]
public partial class GauntletRoomTest
{
	private const string GauntletRooms = "res://scenes/levels/rooms/room_types/gauntlets";
	private static readonly ulong[] Seeds = [1, 2, 3];
	/// <summary>The hero's body across (a 0.5 m capsule radius): no narrower gap lets them through.</summary>
	private const float HeroWidth = 1f;
	/// <summary>How far a plain wall's trim reaches in from its tile edge; ornate walls reach further.</summary>
	private const float WallTrim = .5f;

	private static IEnumerable<T> Find<T>(Node node) => node.FindChildren("*", "", true, false).OfType<T>();

	/// <summary>
	/// Every gauntlet room, in each theme it suits, puts all its hazards under one gauntlet, in rows one tile apart with
	/// none missing that span the room: spouts hang on the side walls firing straight across, blades swing within the walls
	/// and never into each other yet leave no way around them, and no blocking furniture stands in a row.
	/// </summary>
	[TestCase]
	public void EveryGauntletRoomLaysRowsTheHeroMustCross()
	{
		var types = DebugCatalog.Resources<RoomType>(GauntletRooms).Select(entry => entry.Resource).ToArray();
		AssertArray(types).IsNotEmpty();
		foreach (var type in types)
		foreach (string theme in type.Realms)
		foreach (ulong seed in Seeds)
		{
			string at = $"{type.Id} ({theme}) seed {seed}";
			var blueprint = RoomAssembler.Build(type, seed, new RoomAssemblyOptions { Realm = theme });
			try
			{
				var gauntlets = Find<TrapGauntlet>(blueprint.Room).ToArray();
				AssertInt(gauntlets.Length).OverrideFailureMessage($"{at}: one gauntlet").IsEqual(1);
				var hazards = gauntlets[0].GetChildren().OfType<Node3D>().ToArray();
				AssertBool(hazards.All(hazard => hazard is TrapHazard)).OverrideFailureMessage($"{at}: only hazards").IsTrue();
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
				foreach (var row in hazards.OfType<PendulumBlade>().GroupBy(blade => Mathf.Snapped(blade.Position.Z, .01f)))
				{
					// From the wall faces (at least a trim's depth in) across every swing, no gap a hero fits through.
					var edges = row.OrderBy(blade => blade.Position.X)
						.SelectMany(blade => new[] { blade.Position.X - blade.Sweep - blade.BladeHalfWidth, blade.Position.X + blade.Sweep + blade.BladeHalfWidth })
						.Prepend(interior.Position.X + WallTrim).Append(interior.End.X - WallTrim).ToArray();
					for (int gap = 0; gap < edges.Length; gap += 2)
						AssertFloat(edges[gap + 1] - edges[gap]).OverrideFailureMessage($"{at}: a hero walks around the blades of row {row.Key}")
							.IsLess(HeroWidth);
				}
				var rows = hazards.Select(hazard => hazard.Position.Z).Distinct().Order().ToArray();
				AssertInt(rows.Length).OverrideFailureMessage($"{at}: at least two rows").IsGreaterEqual(2);
				AssertBool(rows.Zip(rows.Skip(1)).All(pair => Mathf.IsEqualApprox(pair.Second - pair.First, Room.TileSize, .05f)))
					.OverrideFailureMessage($"{at}: rows one tile apart, none missing: {string.Join(", ", rows)}").IsTrue();
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

	/// <summary>
	/// In the room harness the hero walks from the entrance into a gauntlet room, and its gauntlet wakes for them with its
	/// hazards ahead.
	/// </summary>
	[TestCase]
	public async Task TheHeroWalksIntoAGauntletRoomAndItWakes()
	{
		using var runner = TestFactories.LoadScene(RoomGenerationHarnessTest.Scene);
		var harness = await RoomGenerationHarnessTest.Settle(runner);
		AssertBool(harness.SelectRoom(TestFactories.Load<RoomType>($"{GauntletRooms}/pendulum_hall.tres"))).IsTrue();
		harness.Regenerate();
		var hero = harness.Player;
		var gauntlet = Find<TrapGauntlet>(harness.PreviewRoom).Single();
		// One tile in: through the doorway, short of the first row.
		hero.MovementComponent.SetInputDirection(harness.Inward);
		await TestFactories.AwaitPhysicsUntil(runner, () => (hero.GlobalPosition - harness.Entrance).Dot(harness.Inward) > Room.TileSize, 2);
		hero.MovementComponent.SetInputDirection(Vector3.Zero);
		await TestFactories.AwaitPhysicsUntil(runner, () => gauntlet.IsAwake, 2);
		var nearest = gauntlet.GetChildren().OfType<Node3D>().MinBy(hazard => hazard.GlobalPosition.DistanceTo(hero.GlobalPosition));
		AssertFloat((nearest.GlobalPosition - hero.GlobalPosition).Dot(harness.Inward)).IsGreater(0);
	}
}
