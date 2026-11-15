using System.Linq;
using Godot;

/// <summary>
/// A local point light makes nearby objects cast shadows away from the hero; a separate fill keeps the character readable.
/// Normal floors use a broad, subdued falloff. In darkness, Lit carries a held light and Dark provides innate visibility.
/// The point light stays on the player's side of walls and doors in every mode.
/// </summary>
[GlobalClass]
public partial class PlayerLight : Node3D
{
	/// <summary>Whose held lights relight a dark floor.</summary>
	[Export] public Player Player { get; set; }
	/// <summary>Authored at its spot ahead of the head, without specular: riding just in front of walls, it would
	/// streak them with highlights. Fixtures keep theirs for metal.</summary>
	[Export] public FlickeringLight Light { get; set; }
	[Export] public OmniLight3D Highlight { get; set; }

	[Export]
	public PlayerLightProfile Normal
	{
		get => _normal;
		set { _normal = value; if (IsNodeReady()) Apply(); }
	}

	[Export]
	public PlayerLightProfile Lit
	{
		get => _lit;
		set { _lit = value; if (IsNodeReady()) Apply(); }
	}

	[Export]
	public PlayerLightProfile Dark
	{
		get => _dark;
		set { _dark = value; if (IsNodeReady()) Apply(); }
	}

	/// <summary>What the light stays in front of: walls, and doors on the props layer.</summary>
	[Export(PropertyHint.Layers3DPhysics)] public uint BlockingLayers { get; set; } = 2 | 16;
	/// <summary>Gap the light keeps from a blocking surface. Closer, the wall face nearest to it blows out into a bright
	/// streak; from about 0.4 the wall takes an even glow.</summary>
	[Export(PropertyHint.Range, "0,1,0.01")] public float WallGap { get; set; } = 0.4f;

	private PlayerLightProfile _normal, _lit, _dark;
	private WorldLighting _lighting;
	private Vector3 _spot;
	private PhysicsRayQueryParameters3D _ray;

	/// <summary>Whether full illumination is available: the world is lit, or the player holds a light.</summary>
	public bool IsLit => _lighting?.IsDark != true || HoldsLight;

	private bool HoldsLight => Player?.Inventory?.EquippedItems.Values.Any(entry => entry?.Item is PortableLightItem) == true;

	/// <summary>The world this light is carried in: a level, an arena or a workbench; none leaves it lit.</summary>
	public override void _EnterTree() => _lighting = this.GetAncestorOrNull<ILitWorld>()?.Lighting;

	public override void _Ready()
	{
		if (Light == null || Highlight == null || Normal == null || Lit == null || Dark == null)
		{
			GD.PushError($"{Name}: assign Light, Highlight, Normal, Lit and Dark.");
			SetPhysicsProcess(false);
			return;
		}
		ProcessPhysicsPriority = CombatPhysics.MovementPresentation;
		_spot = Light.Position;
		_ray = PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Zero, BlockingLayers);
		this.SubscribeUntilExit(_lighting, lighting => lighting.DarknessChanged += OnDarknessChanged, lighting => lighting.DarknessChanged -= OnDarknessChanged);
		this.SubscribeUntilExit(Player?.Inventory,
			inventory => { inventory.ItemEquipped += OnEquipmentChanged; inventory.ItemUnequipped += OnEquipmentChanged; },
			inventory => { inventory.ItemEquipped -= OnEquipmentChanged; inventory.ItemUnequipped -= OnEquipmentChanged; });
		Apply();
	}

	/// <summary>Moves the light back along its line from above the player's center when a wall or door cuts it.</summary>
	public override void _PhysicsProcess(double delta)
	{
		var above = new Vector3(0, _spot.Y, 0);
		_ray.From = ToGlobal(above);
		_ray.To = ToGlobal(_spot);
		var hit = GetWorld3D().DirectSpaceState.IntersectRay(_ray);
		float reach = hit.Count == 0 ? 1f
			: Mathf.Max(0f, _ray.From.DistanceTo(hit["position"].AsVector3()) - WallGap) / _ray.From.DistanceTo(_ray.To);
		Light.Position = above.Lerp(_spot, reach);
	}

	private void OnDarknessChanged(bool isDark) => Apply();

	private void OnEquipmentChanged(EquipableItem item, EquipmentSlot slot) => Apply();

	private void Apply()
	{
		bool isDark = _lighting?.IsDark == true;
		var profile = isDark ? (IsLit ? Lit : Dark) : Normal;
		if (profile == null || Light == null || Highlight == null) return;
		Light.Visible = profile.Energy > 0f;
		Light.Energy = profile.Energy;
		Light.Profile = profile.Flicker;
		Light.OmniRange = profile.Range;
		Light.OmniAttenuation = profile.Attenuation;
		Light.LightCullMask = profile.LightLayers;
		Highlight.LightEnergy = profile.HighlightEnergy;
		Highlight.OmniRange = profile.HighlightRange;
		Highlight.OmniAttenuation = profile.HighlightAttenuation;
		Highlight.LightCullMask = profile.HighlightLayers;
		Highlight.Position = Vector3.Up * profile.HighlightHeight;
	}
}