using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Records a whole playtest, across floors, into one JSONL file for <c>simulate.py playtest</c>: the combat trace
/// (attacks, contacts, damage, defenses, healing, monster deaths) plus the game events a balance review needs — each floor
/// entered with its planned monsters and loot, items picked up, used, equipped, dropped and identified, XP, level-ups and
/// the hero's death, with a snapshot of the hero on every floor and level-up. It lives under the tree root, so floor
/// changes, which reload the gameplay scene, do not end it. Started from the debug menu's Playtest tool, or with
/// <c>-- --playtest-log[=PATH]</c>.
/// </summary>
public partial class PlaytestRecorder : Node
{
	/// <summary>
	/// Trace events too frequent or too cosmetic to matter for balance. Monster decisions (<c>ai.</c>) and action starts and
	/// interruptions stay: they explain why a monster attacked less often than its cooldowns allow.
	/// </summary>
	private static readonly string[] IgnoredEvents =
		["effect.", "action.phase", "action.hyper_armor", "slots.", "attack.aim_locked", "attack.facing_committed", "projectile.homing_lost"];

	/// <summary>The recording in progress, or null.</summary>
	public static PlaytestRecorder Current { get; private set; }

	/// <summary>Path of the file being written, known as soon as the recording starts.</summary>
	public string OutputPath => ProjectSettings.GlobalizePath(_path);

	private string _path;
	private Level _startLevel;
	/// <summary>The run in play, so the log can say which item types the player had identified.</summary>
	private RunState _run;
	private CombatTraceRecorder _trace;
	private Player _hero;

	/// <summary>
	/// Starts recording <paramref name="level"/> and every floor after it to <paramref name="path"/>, or to a new file under
	/// <c>user://playtests/</c>; returns the recording.
	/// </summary>
	public static PlaytestRecorder Start(SceneTree tree, Level level, string path = "")
	{
		if (Current != null) return Current;
		if (string.IsNullOrEmpty(path))
		{
			DirAccess.MakeDirRecursiveAbsolute("user://playtests");
			path = $"user://playtests/playtest-{Time.GetDatetimeStringFromSystem().Replace(':', '-')}.jsonl";
		}
		Current = new PlaytestRecorder { Name = nameof(PlaytestRecorder), _path = path, _startLevel = level };
		// The root may be busy adding the scene that asked for this.
		tree.Root.CallDeferred(Node.MethodName.AddChild, Current);
		return Current;
	}

	/// <summary>Ends the recording in progress and closes its file.</summary>
	public static void Stop()
	{
		Current?.QueueFree();
		Current = null;
	}

	public override void _Ready()
	{
		_trace = new CombatTraceRecorder { Name = "Trace", CaptureFrames = false, EchoEffectsToConsole = false, IgnoredEventPrefixes = IgnoredEvents };
		AddChild(_trace);
		_trace.Start(GetTree().Root, GameSession.Instance?.Run.RunSeed ?? 0, _path);
		GD.Print($"Recording playtest to {OutputPath}");

		this.SubscribeUntilExit(SignalBus.Instance, bus =>
		{
			bus.LevelLoaded += OnLevelLoaded;
			bus.PlayerSpawned += OnPlayerSpawned;
			bus.ItemPickedUp += OnItemPickedUp;
			bus.ItemConsumed += OnItemConsumed;
			bus.ItemEquipped += OnItemEquipped;
			bus.ItemUnequipped += OnItemUnequipped;
			bus.ItemDropped += OnItemDropped;
			bus.ItemDestroyed += OnItemDestroyed;
			bus.ItemIdentityRevealed += OnItemIdentityRevealed;
			bus.XpRewarded += OnXpRewarded;
			bus.PlayerLeveledUp += OnPlayerLeveledUp;
			bus.PlayerDeath += OnPlayerDeath;
		}, bus =>
		{
			bus.LevelLoaded -= OnLevelLoaded;
			bus.PlayerSpawned -= OnPlayerSpawned;
			bus.ItemPickedUp -= OnItemPickedUp;
			bus.ItemConsumed -= OnItemConsumed;
			bus.ItemEquipped -= OnItemEquipped;
			bus.ItemUnequipped -= OnItemUnequipped;
			bus.ItemDropped -= OnItemDropped;
			bus.ItemDestroyed -= OnItemDestroyed;
			bus.ItemIdentityRevealed -= OnItemIdentityRevealed;
			bus.XpRewarded -= OnXpRewarded;
			bus.PlayerLeveledUp -= OnPlayerLeveledUp;
			bus.PlayerDeath -= OnPlayerDeath;
		});

		// Started mid-floor (or from the command line, after the first floor loaded): describe the floor and hero in play.
		if (IsInstanceValid(_startLevel) && _startLevel.Floor != null) OnLevelLoaded(_startLevel);
		_startLevel = null;
		if (GetTree().GetFirstNodeInGroup("player") is Player hero) OnPlayerSpawned(hero);
	}

	/// <summary>The trace's clock counts unpaused play time.</summary>
	public override void _PhysicsProcess(double delta) => _trace?.AdvanceClock(delta);

	public override void _ExitTree()
	{
		if (Current == this) Current = null;
	}

