using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Headless probe behind the balance model's supply check (<c>simulate.py supply</c>). It generates seeded floors for each
/// depth, finds every loot source on them (loose pickups, chests, monsters, the gatekeeper) and samples each source's
/// loot many times through the game's own <see cref="LootTableComponent.GenerateOutcome(LootDropProfile, uint, RandomNumberGenerator, string)"/>,
/// then writes the average items per floor as JSON and quits. Nest summons are not counted.
/// <code>godot --headless --path . res://scenes/debug/balance/loot_supply_probe.tscn -- --supply-out=FILE [--supply-depths=1-3] [--supply-seeds=12] [--supply-samples=40]</code>
/// </summary>
public partial class LootSupplyProbe : Node
{
	[Export] public PackedScene LevelScene { get; set; }

	public override async void _Ready()
	{
		var args = OS.GetCmdlineUserArgs().Select(arg => arg.Split('=', 2)).Where(pair => pair.Length == 2)
			.ToDictionary(pair => pair[0].TrimStart('-'), pair => pair[1]);
		string output = args.GetValueOrDefault("supply-out", "user://loot_supply.json");
		string[] range = args.GetValueOrDefault("supply-depths", "1-3").Split('-');
		uint firstDepth = uint.Parse(range[0]), lastDepth = uint.Parse(range[^1]);
		int seeds = int.Parse(args.GetValueOrDefault("supply-seeds", "12"));
		int samples = int.Parse(args.GetValueOrDefault("supply-samples", "40"));

		var level = LevelScene.Instantiate<Level>();
		level.AutoGenerateOnReady = false;
		AddChild(level);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var report = new Godot.Collections.Dictionary();
		for (uint depth = firstDepth; depth <= lastDepth; depth++)
		{
			var perFloor = new Dictionary<string, double>();
			for (int seed = 1; seed <= seeds; seed++)
			{
				level.ConfigureGeneration(new RunState((ulong)seed), depth, null, null);
				level.Generate();
				Count(level, depth, samples, perFloor);
			}
			var row = new Godot.Collections.Dictionary();
			foreach (var (key, total) in perFloor.OrderBy(pair => pair.Key)) row[key] = total / seeds;
			report[depth.ToString()] = row;
		}

		using var file = FileAccess.Open(output, FileAccess.ModeFlags.Write);
		file.StoreString(Json.Stringify(report, "\t"));
		GD.Print($"Loot supply written to {output}");
		GetTree().Quit();
	}

	/// <summary>Adds one generated floor's expected items to <paramref name="totals"/>.</summary>
	private static void Count(Level level, uint depth, int samples, Dictionary<string, double> totals)
	{
		foreach (var pickup in level.FindChildren("*", "", true, false).OfType<LootableItem>())
			Add(totals, pickup.Item, pickup.Quantity);
		foreach (var chest in level.FindChildren("*", "", true, false).OfType<Chest>())
			Sample(totals, chest.LootTable?.Profile, depth, samples, chest.GetPath());
		foreach (var spawn in level.MapGenerator.EnemySpawnPoints)
		{
			if (spawn.Scenes.Count == 0 || level.Floor.IsNest(spawn)) continue;
			var definition = RealmEncounterFactory.DefinitionOf(spawn.Scenes[0]);
			Sample(totals, definition?.Loot, depth, samples, spawn.PersistentId);
			totals["monsters"] = totals.GetValueOrDefault("monsters") + 1;
		}
	}

	private static void Sample(Dictionary<string, double> totals, LootDropProfile profile, uint depth, int samples, string sourceId)
	{
		if (profile == null) return;
		for (int i = 0; i < samples; i++)
		{
			using var rng = GenerationRandom.Create((ulong)i, GenerationRandom.Loot, sourceId);
			foreach (var record in LootTableComponent.GenerateOutcome(profile, depth, rng, $"{sourceId}:{i}"))
				Add(totals, record.Item, record.Quantity / (double)samples);
		}
	}

	private static void Add(Dictionary<string, double> totals, Item item, double quantity)
	{
		if (item == null) return;
		string key = item switch
		{
			Gold => "gold",
			Weapon or Armor when item is EquipableItem equipment => $"gear:T{equipment.Tier}",
			EquipableItem => "jewelry",
			FoodItem => "food",
			IdentifiableItem identifiable when !string.IsNullOrEmpty(identifiable.TypeId) =>
				string.IsNullOrEmpty(identifiable.AppearanceForm) ? identifiable.TypeId : $"{identifiable.TypeId}:{identifiable.AppearanceForm}",
			_ => item.GetType().Name,
		};
		totals[key] = totals.GetValueOrDefault(key) + quantity;
	}
}
