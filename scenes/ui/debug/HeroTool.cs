using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Actions on the player character: restoring or killing them, and applying a <see cref="HeroBuildProfile"/> from
/// <see cref="BuildDirectory"/>, which replaces their attributes and inventory with an exact, seeded set of items.
/// </summary>
[GlobalClass]
public partial class HeroTool : DebugTool
{
	/// <summary>Folder whose build presets the Builds submenu lists, so a new preset shows up without a menu edit.</summary>
	[Export] public string BuildDirectory { get; set; } = "res://scenes/debug/hero_build_profiles/";

	/// <summary>Creates a build's items with its creation policy. Unassigned, builds are unavailable.</summary>
	[Export] public ItemSpawnTool Items { get; set; }

	protected override void BuildMenu(PopupMenu menu)
	{
		var builds = DebugCatalog.Resources<HeroBuildProfile>(BuildDirectory);
		if (Items != null && builds.Count > 0)
		{
			PopupMenu buildMenu = AddSubmenu(menu, "Builds", "BuildMenu");
			foreach (var (build, path) in builds)
			{
				AddAction(buildMenu, string.IsNullOrEmpty(build.DisplayName) ? path.GetFile().GetBaseName() : build.DisplayName, () => ApplyBuild(build));
				buildMenu.SetItemTooltip(buildMenu.ItemCount - 1, path);
			}
			menu.AddSeparator();
		}
		AddAction(menu, "Restore health", Reset);
		AddAction(menu, "Kill player", Kill);
	}

	/// <summary>
	/// Returns the player to a fresh stance: full health, mana and poise, no action, motion or stagger, and a guard or
	/// dodge needing a fresh press.
	/// </summary>
	public void Reset()
	{
		Player player = Menu?.Player;
		if (player == null)
		{
			return;
		}

		player.ActionGate.ResetForSimulation();
		player.AttackController.CancelAttack();
		player.MovementComponent.Stop();
		player.MovementComponent.Push(Vector3.Zero, 0);
		RefillPools(player);
		player.HurtBoxComponent.Poise?.Restore();
		player.InputController.RequireFreshDefenseInput();
		if (player.GetNode<AnimationTree>("AnimationTree").Get("parameters/playback").AsGodotObject() is AnimationNodeStateMachinePlayback playback)
		{
			playback.Start("Idle");
		}
	}

	/// <summary>Kills the player through its health, as lethal damage would.</summary>
	public void Kill() => Kill(Menu?.Player?.HealthComponent);

	/// <summary>
	/// Replaces the player's attributes and inventory with <paramref name="build"/>'s, then refills health and mana.
	/// Every entry is checked and created before the player changes, so a bad build leaves them untouched. Affixes roll
	/// from a stream of the world's run seed, the build's path and the entry's position, so a build gives the same
	/// items for the same seed. The player keeps their model: the run's class is unchanged.
	/// </summary>
	public bool ApplyBuild(HeroBuildProfile build)
	{
		Player player = Menu?.Player;
		if (build == null || player?.Inventory == null || Items == null)
		{
			GD.PrintErr("Cannot apply a hero build without a build, an active player and an item tool.");
			return false;
		}
		HeroBuildItem[] entries = [.. build.Equipment, .. build.Backpack];
		if (Validate(build, entries, player.Inventory.Capacity) is { } problem)
		{
			GD.PrintErr($"{build.ResourcePath}: {problem}");
			return false;
		}

		var items = new Item[entries.Length];
		ulong runSeed = Menu.DebugWorld?.RunSeed ?? RunState.DefaultSeed;
		for (int index = 0; index < entries.Length; index++)
		{
			HeroBuildItem entry = entries[index];
			using var rng = GenerationRandom.Create(runSeed, "debug.hero_build", $"{build.ResourcePath}#{index}");
			items[index] = Items.Create(entry.Definition, entry.Spell, entry.Quality, entry.Cursed, rng);
			if (items[index] == null)
			{
				GD.PrintErr($"{build.ResourcePath}: {entry.Definition.Name} could not be created.");
				return false;
			}
		}

		player.AttackController.CancelAttack();
		EmptyInventory(player);
		ApplyAttributes(build, player.Stats);
		for (int index = 0; index < entries.Length; index++)
		{
			bool equip = index < build.Equipment.Length;
			if (!player.Inventory.TryAdd(items[index], equip ? 1 : entries[index].Quantity).Success)
			{
				GD.PrintErr($"{build.ResourcePath}: {items[index].Name} does not fit in the backpack.");
				continue;
			}
			if (equip)
			{
				InventoryItemSlot slot = player.Inventory.Items.First(entry => ReferenceEquals(entry.Item, items[index]));
				if (!player.Inventory.Equip(slot, entries[index].Slot).Success)
				{
					GD.PrintErr($"{build.ResourcePath}: {items[index].Name} could not be worn in {entries[index].Slot}.");
				}
			}
		}
		RefillPools(player);
		return true;
	}

	/// <summary>
	/// The first reason <paramref name="build"/> cannot be applied, or null when every entry is valid. Spells are checked
	/// when the items are created; slots by the inventory's own rules, plus what only a whole build can get wrong.
	/// </summary>
	private static string Validate(HeroBuildProfile build, HeroBuildItem[] entries, int capacity)
	{
		if (entries.Any(entry => entry?.Definition == null))
		{
			return "every entry needs an item definition.";
		}
		if (entries.Length > capacity)
		{
			return $"{entries.Length} entries do not fit a backpack of {capacity}.";
		}
		if (entries.FirstOrDefault(entry => entry.Quantity < 1 || entry.Quantity > 1 && !entry.Definition.CanStack) is { } stack)
		{
			return $"{stack.Definition.Name} cannot be packed {stack.Quantity} times.";
		}

		var slots = new HashSet<EquipmentSlot>();
		foreach (HeroBuildItem entry in build.Equipment)
		{
			if (entry.Definition is not EquipableItem equipable || !Inventory.CanEquip(equipable, entry.Slot).Success)
			{
				return $"{entry.Definition.Name} cannot be worn in {entry.Slot}.";
			}
			if (!slots.Add(entry.Slot))
			{
				return $"two items are worn in {entry.Slot}.";
			}
		}
		if (build.Equipment.Any(entry => entry.Definition is Weapon { IsTwoHanded: true }) && slots.Contains(EquipmentSlot.ShieldHand))
		{
			return $"a two-handed weapon leaves no {EquipmentSlot.ShieldHand} free.";
		}
		return null;
	}

	/// <summary>Takes off and removes everything, curses included, as restoring a save does.</summary>
	private static void EmptyInventory(Player player)
	{
		player.ActionManager.ClearActions();
		player.Inventory.ReleaseAll();
		player.Inventory.Items.Clear();
	}

	private static void ApplyAttributes(HeroBuildProfile build, PlayerStats stats)
	{
		build.CharacterClass?.ApplyToStats(stats);
		if (!build.OverrideAttributes)
		{
			return;
		}
		stats.BaseStrength = build.Strength;
		stats.BaseDexterity = build.Dexterity;
		stats.BaseVitality = build.Vitality;
		stats.BaseIntelligence = build.Intelligence;
	}

	private static void RefillPools(Player player)
	{
		player.HealthComponent.SetHealth(player.Stats.MaxHealth, player.Stats.MaxHealth);
		player.ManaComponent.SetMana(player.Stats.MaxMana, player.Stats.MaxMana);
	}
}
