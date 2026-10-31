using System.Collections.Generic;
using System.Linq;
using Godot;

public enum PropMount { Floor, Wall }

/// <summary>
/// One object: a mesh-library item (with cosmetic variants) or a scene for props that need nodes, such as lights.
/// Convention: after <see cref="QuarterTurns"/> the prop's front faces local +Z and its back faces local -Z, so
/// back-to-wall props stand with -Z against the wall. Size is measured from the model; nothing is hand-authored.
/// </summary>
[Tool, GlobalClass]
public partial class Prop : Placeable
{
	private MeshLibrary _library;
	private string[] _items = [];
	private PackedScene[] _scenes = [];
	private int _quarterTurns;
	private bool _blocking = true;
	private PropGeometry[] _geometry;

	[ExportGroup("Model")]
	[Export] public MeshLibrary Library { get => _library; set { _library = value; _geometry = null; NotifyPropertyListChanged(); } }
	/// <summary>Cosmetic variants from <see cref="Library"/>; each is measured separately, and unused ones are preferred.</summary>
	[Export] public string[] Items { get => _items; set { _items = value ?? []; _geometry = null; } }
	/// <summary>Scene variants, used instead of Items for props that need nodes: lights, particles, interaction.</summary>
	[Export] public PackedScene[] Scenes { get => _scenes; set { _scenes = value ?? []; _geometry = null; } }
	/// <summary>Turns the model so its front faces local +Z.</summary>
	[Export(PropertyHint.Range, "0,3")] public int QuarterTurns { get => _quarterTurns; set { _quarterTurns = value; _geometry = null; } }

	[ExportGroup("Placement")]
	[Export] public PropMount Mount { get; set; }
	/// <summary>Floor props whose back stands flush against a wall: beds, shelves, wall-side tables.</summary>
	[Export] public bool BackToWall { get; set; }
	/// <summary>Wall props: height of the scene root above the floor.</summary>
	[Export(PropertyHint.Range, "0,6,0.05")] public float MountHeight { get; set; } = 2.2f;
	/// <summary>Wall props: recess from the wall's outer trim to its flat mounting surface.</summary>
	[Export(PropertyHint.Range, "0,0.5,0.01")] public float WallInset { get; set; } = .17f;
	/// <summary>Free margin kept to neighbouring props; zero lets sections abut.</summary>
	[Export(PropertyHint.Range, "0,1,0.01")] public float Gap { get; set; } = .2f;
	/// <summary>False for props actors walk over or that rest on other props: rugs, bottles on tables.</summary>
	[Export] public bool Blocking { get => _blocking; set { _blocking = value; _geometry = null; } }
	/// <summary>Keeps the space in front of the prop clear so it can be used.</summary>
	[Export] public bool Interactive { get; set; }
	/// <summary>Bars, fences, corners and posts: structural pieces may overlap each other where they join (a corner over
	/// the ends of two runs), never furniture.</summary>
	[Export] public bool Structural { get; set; }
	/// <summary>Largest random turn, in degrees either way, so loose things (barrels, stools, vases, food) never line up
	/// perfectly. Placement measures the turned prop, so jitter never causes overlaps.</summary>
	[Export(PropertyHint.Range, "0,45,0.5")] public float TurnJitter { get; set; }
	/// <summary>Largest random shift sideways and forward, in units, so props sit a little off the packing grid.</summary>
	[Export(PropertyHint.Range, "0,0.5,0.01")] public float ShiftJitter { get; set; }

	public int VariantCount => Scenes.Length > 0 ? Scenes.Length : Items.Length;
	public Basis Turn => new(Vector3.Up, QuarterTurns * Mathf.Pi / 2);

	/// <summary>Measured once per variant and cached on this shared definition; changing the model clears it.</summary>
	public PropGeometry Geometry(int variant)
	{
		_geometry ??= new PropGeometry[VariantCount];
		return _geometry[variant] ??= Scenes.Length > 0
			? PropGeometry.FromScene(Scenes[variant], Turn, Blocking)
			: PropGeometry.FromLibraryItem(Library, Library.FindItemByName(Items[variant]), Turn, Blocking);
	}

	public override void Compose(RoomBlueprint room, RandomNumberGenerator rng, PlacementPlan plan, Transform3D at, bool optional)
	{
		int variant = room.PickVariant(this, rng);
		plan.Add(this, variant, at * Jitter(rng), optional);
	}

	/// <summary>A seeded nudge about the prop's own origin. Props without jitter draw nothing, keeping other streams stable.</summary>
	private Transform3D Jitter(RandomNumberGenerator rng)
	{
		if (TurnJitter <= 0 && ShiftJitter <= 0) return Transform3D.Identity;
		float turn = Mathf.DegToRad(rng.RandfRange(-TurnJitter, TurnJitter));
		var shift = new Vector3(rng.RandfRange(-ShiftJitter, ShiftJitter), 0, rng.RandfRange(-ShiftJitter, ShiftJitter));
		return new Transform3D(new Basis(Vector3.Up, turn), shift);
	}

	public override string[] Validate()
	{
		var errors = new List<string>();
		if (string.IsNullOrWhiteSpace(Id)) errors.Add("assign a stable ID");
		if ((Scenes.Length == 0) == (Items.Length == 0) || Scenes.Any(scene => scene == null)) errors.Add("assign either mesh-library items or scenes");
		if (Items.Length > 0)
		{
			if (Library == null) errors.Add("assign the mesh library its items come from");
			else errors.AddRange(Items.Where(item => Library.FindItemByName(item) < 0).Select(item => $"item '{item}' is not in {Library.ResourcePath}"));
		}
		if (Mount == PropMount.Wall && BackToWall) errors.Add("wall props are always mounted on a wall; clear BackToWall");
		if (Mount == PropMount.Wall && (TurnJitter > 0 || ShiftJitter > 0)) errors.Add("wall props hang flat on the wall; clear the jitter");
		return errors.Select(error => $"Prop {Id}: {error}.").ToArray();
	}

	/// <summary>Offers the library's item names as a dropdown for each Items entry.</summary>
	public override void _ValidateProperty(Godot.Collections.Dictionary property)
	{
		if (property["name"].AsStringName() != PropertyName.Items || Library == null) return;
		var names = Library.GetItemList().Select(Library.GetItemName).Order();
		property["hint"] = (int)PropertyHint.TypeString;
		property["hint_string"] = $"{(int)Variant.Type.String}/{(int)PropertyHint.Enum}:{string.Join(",", names)}";
	}
}