	private void OnLevelLoaded(Level level)
	{
		_run = level.Run;
		var monsters = new List<Dictionary<string, object>>();
		int nests = 0;
		foreach (var spawn in level.MapGenerator.EnemySpawnPoints)
		{
			if (level.Floor.IsNest(spawn)) { nests++; continue; }
			if (spawn.Scenes.Count == 0 || level.Run.IsCleared(spawn.PersistentId)) continue;
			var definition = RealmEncounterFactory.DefinitionOf(spawn.Scenes[0]);
			monsters.Add(new()
			{
				["id"] = spawn.PersistentId, ["monster"] = definition?.DisplayName ?? spawn.Scenes[0].ResourcePath,
				["threat"] = definition?.ThreatLevel, ["archetype"] = definition?.Archetype.ToString(),
			});
		}
		var nodes = level.FindChildren("*", "", true, false);
		CombatTrace.Write(level, "floor.entered", ("depth", level.Depth), ("seed", level.Seed),
			("realm", level.MapGenerator.CurrentRealm?.Id), ("monsters", monsters), ("nests", nests),
			("chests", nodes.OfType<Chest>().Count()),
			("loose_items", nodes.OfType<LootableItem>().Where(pickup => pickup.Item != null).Select(pickup => Describe(pickup.Item, pickup.Quantity)).ToList()));
	}

	private void OnPlayerSpawned(Player hero)
	{
		_hero = hero;
		Snapshot("spawned");
	}

	private void OnItemPickedUp(Player player, Item item, int quantity) => CombatTrace.Write(player, "item.picked_up", ("item", Describe(item, quantity)));
	private void OnItemConsumed(Player player, ConsumableItem item) => CombatTrace.Write(player, "item.consumed", ("item", Describe(item, 1)));
	private void OnItemEquipped(Player player, EquipableItem item) => CombatTrace.Write(player, "item.equipped", ("item", Describe(item, 1)));
	private void OnItemUnequipped(Player player, EquipableItem item) => CombatTrace.Write(player, "item.unequipped", ("item", Describe(item, 1)));
	private void OnItemDropped(Player player, Item item, int quantity) => CombatTrace.Write(player, "item.dropped", ("item", Describe(item, quantity)));
	private void OnItemDestroyed(Player player, Item item, int quantity) => CombatTrace.Write(player, "item.destroyed", ("item", Describe(item, quantity)));

	private void OnItemIdentityRevealed(string typeId, string disguisedName, string trueName, string source) =>
		CombatTrace.Write(this, "item.identified", ("type_id", typeId), ("disguised", disguisedName), ("name", trueName), ("by", source));

	private void OnXpRewarded(int xp)
	{
		if (IsInstanceValid(_hero)) CombatTrace.Write(_hero, "hero.xp", ("xp", xp), ("total", _hero.Stats?.Xp));
	}

	private void OnPlayerLeveledUp(int level) => Snapshot("leveled_up");
	private void OnPlayerDeath(Player player) => Snapshot("died");

	/// <summary>Writes the hero's level, health, attributes, equipment and pack.</summary>
	private void Snapshot(string reason)
	{
		if (!IsInstanceValid(_hero) || _hero.Stats == null) return;
		var stats = _hero.Stats;
		var inventory = _hero.Inventory;
		var attack = stats.ResolveAttack(inventory?.EquippedWeapon, inventory?.EquippedOffhandWeapon);
		CombatTrace.Write(_hero, "hero.snapshot", ("reason", reason), ("class", GameSession.Instance?.ActiveCharacterClass?.Id),
			("level", stats.XpLevel), ("xp", stats.Xp), ("health", _hero.HealthComponent?.CurrentHealth), ("max_health", stats.MaxHealth),
			("mana", stats.Mana), ("max_mana", stats.MaxMana), ("armor", stats.Armor), ("speed", stats.Speed),
			("strength", stats.Strength), ("dexterity", stats.Dexterity), ("vitality", stats.Vitality), ("intelligence", stats.Intelligence),
			("min_damage", attack.MinDamage), ("max_damage", attack.MaxDamage), ("crit_chance", attack.CritChance), ("gold", stats.Gold),
			("equipment", inventory?.EquippedItems.Where(pair => pair.Value?.Item != null)
				.ToDictionary(pair => pair.Key.ToString(), pair => Describe(pair.Value.Item, pair.Value.Quantity))),
			("pack", inventory?.Items.Where(slot => slot?.Item != null).Select(slot => Describe(slot.Item, slot.Quantity)).ToList()));
	}

	/// <summary>
	/// An item as the log names it: its definition, real name, kind, identity and whether the player knew that identity
	/// at the time, and for equipment its tier, quality and whether it is cursed.
	/// </summary>
	private Dictionary<string, object> Describe(Item item, int quantity) => new()
	{
		["definition"] = string.IsNullOrEmpty(item.SourceDefinitionPath) ? item.ResourcePath : item.SourceDefinitionPath,
		["name"] = item.Name, ["kind"] = item.GetType().Name, ["quantity"] = quantity,
		["type_id"] = (item as IIdentifiable)?.TypeId, ["form"] = (item as IIdentifiable)?.AppearanceForm,
		["identified"] = item is IIdentifiable { HasIdentity: true } identifiable ? _run?.Identification.IsIdentified(identifiable) : null,
		["tier"] = (item as EquipableItem)?.Tier, ["quality"] = (item as EquipableItem)?.Quality.ToString(),
		["cursed"] = (item as EquipableItem)?.IsCursed,
	};
}
