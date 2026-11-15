using System.Linq;
using Godot;

/// <summary>
/// The menu's living backdrop: the dungeon's entrance room, assembled by the game's own room builder and lit like a
/// floor, seen from the game's diagonal but closer and lower, with a hero standing where a run begins. The menu's
/// screens are laid over it and choose which hero stands there.
/// </summary>
public partial class MenuStage : Node3D, ILitWorld
{
	[Export] public WorldLighting Lighting { get; set; }
	/// <summary>The room to build; the entrance room, whose stairs carry the spawn point the hero stands at.</summary>
	[Export] public RoomType RoomType { get; set; }
	[Export] public string Realm { get; set; } = "garrison";
	/// <summary>Picks the room's shape and furnishing; the same seed always builds the same room.</summary>
	[Export] public ulong Seed { get; set; } = 12;
	[Export] public Camera3D Camera { get; set; }
	/// <summary>Heading of the view around the hero, in degrees; 45 looks along the dungeon's diagonal like the game.</summary>
	[Export(PropertyHint.Range, "-180,180,1,suffix:°")] public float YawDegrees { get; set; } = 45;
	/// <summary>How far the view looks down, in degrees; flatter than the game's, so the hero reads as a figure.</summary>
	[Export(PropertyHint.Range, "10,89,1,suffix:°")] public float PitchDegrees { get; set; } = 32;
	/// <summary>Visible height of the orthographic view in metres.</summary>
	[Export(PropertyHint.Range, "4,30,0.5,suffix:m")] public float ViewSize { get; set; } = 9;
	/// <summary>Where the hero appears across the screen, from 0 (left edge) to 1 (right edge); the menu sits on the left.</summary>
	[Export(PropertyHint.Range, "0,1,0.01")] public float HeroScreenX { get; set; } = 0.62f;
	/// <summary>The hero on display, dressed in the class's starting kit and showing it off now and then.</summary>
	[Export] public HeroShowcase Hero { get; set; }
	/// <summary>The hero's light, moved to wherever the hero stands.</summary>
	[Export] public Node3D HeroLight { get; set; }
	/// <summary>Effects that fill the air around the hero, such as drifting embers; moved to wherever the hero stands.</summary>
	[Export] public Node3D Atmosphere { get; set; }
	/// <summary>How far the hero turns away from facing the camera, in degrees.</summary>
	[Export(PropertyHint.Range, "-90,90,1,suffix:°")] public float HeroTurnDegrees { get; set; } = -25;

	/// <summary>Camera distance along the view; orthographic, so it only has to clear the room.</summary>
	private const float ViewDistance = 30;
	private Basis ViewBasis => new Basis(Vector3.Up, Mathf.DegToRad(YawDegrees)) * new Basis(Vector3.Right, -Mathf.DegToRad(PitchDegrees));
	private Vector3 _stand;
	private string _heroClassId;

	public override void _Ready()
	{
		if (RoomType == null || Camera == null || Hero == null)
		{
			GD.PushError($"{Name}: assign the room type, camera and hero.");
			return;
		}

		// Assembly loads the room's pieces by path; settle what the previous scene left behind first.
		ResourceCacheGuard.Settle();
		Room room = RoomAssembler.Assemble(RoomType, Seed, Realm);
		AddChild(room);
		// The hero stands where an arrival steps off the stairs.
		PlayerSpawnPoint arrival = room.FindChildren("*", "", true, false).OfType<PlayerSpawnPoint>().FirstOrDefault();
		_stand = (arrival?.Landing ?? arrival)?.GlobalPosition ?? Vector3.Zero;
		if (HeroLight != null)
		{
			HeroLight.GlobalPosition = _stand;
		}
		if (Atmosphere != null)
		{
			Atmosphere.GlobalPosition = _stand + Atmosphere.Position;
		}
		FrameHero();
		this.SubscribeUntilExit(GetViewport(), viewport => viewport.SizeChanged += FrameHero, viewport => viewport.SizeChanged -= FrameHero);
	}

	/// <summary>Stands <paramref name="characterClass"/>'s hero on the stage in its starting kit; a hero already there stays.</summary>
	public void ShowHero(CharacterClass characterClass)
	{
		if (characterClass == null || characterClass.Id == _heroClassId)
		{
			return;
		}

		_heroClassId = characterClass.Id;
		Vector3 toCamera = ViewBasis.Z with { Y = 0 };
		Hero.GlobalPosition = _stand;
		Hero.Rotation = new Vector3(0, Mathf.Atan2(toCamera.X, toCamera.Z) + Mathf.DegToRad(HeroTurnDegrees), 0);
		Hero.Show(characterClass, HeroShowcase.StartingGear(characterClass));
	}

	/// <summary>Aims the camera so the hero's stand sits at <see cref="HeroScreenX"/> across the screen, whatever its shape.</summary>
	private void FrameHero()
	{
		Basis basis = ViewBasis;
		Vector2 screen = GetViewport().GetVisibleRect().Size;
		float width = ViewSize * screen.X / Mathf.Max(1, screen.Y);
		Vector3 target = _stand + Vector3.Up - basis.X * (HeroScreenX - 0.5f) * width;
		Camera.Projection = Camera3D.ProjectionType.Orthogonal;
		Camera.Size = ViewSize;
		Camera.GlobalTransform = new Transform3D(basis, target + basis.Z * ViewDistance);
	}
}
