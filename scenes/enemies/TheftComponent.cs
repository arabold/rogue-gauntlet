using System.Linq;
using Godot;

/// <summary>
/// Lets a monster steal from the hero. Its first melee hit that damages the hero takes one random item stack the hero is
/// not wearing from the pack. While it carries loot the monster runs out of the hero's reach instead of fighting
/// (<see cref="EnemyContext.EvaluateMovement"/>); once it has stayed out of the hero's sight or reach for
/// <see cref="EscapeTime"/>, it vanishes with the loot for good. Killing it first drops the loot at its feet. The nymph
/// steals this way.
/// </summary>
[GlobalClass]
public partial class TheftComponent : Node
{
	[Export] public EnemyBase Actor { get; set; }
	[Export] public AttackController AttackController { get; set; }
	[Export] public HealthComponent Health { get; set; }
	/// <summary>The world pickup the loot drops as when the thief dies.</summary>
	[Export] public PackedScene LootableItemScene { get; set; }
	/// <summary>How far from the hero the thief runs while it carries loot; farther counts as out of reach.</summary>
	[Export(PropertyHint.Range, "2,40,0.5,suffix:m")] public float EscapeDistance { get; set; } = 12;
	/// <summary>Seconds out of the hero's sight or reach before the thief vanishes with its loot.</summary>
	[Export(PropertyHint.Range, "0.5,20,0.1,suffix:s")] public float EscapeTime { get; set; } = 3;
	/// <summary>Optional effect the thief vanishes in; it is removed once the effect ends, or at once without one.</summary>
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
		if (inventory == null) return;
		// Only what the hero carries loose: never worn gear or quest items.
		var candidates = inventory.Items.Where(entry => entry?.Item is { ShowInInventory: true, IsQuestItem: false }
			&& !inventory.IsEquipped(entry) && inventory.CanRemove(entry, entry.Quantity).Success).ToList();
		if (candidates.Count == 0) return;
		var stolen = candidates[random.RandiRange(0, candidates.Count - 1)];
		_loot = stolen.Item;
		_quantity = stolen.Quantity;
		_victim = player;
		_unseen = 0;
		CarriedSeconds = 0;
		inventory.RemoveItem(stolen, _quantity);
		_loot.OnDropped(player, _quantity);
		SignalBus.EmitItemStolen(player, _loot, _quantity, ThiefName);
		if (CombatTrace.Enabled) CombatTrace.Write(Actor, "theft.stole", ("item", _loot.DefinitionPath), ("quantity", _quantity));
		SetPhysicsProcess(true);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!IsCarrying || _vanished) return;
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
		if (Actor.HasMeta("persistent_id") && _world is Level level) level.Run.MarkCleared(Actor.GetMeta("persistent_id").AsString());
		Actor.EnemyBehaviorComponent.AIEnabled = false;
		Actor.MovementComponent.Stop();
		Actor.CollisionLayer = 0;
		Actor.HurtBoxComponent.Invulnerable = true;
		var host = Actor.WorldEffects;
		var result = VanishEffect != null && host != null
			? host.PlayBurstConfigured(VanishEffect, (uint)Actor.GetInstanceId(), instance =>
			{
				instance.Finished += Remove;
				return true;
			})
			: WorldEffectResult.Rejected;
		if (result != WorldEffectResult.Started) Remove();
	}

	private void Remove()
	{
		if (GodotObject.IsInstanceValid(Actor)) Actor.QueueFree();
	}

	/// <summary>A thief killed before it escapes drops its loot at its feet, saved with the floor like any dropped item.</summary>
	private void OnDied()
	{
		if (!IsCarrying || _vanished) return;
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
