using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The root of a harness arena: a flat floor outside any generated level that the debug tools act in. Monster navigation
/// is a clearance grid over <see cref="Bounds"/> built from the arena's static collision, rebuilt when an optional
/// <see cref="Stage"/> changes its wall arrangement or a harness changes its <see cref="Structure"/>. It lights through
/// the game's <see cref="WorldLighting"/>.
/// </summary>
public partial class DebugArena : Node3D, IDebugWorld
{
	/// <summary>How far a free standing spot is searched for around a requested position.</summary>
	private const float FreeSearchRadius = 6f;
	/// <summary>Room a standing spot needs: the player's capsule radius.</summary>
	private const float StandingRadius = .5f;

	/// <summary>Tuning for monster navigation. Unassigned, spawned monsters cannot navigate and hold still.</summary>
	[Export] public NavigationProfile NavigationProfile { get; set; }

	/// <summary>The walkable floor in world XZ: navigation covers it, solid collision inside it blocks.</summary>
	[Export] public Rect2 Bounds { get; set; } = new(-20, -20, 40, 40);

	/// <summary>Optional stage whose arrangements toggle walls; navigation follows each change.</summary>
	[Export] public DebugStage Stage { get; set; }

	/// <summary>
	/// Optional node holding staged structure, such as the room harness's room, whose collision blocks navigation. A
	/// harness that changes it calls <see cref="RebuildNavigation"/>.
	/// </summary>
	[Export] public Node3D Structure { get; set; }

	/// <summary>
	/// Where navigation finds floor, such as the tiles of a staged room and its corridors; null makes all of
	/// <see cref="Bounds"/> floor. A harness that changes it calls <see cref="RebuildNavigation"/>.
	/// </summary>
	public IReadOnlyList<Rect2> FloorAreas { get; set; }

	/// <summary>The arena's illumination, the game's own, so the arena looks and darkens like a dungeon floor.</summary>
	[Export] public WorldLighting Lighting { get; set; }

	/// <summary>Depth equipment spawned here rolls at.</summary>
	[Export] public uint Depth { get; set; } = 1;

	/// <summary>Seeds spawned monsters' combat streams; a harness sets it from its own seed so runs replay.</summary>
	[Export] public ulong RunSeed { get; set; } = RunState.DefaultSeed;

	private readonly Dictionary<NavigationLayer, FloorNavigation> _navigation = [];

	public FloorNavigation NavigationFor(NavigationLayer layer) => _navigation.GetValueOrDefault(layer);

	private FloorNavigation Ground => NavigationFor(NavigationLayer.Ground);

	public override void _Ready()
	{
		if (NavigationProfile == null)
		{
			GD.PushError($"{Name}: assign a NavigationProfile; spawned monsters cannot navigate.");
		}
		if (Stage != null)
		{
			Stage.ArrangementChanged += RebuildNavigation;
		}
		RebuildNavigation();
	}

	/// <summary>Adds <paramref name="node"/> as a direct child, positioned before it enters the tree as spawn points do.</summary>
	public void AddWorldNode(Node3D node, Vector3 globalPosition)
	{
		node.Position = ToLocal(globalPosition);
		AddChild(node);
		node.ResetPhysicsInterpolation();
	}

	public Vector3 FindFreePositionNear(Vector3 origin) =>
		IsFree(origin) ? origin
		: Ground.TryFindTraversable(origin, StandingRadius, FreeSearchRadius, out Vector3 spot) ? spot : origin;

	public Vector3 FindDropLanding(Vector3 source, Vector3 direction, float distance)
	{
		if (!direction.TryNormalizeHorizontal(out Vector3 heading))
		{
			return source;
		}
		Vector3 landing = source + heading * distance;
		return IsFree(landing) ? landing : source;
	}

	/// <summary>
	/// Builds navigation from the arena's static bodies, its stage's walls and its structure: a ground grid, and an air
	/// grid that solids no taller than <see cref="ClearanceGrid.AirClearanceHeight"/> do not block. Monsters in the arena
	/// read them from the arena, so rebuilt grids reach them without being handed over.
	/// </summary>
	public void RebuildNavigation()
	{
		if (NavigationProfile == null)
		{
			return;
		}

		var shared = new ClearanceGrid(Bounds);
		foreach (var area in FloorAreas ?? [Bounds]) shared.AddFloor(area);
		// Only the floor, the boundaries, the stage and the structure are solid: actors, the HUD and spawns never are.
		var solids = GetChildren().OfType<Node3D>().Where(child => child is StaticBody3D || child == Stage || child == Structure).ToList();
		IEnumerable<SolidOutline> Outlines(uint layers) =>
			solids.SelectMany(solid => CollisionOutlines.FromStaticBodies(solid, solid.GlobalTransform, layers));
		const uint walking = CollisionOutlines.WalkingTerrainLayer;
		var (ground, air) = ClearanceGrid.BuildLayers(shared, Outlines(CollisionOutlines.BlockingLayers & ~walking), [], [], Outlines(walking));
		_navigation[NavigationLayer.Ground] = new FloorNavigation(ground, NavigationProfile);
		_navigation[NavigationLayer.Air] = new FloorNavigation(air, NavigationProfile);
	}

	/// <summary>Whether a body can stand at <paramref name="point"/>; anywhere when the arena has no navigation.</summary>
	private bool IsFree(Vector3 point) => Ground == null || Ground.IsTraversable(point, StandingRadius);
}
