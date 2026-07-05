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

	[Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";

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
	/// health, since MaxHealth is derived from the new Vitality.
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
	}

	/// <summary>
	/// Builds a fresh runtime inventory from the starting items; the authored slot
	/// definitions are never mutated.
	/// </summary>
	public Inventory CreateStartingInventory(int capacity)
	{
		var inventory = new Inventory { Capacity = capacity };
		foreach (InventoryItemSlot slot in StartingItems)
		{
			if (slot?.Item != null)
			{
				inventory.Items.Add(slot.CreateRuntimeCopy());
			}
		}

		return inventory;
	}
}
