using System.Linq;
using Godot;

/// <summary>
/// The light the player carries: a shadow-casting <see cref="Light"/> ahead of the head, which shadows the player and
/// their gear like a held torch, and an object <see cref="Highlight"/> above. It burns at <see cref="Lit"/> on normal
/// floors or while the player holds a portable light, and at <see cref="Dark"/> otherwise, so held lamps and torches
/// need no light of their own. The light keeps to the player's side of walls: when its spot lies past a wall or door, it
/// stops just in front of it, and the wall shadows the room beyond.
/// </summary>
[GlobalClass]
public partial class PlayerLight : Node3D
{
	/// <summary>Whose held lights relight a dark floor.</summary>
	[Export] public Player Player { get; set; }
	/// <summary>Authored at its spot ahead of the head.</summary>
	[Export] public FlickeringLight Light { get; set; }
	[Export] public OmniLight3D Highlight { get; set; }

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
	/// <summary>Gap the light keeps from a blocking surface.</summary>
	[Export(PropertyHint.Range, "0,1,0.01")] public float WallGap { get; set; } = 0.2f;

	private PlayerLightProfile _lit, _dark;
	private Level _level;
	private Vector3 _spot;
	private PhysicsRayQueryParameters3D _ray;

	/// <summary>Whether the light burns at <see cref="Lit"/>: the floor is lit, or the player holds a light.</summary>
	public bool IsLit => _level?.IsDark != true || HoldsLight;

	private bool HoldsLight => Player?.Inventory?.EquippedItems.Values.Any(entry => entry?.Item is PortableLightItem) == true;

	public override void _EnterTree() => _level = this.GetAncestorOrNull<Level>();

	public override void _Ready()
	{
		if (Light == null || Highlight == null || Lit == null || Dark == null)
		{
			GD.PushError($"{Name}: assign Light, Highlight, Lit and Dark.");
			SetPhysicsProcess(false);
			return;
		}
		ProcessPhysicsPriority = CombatPhysics.MovementPresentation;
		_spot = Light.Position;
		_ray = PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Zero, BlockingLayers);
		this.SubscribeUntilExit(_level, level => level.DarknessChanged += OnDarknessChanged, level => level.DarknessChanged -= OnDarknessChanged);
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
		var profile = IsLit ? Lit : Dark;
		if (profile == null || Light == null || Highlight == null) return;
		Light.Energy = profile.Energy;
		Light.Profile = profile.Flicker;
		Light.OmniRange = profile.Range;
		Light.OmniAttenuation = profile.Attenuation;
		Highlight.LightEnergy = profile.HighlightEnergy;
		Highlight.OmniRange = profile.HighlightRange;
		Highlight.OmniAttenuation = profile.HighlightAttenuation;
		Highlight.Position = Vector3.Up * profile.HighlightHeight;
	}
}