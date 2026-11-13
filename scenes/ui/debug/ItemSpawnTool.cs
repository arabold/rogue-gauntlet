using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>Where a spawned item goes.</summary>
public enum ItemDelivery
{
	/// <summary>Thrown out in front of the player like a monster drop; the magnet collects it.</summary>
	Drop,
	/// <summary>Straight into the backpack.</summary>
	Backpack,
	/// <summary>Into the backpack and equipped at once, replacing what the slot held.</summary>
	Equip,
}

/// <summary>
/// Spawns authored items for the player, dropped, packed or equipped as <see cref="Delivery"/> says. Every item under the
/// <see cref="Categories"/> folders is listed. Equipment is rolled like a drop at the world's depth, at
/// <see cref="Quality"/> and with <see cref="Cursed"/>, and each wand or staff lists the spells its pool can bind.
/// </summary>
[GlobalClass]
public partial class ItemSpawnTool : DebugTool
{
	/// <summary>How far a spawned item is thrown: the default monster drop distance.</summary>
	private const float DropDistance = 1.5f;

	/// <summary>
	/// Category label to the res:// folder its submenu lists: every <c>.tres</c> item in the folder or its subfolders, so an
	/// item authored into a listed folder shows up without a menu edit. Wands and staves get the
	/// <see cref="SpellWeaponCategories"/> submenus instead.
	/// </summary>
	[Export]
	public Godot.Collections.Dictionary<string, string> Categories { get; set; } = new()
	{
		{ "Weapons", "res://scenes/items/weapons/" },
		{ "Shields", "res://scenes/items/armor/" },
		{ "Potions", "res://scenes/items/potions/" },
		{ "Scrolls", "res://scenes/items/scrolls/" },
		{ "Jewelry", "res://scenes/items/jewelry/" },
		{ "Food", "res://scenes/items/food/food/" },
		{ "Drinks", "res://scenes/items/food/drink/" },
		{ "Gold", "res://scenes/items/gold/" },
		{ "Lamps and torches", "res://scenes/items/tools/lamp/" },
	};

	/// <summary>
	/// Quality ladder and curse penalties for rolling spawned equipment. Without it the quality picker is unavailable and
	/// equipment spawns exactly as saved, apart from a picked spell. Affixes come from each definition's roll table.
	/// </summary>
	[Export] public ItemCreationPolicy CreationPolicy { get; set; }

	/// <summary>Quality forced onto spawned equipment; null rolls it like a drop at the world's depth.</summary>
	public ItemQuality? Quality { get; set; }

	/// <summary>Whether spawned equipment is cursed, with its curse status still unknown.</summary>
	public bool Cursed { get; set; }

	/// <summary>Where menu spawns go; an arena without an inventory screen equips them.</summary>
	[Export] public ItemDelivery Delivery { get; set; } = ItemDelivery.Drop;

	/// <summary>What "Fill backpack" packs; unassigned, the entry is left out.</summary>
	[Export] public Item FillItem { get; set; }

	/// <summary>
	/// Spell weapons are authored among the other weapons but listed in their own submenus, right after the folder they are
	/// found in: one-handed wands apart from the stronger two-handed staves.
	/// </summary>
	private static readonly (string Label, System.Func<Item, bool> Matches)[] SpellWeaponCategories =
	[
		("Wands", item => item is MagicStaff { IsTwoHanded: false }),
		("Staves", item => item is MagicStaff { IsTwoHanded: true }),
	];

