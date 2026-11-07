namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Walking actors cross every walkable floor tile, the seams between copies of it and the steps between different
/// tiles of one footprint, without touching anything steep enough to count as a wall. Floor detail is visual; a lip
/// that stops a rat while navigation and the generator call the floor walkable strands monsters in place.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public partial class FloorTraversalTest
{
	private const string FloorLibrary = "res://scenes/levels/dungeon/FloorMeshLibrary.tres";
	private const float RowSpacing = 12f;
	private const float Speed = 6f;
	private const int Copies = 3;
	// Contacts flatter than CharacterBody3D's default 45° floor angle are floors; anything steeper stops movement.
	private static readonly float FloorNormalY = Mathf.Cos(Mathf.DegToRad(46));

	/// <summary>Body radius and height of the smallest (rat) and a medium (skeleton) walking actor.</summary>
	private static readonly (float Radius, float Height)[] Bodies = [(.35f, .75f), (.75f, 2f)];

	private readonly List<(CharacterBody3D Body, string Row, float Radius, float Goal)> _walkers = [];
	private int _row;

	[TestCase]
	public async Task WalkingActorsCrossEveryFloorTileWithoutWallContacts()
	{
		var library = TestFactories.Load<MeshLibrary>(FloorLibrary);
		var root = new Node3D();
		var floor = new GridMap { MeshLibrary = library, CellSize = Vector3.One, CellCenterX = false, CellCenterY = false, CellCenterZ = false };
		root.AddChild(floor);
		var walkable = library.GetItemList().Where(item => IsWalkableFloor(library.GetItemName(item))
			&& library.GetItemShapes(item).Count > 0).ToArray();
		foreach (int item in walkable) AddRow(root, floor, library, Enumerable.Repeat(item, Copies).ToArray());
		// Different tiles of one footprint side by side: the steps between their walkable heights.
		foreach (var group in walkable.GroupBy(item => Footprint(library, item)).Where(group => group.Count() > 1))
			AddRow(root, floor, library, group.ToArray());
		TestFactories.AddToSceneTree(root);
		var tree = root.GetTree();
		await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
		await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

		var blocked = new SortedSet<string>();
		float longest = _walkers.Max(w => w.Goal - w.Body.Position.X);
		int frames = Mathf.CeilToInt(longest / Speed * Engine.PhysicsTicksPerSecond * 1.25f) + 30;
		for (int frame = 0; frame < frames && _walkers.Any(w => w.Body.Position.X < w.Goal); frame++)
		{
			await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
			foreach (var (body, row, radius, goal) in _walkers)
			{
				if (body.Position.X >= goal) continue;
				body.Velocity = new Vector3(Speed, body.IsOnFloor() ? 0 : body.Velocity.Y - .5f, 0);
				body.MoveAndSlide();
				for (int i = 0; i < body.GetSlideCollisionCount(); i++)
				{
					var normal = body.GetSlideCollision(i).GetNormal();
					if (normal.Y < FloorNormalY) blocked.Add($"{row} (r={radius})");
				}
			}
		}
		var stranded = _walkers.Where(w => w.Body.Position.X < w.Goal).Select(w => $"{w.Row} (r={w.Radius})").ToArray();
		AssertArray(blocked.ToArray()).OverrideFailureMessage($"Wall contacts on: {string.Join("; ", blocked)}").IsEmpty();
		AssertArray(stranded).OverrideFailureMessage($"Stranded on: {string.Join("; ", stranded)}").IsEmpty();
	}

	/// <summary>Lays <paramref name="items"/> in a row along +X, one walker per body size crossing all of them.</summary>
	private void AddRow(Node3D root, GridMap floor, MeshLibrary library, int[] items)
	{
		var bounds = library.GetItemMesh(items[0]).GetAabb();
		int step = Mathf.Max(1, Mathf.RoundToInt(bounds.Size.X));
		string row = string.Join(" > ", items.Distinct().Select(library.GetItemName));
		foreach (var (radius, height) in Bodies)
		{
			int z = Mathf.RoundToInt(_row++ * RowSpacing);
			for (int index = 0; index < items.Length; index++) floor.SetCellItem(new Vector3I(index * step, 0, z), items[index]);
			var body = new CharacterBody3D { CollisionLayer = 0, CollisionMask = 1 };
			body.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = radius, Height = height }, Position = Vector3.Up * height / 2 });
			float start = bounds.Position.X + radius + .1f;
			body.Position = new Vector3(start, .3f, z + bounds.GetCenter().Z);
			root.AddChild(body);
			_walkers.Add((body, row, radius, start + (items.Length - 1) * step));
		}
	}

	private static Vector2I Footprint(MeshLibrary library, int item)
	{
		var size = library.GetItemMesh(item).GetAabb().Size;
		return new Vector2I(Mathf.RoundToInt(size.X), Mathf.RoundToInt(size.Z));
	}

	/// <summary>Foundations are raised structure and pool pieces line a pit; every other floor item is walked on.</summary>
	private static bool IsWalkableFloor(string name) => !name.StartsWith("floor_foundation_") && !name.StartsWith("floor_pool_");
}