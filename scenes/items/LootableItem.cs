using Godot;
using System;


/// <summary>
/// Represents an item that can be picked up by the player. Shows a rarity/type-colored loot
/// beam and, when within magnet range, floats to the player and is collected automatically.
/// </summary>
public partial class LootableItem : Node3D
{
	/// <summary>
	/// The item to be picked up.
	/// </summary>
	[Export] public Item Item;
	/// <summary>
	/// The quantity of items that can be picked up.
	/// </summary>
	[Export] public int Quantity = 1;
	/// <summary>
	/// Animate the item.
	/// </summary>
	[Export] public bool Animate = true;

	/// <summary>
	/// Distance at which the item is magnetically pulled to the player (~one floor tile).
	/// Larger than the pickup collider so it also grabs loot that spawns on top of the player
	/// or rests on furniture/in chests, which the collider never reaches.
	/// </summary>
	[Export] public float MagnetRadius = 4.0f;
	/// <summary>
	/// Distance at which the pulled item is actually collected.
	/// </summary>
	[Export] public float CollectRadius = 0.6f;
	/// <summary>Fly speed at the edge of the magnet radius.</summary>
	[Export] public float MagnetMinSpeed = 4.0f;
	/// <summary>Fly speed right before collection (the pull accelerates inward).</summary>
	[Export] public float MagnetMaxSpeed = 18.0f;

	/// <summary>
	/// Waits for the player to exit first before triggering a pickup.
	/// This is useful when a player just dropped an item and is still within
	/// the trigger zone. In that case we want to wait until the player leaves
	/// before listening for new trigger events.
	///
	/// This must be set before the LootableItem is added to the tree!
	/// </summary>
	public bool WaitForPlayerExited = false;

	public Node3D Pivot;

	private Player _player;
	private bool _armed;
	private bool _collected;
	private bool _pickupBlocked;

	public override void _Ready()
	{
		base._Ready();

		if (!Engine.IsEditorHint())
		{
			var trigger = GetNode<TriggerComponent>("TriggerComponent");
			trigger.WaitForBodyExited = WaitForPlayerExited;
			trigger.Triggered += OnTriggered;
		}

		if (Item == null)
		{
			GD.PrintErr("LootableItem has no item assigned");
			QueueFree();
			return;
		}
		if (Item.Scene == null)
		{
			GD.PrintErr("LootableItem's item has no scene assigned");
			QueueFree();
			return;
		}

		Pivot = GetNode<Node3D>("Pivot");

		// The item keeps its own model; only a per-run tint is applied when unidentified.
		var itemScene = Item.Scene.Instantiate<Node>();
		UpdateItemNodeProperties(itemScene);
		Color? tint = ItemIdentity.ResolveTint(Item);
		if (tint.HasValue)
		{
			ItemIdentity.ApplyTint(itemScene, tint.Value);
		}

		Pivot.AddChild(itemScene);
		Pivot.RotateY((float)(GD.Randf() * 2 * Math.PI));

		AddLootBeam();
		AddXray();

		// FIXME: Hardcoded check for gold is not ideal
		if (Item is Gold || !Animate)
		{
			Pivot.Position = new Vector3(0, 0, 0);
		}
		else if (Animate)
		{
			var animationPlayer = GetNode<AnimationPlayer>("AnimationPlayer");
			animationPlayer.Play("spin");
		}
	}

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint() || _collected || Item == null)
		{
			return;
		}

		if (_player == null || !GodotObject.IsInstanceValid(_player))
		{
			_player = GetTree().GetFirstNodeInGroup("player") as Player;
			if (_player == null)
			{
				return;
			}
		}

		Vector3 target = _player.GlobalPosition + Vector3.Up * 0.8f;
		float dist = GlobalPosition.DistanceTo(target);

		// A just-dropped item stays put until the player steps out of range once, so it
		// isn't instantly re-collected while still standing on the drop.
		if (!_armed)
		{
			if (!WaitForPlayerExited || dist > MagnetRadius)
			{
				_armed = true;
			}
			else
			{
				return;
			}
		}

		// Inventory was full on the last attempt: stop yanking until the player leaves and
		// comes back, so a full pack doesn't glue the item to them.
		if (_pickupBlocked)
		{
			if (dist > MagnetRadius)
			{
				_pickupBlocked = false;
			}
			return;
		}

		if (dist <= CollectRadius)
		{
			TryCollect(_player);
			return;
		}

		if (dist <= MagnetRadius)
		{
			float nearness = 1f - Mathf.Clamp(dist / MagnetRadius, 0f, 1f);
			float speed = Mathf.Lerp(MagnetMinSpeed, MagnetMaxSpeed, nearness);
			GlobalPosition = GlobalPosition.MoveToward(target, speed * (float)delta);
		}
	}

	private void AddLootBeam()
	{
		var beam = new LootBeam { Name = "LootBeam", BeamColor = LootVisuals.ResolveColor(Item) };
		AddChild(beam);
	}

	private void AddXray()
	{
		Color color = LootVisuals.ResolveColor(Item);
		color.A = 0.85f;
		var xray = new OcclusionXrayComponent
		{
			Name = "OcclusionXrayComponent",
			Category = XrayCategory.Loot,
			XrayColor = color,
			TargetRoot = "../Pivot",
		};
		AddChild(xray);
	}

	private void UpdateItemNodeProperties(Node node)
	{
		if (node is VisualInstance3D visu)
		{
			visu.Layers = 0;
			visu.SetLayerMaskValue(6, true);
		}

		foreach (Node child in node.GetChildren())
		{
			UpdateItemNodeProperties(child);
		}
	}

	private void OnTriggered(Node3D body)
	{
		if (body is Player player)
		{
			TryCollect(player);
		}
	}

	/// <summary>
	/// Attempts to add the item to the player's inventory. Idempotent (guarded by
	/// <see cref="_collected"/>) so the magnet and the collider fallback can't double-collect.
	/// A failed pickup (e.g. full inventory) blocks further pulls until the player leaves range.
	/// </summary>
	private void TryCollect(Player player)
	{
		if (_collected || player == null)
		{
			return;
		}

		if (player.PickupItem(Item, Quantity))
		{
			_collected = true;
			QueueFree();
		}
		else
		{
			_pickupBlocked = true;
		}
	}
}
