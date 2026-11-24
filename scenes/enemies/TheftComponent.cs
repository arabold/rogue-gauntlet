using System.Linq;
using Godot;

/// <summary>
/// Lets a monster steal from the hero. Its first melee hit that damages the hero takes one random item stack the hero is
/// not wearing from the pack. While it carries loot its combat profile runs it out of the hero's reach instead of
/// fighting (a <see cref="RetreatWhileStrategy"/> on <see cref="CarryingLootCondition"/>); once it has stayed out of the
/// hero's sight or reach for <see cref="EscapeTime"/>, it leaves with the loot for good
/// (<see cref="DeathComponent.Leave"/>). Killing it first drops the loot at its feet. The nymph steals this way.
/// </summary>
[GlobalClass]
public partial class TheftComponent : Node
{
	[Export] public EnemyBase Actor { get; set; }
	[Export] public AttackController AttackController { get; set; }
	[Export] public HealthComponent Health { get; set; }
	/// <summary>The world pickup the loot drops as when the thief dies.</summary>
	[Export] public PackedScene LootableItemScene { get; set; }
	/// <summary>
	/// Farther from the hero than this counts as out of reach. The thief's retreat decides how far it runs
	/// (<see cref="RetreatWhileStrategy.Distance"/>); keep that at least this far, or it waits in the hero's reach.
	/// </summary>
	[Export(PropertyHint.Range, "2,40,0.5,suffix:m")] public float EscapeDistance { get; set; } = 12;
	/// <summary>Seconds out of the hero's sight or reach before the thief vanishes with its loot.</summary>
	[Export(PropertyHint.Range, "0.5,20,0.1,suffix:s")] public float EscapeTime { get; set; } = 3;
	/// <summary>Optional effect the thief vanishes in (<see cref="DeathComponent.Leave"/>).</summary>
	[Export] public WorldEffectDefinition VanishEffect { get; set; }

	/// <summary>Within this margin of <see cref="EscapeDistance"/> the hero still counts as in reach, so a thief waiting at the edge does not flicker.</summary>
	private const float ReachMargin = 0.5f;

	private Item _loot;
	private int _quantity;
	private Player _victim;
	private float _unseen;
	private bool _vanished;
	private IDebugWorld _world;

	/// <summary>Whether the thief carries a stolen item stack.</summary>
	public bool IsCarrying => _loot != null;

	/// <summary>Seconds since the theft while the thief carries its loot.</summary>
	public float CarriedSeconds { get; private set; }

	public override void _EnterTree() => _world = this.GetAncestorOrNull<IDebugWorld>();

	public override void _Ready()
	{
		SetPhysicsProcess(false);
		if (Actor == null || AttackController == null || Health == null || LootableItemScene == null)
		{
			GD.PushError($"{GetParent()?.Name}/{Name}: assign Actor, AttackController, Health and LootableItemScene; the monster cannot steal.");
			return;
		}
		AttackController.ContactDamaged += OnContactDamaged;
		Health.Died += OnDied;
	}

	private void OnContactDamaged(Node3D target, RandomNumberGenerator random)
	{
		if (IsCarrying || _vanished || Health.IsDead) return;
		var player = target as Player ?? target.GetAncestorOrNull<Player>();
		var inventory = player?.Inventory;
		if (inventory == null || player.InventoryController == null) return;
		// Only what the hero carries loose: never worn gear or quest items.
		var candidates = inventory.Items.Where(entry => entry?.Item is { ShowInInventory: true, IsQuestItem: false }
			&& !inventory.IsEquipped(entry) && inventory.CanRemove(entry, entry.Quantity).Success).ToList();
		if (candidates.Count == 0) return;
		var stolen = candidates[random.RandiRange(0, candidates.Count - 1)];
		Item item = stolen.Item;
		int quantity = stolen.Quantity;
		// The stack changes hands inside one inventory commit, as a sale does.
		if (!player.InventoryController.TryHandOver(stolen, quantity, () => item.OnDropped(player, quantity)).Success) return;
		_loot = item;
		_quantity = quantity;
		_victim = player;
		_unseen = 0;
		CarriedSeconds = 0;
		SignalBus.EmitItemStolen(player, _loot, _quantity, ThiefName);
		if (CombatTrace.Enabled) CombatTrace.Write(Actor, "theft.stole", ("item", _loot.DefinitionPath), ("quantity", _quantity));
		SetPhysicsProcess(true);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!IsCarrying) return;
		CarriedSeconds += (float)delta;
		bool inReach = GodotObject.IsInstanceValid(_victim) && !_victim.IsDead
			&& Actor.GlobalPosition.DistanceTo(_victim.GlobalPosition) < EscapeDistance - ReachMargin
			&& WorldSight.IsClear(Actor.GetWorld3D(), Actor.GlobalPosition + Vector3.Up, _victim.GlobalPosition + Vector3.Up);
		_unseen = inReach ? 0 : _unseen + (float)delta;
		if (_unseen >= EscapeTime) Vanish();
	}

	/// <summary>Leaves with the loot for good: no death, no rewards, and a reloaded floor does not bring the thief back.</summary>
	private void Vanish()
	{
		_vanished = true;
		SetPhysicsProcess(false);
		SignalBus.EmitThiefEscaped(_loot, _quantity, ThiefName);
		if (CombatTrace.Enabled) CombatTrace.Write(Actor, "theft.escaped", ("item", _loot.DefinitionPath), ("quantity", _quantity));
		_loot = null;
		Actor.DeathComponent.Leave(VanishEffect);
	}

	/// <summary>A thief killed before it escapes drops its loot at its feet, saved with the floor like any dropped item.</summary>
	private void OnDied()
	{
		if (!IsCarrying) return;
		SetPhysicsProcess(false);
		var pickup = LootableItemScene.Instantiate<LootableItem>();
		pickup.Item = _loot;
		pickup.Quantity = _quantity;
		_loot = null;
		Vector3 position = Actor.GlobalPosition;
		if (_world == null)
		{
			GD.PushError($"{Actor.Name}: no world to drop its stolen loot into.");
			pickup.Free();
			return;
		}
		pickup.ThrowFrom(position, 0);
		Vector3 forward = (-Actor.GlobalBasis.Z with { Y = 0 }).Normalized();
		_world.AddWorldNode(pickup, _world.FindDropLanding(position, forward, 1));
	}

	private string ThiefName => Actor.Definition?.DisplayName ?? Actor.Name.ToString();
}
