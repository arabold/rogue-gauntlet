using Godot;
using System.Linq;

/// <summary>
/// The root of a harness arena: a flat floor outside any generated level that the debug tools act in. Monster navigation
/// is a clearance grid over <see cref="Bounds"/> built from the arena's static collision, rebuilt when an optional
/// <see cref="Stage"/> changes its wall arrangement. Darkness lowers the ambient and key light.
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

	/// <summary>Environment whose ambient light darkness turns off; unassigned, darkness only dims the key light.</summary>
	[Export] public WorldEnvironment Lighting { get; set; }

	/// <summary>The arena's main light, dimmed while dark.</summary>
	[Export] public DirectionalLight3D KeyLight { get; set; }

	/// <summary>Depth equipment spawned here rolls at.</summary>
	[Export] public uint Depth { get; set; } = 1;

	/// <summary>Seeds spawned monsters' combat streams; a harness sets it from its own seed so runs replay.</summary>
	[Export] public ulong RunSeed { get; set; } = RunState.DefaultSeed;

	/// <summary>Fraction of the key light's energy left while dark.</summary>
	[Export(PropertyHint.Range, "0,1,0.01")] public float DarkKeyLightScale { get; set; } = .12f;

	public FloorNavigation Navigation { get; private set; }

	public bool IsDark { get; private set; }

	private Environment _environment;
	private float _normalAmbient;
	private float _normalKeyLight;

	public override void _Ready()
	{
		if (Lighting?.Environment != null)
		{
			// Own the runtime copy so darkness never mutates the shared environment resource.
			_environment = (Environment)Lighting.Environment.Duplicate();
			Lighting.Environment = _environment;
			_normalAmbient = _environment.AmbientLightEnergy;
		}
		_normalKeyLight = KeyLight?.LightEnergy ?? 0;
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

	public void SetDark(bool isDark)
	{
		IsDark = isDark;
		if (_environment != null) { _environment.AmbientLightEnergy = isDark ? 0 : _normalAmbient; }
		if (KeyLight != null) { KeyLight.LightEnergy = isDark ? _normalKeyLight * DarkKeyLightScale : _normalKeyLight; }
	}

	/// <summary>Adds <paramref name="node"/> as a direct child, positioned before it enters the tree as spawn points do.</summary>
	public void AddWorldNode(Node3D node, Vector3 globalPosition)
	{
		node.Position = ToLocal(globalPosition);
		AddChild(node);
		node.ResetPhysicsInterpolation();
	}

	public Vector3 FindFreePositionNear(Vector3 origin)
	{
		if (IsFree(origin))
		{
			return origin;
		}

		const int samplesPerRing = 12;
		for (float radius = 1; radius <= FreeSearchRadius; radius++)
		{
			for (int i = 0; i < samplesPerRing; i++)
			{
				float angle = Mathf.Tau * i / samplesPerRing;
				Vector3 candidate = origin + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
				if (IsFree(candidate))
				{
					return candidate;
				}
			}
		}
		return origin;
	}

	public Vector3 FindDropLanding(Vector3 source, Vector3 direction, float distance)
	{
		Vector3 heading = direction with { Y = 0 };
		if (heading.IsZeroApprox())
		{
			return source;
		}
		Vector3 landing = source + heading.Normalized() * distance;
		return IsFree(landing) ? landing : source;
	}

	/// <summary>
	/// Builds navigation from the arena's current static collision and moves every monster already in the arena onto it.
	/// </summary>
	private void RebuildNavigation()
	{
		if (NavigationProfile == null)
		{
			return;
		}

		var grid = new ClearanceGrid(Bounds);
		grid.AddFloor(Bounds);
		foreach (SolidOutline outline in CollisionOutlines.FromStaticBodies(this, GlobalTransform))
		{
			grid.AddObstacle(outline);
		}
		grid.Freeze();
		Navigation = new FloorNavigation(grid, NavigationProfile);
		foreach (EnemyBase monster in FindChildren("*", "", true, false).OfType<EnemyBase>())
		{
			monster.EnemyBehaviorComponent?.Navigation?.SetNavigationOverride(Navigation);
		}
	}

	/// <summary>Whether a body can stand at <paramref name="point"/>; anywhere when the arena has no navigation.</summary>
	private bool IsFree(Vector3 point) => Navigation == null || Navigation.Grid.ClearanceAt(new Vector2(point.X, point.Z)) >= StandingRadius;
}