	protected override void BuildMenu(PopupMenu menu)
	{
		var categories = new List<(string Label, IEnumerable<(Item Item, string Path)> Entries)>();
		foreach (var (label, directory) in Categories)
		{
			var entries = DebugCatalog.Resources<Item>(directory);
			categories.Add((label, entries.Where(e => e.Resource is not MagicStaff)));
			categories.AddRange(SpellWeaponCategories.Select(c => (c.Label, entries.Where(e => c.Matches(e.Resource)))));
		}
		// Merges spell weapons found in several folders into one submenu, placed where they first appear.
		foreach (var category in categories.GroupBy(c => c.Label, c => c.Entries))
		{
			AddCategory(menu, category.Key, category.SelectMany(entries => entries));
		}

		menu.AddSeparator();
		if (FillItem != null)
		{
			AddAction(menu, $"Fill backpack with {FillItem.Name}", () => FillBackpack(FillItem));
		}
		PopupMenu deliveries = AddSubmenu(menu, "Delivery", "DeliveryMenu");
		foreach (ItemDelivery delivery in System.Enum.GetValues<ItemDelivery>())
		{
			AddRadio(deliveries, delivery.ToString(), () => Delivery == delivery, () => Delivery = delivery);
		}

		if (CreationPolicy?.QualityLadder == null)
		{
			if (CreationPolicy != null) { GD.PushError($"{Name}: the creation policy needs a quality ladder."); }
			return;
		}
		// "Random" rolls the quality like a drop; every other entry forces one.
		PopupMenu qualities = AddSubmenu(menu, "Quality", "QualityMenu");
		AddRadio(qualities, "Random", () => Quality == null, () => Quality = null);
		foreach (ItemQuality quality in System.Enum.GetValues<ItemQuality>())
		{
			AddRadio(qualities, quality.ToString(), () => Quality == quality, () => Quality = quality);
		}
		qualities.AddSeparator();
		AddToggle(qualities, "Cursed", () => Cursed, value => Cursed = value);
	}

	/// <summary>
	/// A runtime copy of <paramref name="definition"/>: equipment is rolled like a drop at the world's depth with
	/// <see cref="Quality"/> (rolled too when null) and <see cref="Cursed"/> forced, and a wand or staff then carries
	/// <paramref name="spell"/>, which the roll would otherwise redraw from its pool. Null when the item cannot carry the spell.
	/// </summary>
	public Item Create(Item definition, SpellDefinition spell = null)
	{
		var rng = new RandomNumberGenerator();
		rng.Randomize();
		return Create(definition, spell, Quality, Cursed, rng);
	}

	/// <summary>
	/// A runtime copy of <paramref name="definition"/> rolled from <paramref name="rng"/>, so a seeded caller such as a hero
	/// build gets the same affixes every time. Without a creation policy, equipment is copied exactly as authored.
	/// </summary>
	public Item Create(Item definition, SpellDefinition spell, ItemQuality? quality, bool cursed, RandomNumberGenerator rng)
	{
		if (definition == null)
		{
			GD.PrintErr("Cannot spawn an item without a definition.");
			return null;
		}
		if (spell != null && (definition is not MagicStaff staffDefinition || !staffDefinition.CanBind(spell)))
		{
			GD.PrintErr($"{definition.Name} cannot carry {spell.DisplayName}.");
			return null;
		}

		Item item = ItemFactory.Create(definition);
		if (CreationPolicy?.QualityLadder != null && item is EquipableItem { RollTable: not null } equipment)
		{
			LootRoller.ApplyRoll(equipment, Menu?.DebugWorld?.Depth ?? 1, rng, CreationPolicy, quality, cursed);
		}
		if (item is MagicStaff staff && spell != null)
		{
			staff.BoundSpell = spell;
		}
		return item;
	}

	/// <summary>Throws <paramref name="item"/> out of a free spot ahead of the player like a monster drop; the magnet collects it.</summary>
	public bool Drop(Item item)
	{
		if (item == null)
		{
			return false;
		}

		Player player = Menu?.Player;
		IDebugWorld world = Menu?.DebugWorld;
		if (player == null || world == null || player.LootableItemScene == null)
		{
			GD.PrintErr("Cannot spawn an item without an active player, world and lootable item scene.");
			return false;
		}

		var pickup = player.LootableItemScene.Instantiate<LootableItem>();
		pickup.Item = item;
		pickup.Quantity = 1;
		Vector3 origin = player.GlobalPosition - player.GlobalTransform.Basis.Z.Normalized() * 2.0f;
		Vector3 source = world.FindFreePositionNear(origin);
		pickup.ThrowFrom(source);
		Vector3 direction = Vector3.Back.Rotated(Vector3.Up, GD.Randf() * Mathf.Tau);
		world.AddWorldNode(pickup, world.FindDropLanding(source, direction, DropDistance));
		return true;
	}

