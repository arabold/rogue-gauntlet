using Godot;
using System.Linq;

/// <summary>
/// Authored playground for production item operations. Definitions and creation policy are
/// assigned in the Inspector; runtime identity and outcome are readable in the Remote tree.
/// </summary>
public partial class ItemLifecycleHarness : CanvasLayer
{
	[Export] public Level Level { get; set; }
	[Export] public PackedScene PickupScene { get; set; }
	[Export] public ItemCreationPolicy CreationPolicy { get; set; }
	[Export] public Godot.Collections.Array<Item> Definitions { get; set; } = new();
	[Export] public ulong RunSeed { get; set; } = 42;
	[Export(PropertyHint.Range, "1,20")] public uint Depth { get; set; } = 1;
	[Export(PropertyHint.Range, "1,100")] public int Quantity { get; set; } = 1;
	public string LastResult { get; private set; } = "";
	public ulong LevelSeed => GameSession.GetLevelSeed(RunSeed, Depth);
	private uint _spawnIndex;
	private OptionButton _selection;
	private Label _status;

	public override void _Ready()
	{
		if (Level.MapGenerator.Seed != LevelSeed || Level.MapGenerator.DungeonDepth != Depth)
		{
			Level.ConfigureGeneration(LevelSeed, Depth);
			Level.Generate();
		}
		_selection = GetNode<OptionButton>("Panel/Controls/Selection");
		_status = GetNode<Label>("Panel/Controls/Status");
		foreach (var definition in Definitions) { _selection.AddItem(definition.Name); }
		GetNode<Button>("Panel/Controls/Spawn").Pressed += SpawnSelected;
		GetNode<Button>("Panel/Controls/Fill").Pressed += FillBackpack;
		SetStatus($"Seed {RunSeed}, depth {Depth}. Spawn supplies, then use the backpack to eat, equip, split or drop.");
	}

	/// <summary>Uses the same factory, pickup scene, and local world ownership as dungeon rewards.</summary>
	public void SpawnSelected()
	{
		Player player = GetTree().GetFirstNodeInGroup("player") as Player;
		if (player == null || Definitions.Count == 0) { SetStatus("No player or definitions."); return; }
		string source = $"harness:{_spawnIndex++}";
		using var rng = GenerationRandom.Create(LevelSeed, GenerationRandom.Items, source);
		var entries = ItemFactory.CreateBatch(Definitions[_selection.Selected], Quantity, CreationPolicy, Depth, rng, source);
		for (int i = 0; i < entries.Count; i++)
		{
			var entry = entries[i];
			var pickup = PickupScene.Instantiate<LootableItem>();
			pickup.Item = entry.Item; pickup.Quantity = entry.Quantity;
			Level.AddWorldNode(pickup, player.GlobalPosition + new Vector3(2 + i * 0.5f, 0.5f, 0));
		}
		SetStatus($"Created {Quantity} × {Definitions[_selection.Selected].Name} from {source}. Inspect RuntimeWorld in the Remote tree.");
	}

	/// <summary>Exercises the normal capacity check with individual food pieces.</summary>
	public void FillBackpack()
	{
		Player player = GetTree().GetFirstNodeInGroup("player") as Player;
		Item food = Definitions.OfType<FoodItem>().FirstOrDefault();
		if (player == null || food == null) { return; }
		while (!player.Inventory.IsFull) { player.InventoryController.PickupItem(ItemFactory.Create(food)); }
		SetStatus($"Backpack filled: {player.Inventory.Items.Count}/{player.Inventory.Capacity}. Try collecting another item.");
	}

	private void SetStatus(string message) { LastResult = message; if (_status != null) { _status.Text = message; } }
}
