using Godot;
using Godot.Collections;

/// <summary>
/// Designer-authored definition of a playable character class: visual model, attribute
/// block, stat-derivation profile, starting gear, and selector metadata. Selected at the
/// start of a run, persisted per save slot by <see cref="Id"/>, and applied to the player
/// on spawn.
/// </summary>
[GlobalClass]
public partial class CharacterClass : Resource
{
	/// <summary>Stable identifier stored in save files; never rename once shipped.</summary>
	[Export] public string Id { get; set; } = "";

	[Export] public string DisplayName { get; set; } = "";

	/// <summary>The class in a glance for the hero picker: five words at most.</summary>
	[Export] public string Tagline { get; set; } = "";

	/// <summary>
	/// KayKit adventurer model scene. Must expose the shared rig (Skeleton3D with the common
	/// bone and animation names) and an AnimationPlayer named "AnimationPlayer" so the
	/// player's AnimationTree can drive it unchanged.
	/// </summary>
	[Export] public PackedScene CharacterScene { get; set; }

	/// <summary>Attribute-derivation rules for this class (e.g. caster damage from Intelligence).</summary>
	[Export] public StatProfile StatProfile { get; set; }

	[Export] public float BaseStrength { get; set; } = 10;
	[Export] public float BaseDexterity { get; set; } = 10;
	[Export] public float BaseVitality { get; set; } = 10;
	[Export] public float BaseIntelligence { get; set; } = 10;

	/// <summary>
	/// Starting inventory. Auto-equip lets the last equipable item of a slot win, so list
	/// the intended main-hand weapon last.
	/// </summary>
	[Export] public Array<InventoryItemSlot> StartingItems { get; set; } = new();

	/// <summary>
	/// Share of its damage each starting weapon keeps. The kit is worn (Poor quality), so the first weapon the hero finds
	/// is an upgrade and the first floors take more blows; <c>simulate.py derive</c> writes it from the balance model.
	/// </summary>
	[Export(PropertyHint.Range, "0.1,1,0.05")] public float StartingWeaponScale { get; set; } = 1f;

	/// <summary>
	/// Item type ids identified at run start (e.g. "potion.healing") — the hero knows what
	/// they packed, so their own supplies are never disguised.
	/// </summary>
	[Export] public string[] PreIdentifiedTypeIds { get; set; } = System.Array.Empty<string>();

	/// <summary>Looping animation the character-select preview plays.</summary>
	[Export] public string PreviewAnimation { get; set; } = "Idle";

	/// <summary>
	/// Model-specific BoneAttachment3D node names under the skeleton, per attachment type.
	/// The KayKit models name these differently (1H_Axe vs 1H_Sword vs Knife, ...); omit
	/// types the model has no node for — such gear equips without a visual.
	/// </summary>
	[Export] public Dictionary<AttachmentType, string> AttachmentNodeNames { get; set; } = new();

	/// <summary>
	/// Writes the class's profile and attribute block into a runtime stats copy and refills
	/// health and mana, since their maximums derive from the new Vitality and Intelligence.
	/// </summary>
	public void ApplyToStats(PlayerStats stats)
	{
		if (StatProfile != null)
		{
			stats.Profile = StatProfile;
		}

		stats.BaseStrength = BaseStrength;
		stats.BaseDexterity = BaseDexterity;
		stats.BaseVitality = BaseVitality;
		stats.BaseIntelligence = BaseIntelligence;
		stats.Health = stats.MaxHealth;
		stats.Mana = stats.MaxMana;
	}

	/// <summary>
	/// Builds a fresh runtime inventory from the starting items, with worn weapons (<see cref="StartingWeaponScale"/>);
	/// the authored slot definitions are never mutated.
	/// </summary>
	public Inventory CreateStartingInventory(int capacity)
	{
		var inventory = new Inventory { Capacity = capacity, Items = StartingItems }.CreateRuntimeCopy();
		if (StartingWeaponScale >= 1) return inventory;
		foreach (var slot in inventory.Items)
		{
			if (slot.Item is not Weapon weapon) continue;
			weapon.Quality = ItemQuality.Poor;
			weapon.QualityAdjective = "worn";
			weapon.MagnitudeScale = StartingWeaponScale;
		}
		return inventory;
	}
}