	/// <summary>
	/// Picks <paramref name="item"/> up into the player's backpack and, with <paramref name="equip"/>, equips it, through the
	/// player's own inventory controller as a pickup and an equip from the inventory would. False when it does not fit or
	/// cannot be equipped.
	/// </summary>
	public bool Give(Item item, bool equip)
	{
		Player player = Menu?.Player;
		if (item == null || player?.Inventory == null)
		{
			GD.PrintErr("Cannot give an item without an active player.");
			return false;
		}
		if (equip && item is not EquipableItem)
		{
			GD.PrintErr($"{item.Name} cannot be equipped.");
			return false;
		}
		if (!player.InventoryController.PickupItem(item))
		{
			GD.PrintErr($"{item.Name} does not fit in the backpack.");
			return false;
		}
		if (!equip)
		{
			return true;
		}

		InventoryItemSlot entry = player.Inventory.Items.First(slot => ReferenceEquals(slot.Item, item));
		if (player.InventoryController.TryEquip(entry).Success)
		{
			return true;
		}
		GD.PrintErr($"{item.Name} could not be equipped.");
		return false;
	}

	/// <summary>
	/// Packs copies of <paramref name="definition"/> through the normal pickup path until the backpack is full, so the
	/// next pickup exercises the capacity check. Returns how many were packed.
	/// </summary>
	public int FillBackpack(Item definition)
	{
		Player player = Menu?.Player;
		if (definition == null || player?.Inventory == null)
		{
			GD.PrintErr("Cannot fill a backpack without an item and an active player.");
			return 0;
		}

		// Bounded by the capacity: a stackable item merges into one entry and never fills the backpack.
		int packed = 0;
		while (packed < player.Inventory.Capacity && !player.Inventory.IsFull && player.InventoryController.PickupItem(Create(definition)))
		{
			packed++;
		}
		return packed;
	}

	/// <summary>Delivers <paramref name="item"/> the way <see cref="Delivery"/> says.</summary>
	public bool Deliver(Item item) => Delivery switch
	{
		ItemDelivery.Backpack => Give(item, equip: false),
		ItemDelivery.Equip when item is EquipableItem => Give(item, equip: true),
		ItemDelivery.Equip => Give(item, equip: false),
		_ => Drop(item),
	};

	/// <summary>
	/// Adds one submenu listing <paramref name="entries"/> by name and tier; an empty one is left out. A wand or staff opens
	/// its own submenu of the spells its pool can roll, so the menu offers exactly the variants a dungeon drop can be.
	/// </summary>
	private void AddCategory(PopupMenu menu, string label, IEnumerable<(Item Item, string Path)> entries)
	{
		var sorted = entries.OrderBy(e => e.Item.Name, System.StringComparer.Ordinal)
			.ThenBy(e => (e.Item as EquipableItem)?.Tier ?? 0)
			.ThenBy(e => e.Path, System.StringComparer.Ordinal).ToList();
		if (sorted.Count == 0)
		{
			return;
		}

		PopupMenu category = AddSubmenu(menu, label, $"Spawn{label}Menu");
		string[] labels = DescribeEntries(sorted);
		for (int i = 0; i < sorted.Count; i++)
		{
			var (item, path) = sorted[i];
			if (item is MagicStaff { SpellPool: not null } staff)
			{
				PopupMenu spells = AddSubmenu(category, labels[i]);
				foreach (SpellDefinition spell in staff.SpellPool.Spells.Where(staff.CanBind))
				{
					AddAction(spells, spell.DisplayName, () => Deliver(Create(staff, spell)));
				}
			}
			else
			{
				AddAction(category, labels[i], () => Deliver(Create(item)));
			}
			category.SetItemTooltip(i, path);
		}
	}

	/// <summary>
	/// Display names repeat across tiers and model variants, so equipment shows its tier and any remaining clash adds the
	/// resource name. The full path is in each entry's tooltip.
	/// </summary>
	private static string[] DescribeEntries(List<(Item Item, string Path)> entries)
	{
		static string Describe(Item item) => item is EquipableItem { Tier: > 0 } equipable ? $"{item.Name} · T{equipable.Tier}" : item.Name;
		var collisions = entries.GroupBy(e => Describe(e.Item)).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
		return entries.Select(e => collisions.Contains(Describe(e.Item))
			? $"{Describe(e.Item)} · {e.Path.GetFile().GetBaseName()}"
			: Describe(e.Item)).ToArray();
	}
}
