using Godot;
using System;


/// <summary>
/// Represents an item that can be picked up by the player. Shows a quality/type-colored loot
/// beam and, when within magnet range, floats to the player and is collected automatically.
/// Fresh chest and monster drops are first thrown out of their source and rest a moment, so the player sees what dropped.
/// </summary>
public partial class LootableItem : Node3D
{
	private static readonly StringName PlayerGroup = "player";
	/// <summary>
	/// The item to be picked up.
	/// </summary>
	[Export] public Item Item { get; set; }
	[Export] public PackedScene CursePresentation { get; set; }
	/// <summary>
	/// The quantity of items that can be picked up.
	/// </summary>
	[Export] public int Quantity { get; set; } = 1;
	/// <summary>
	/// Animate the item.
	/// </summary>
	[Export] public bool Animate { get; set; } = true;

	/// <summary>
	/// Distance at which the item is magnetically pulled to the player (~one floor tile).
	/// Larger than the pickup collider so it also grabs loot that spawns on top of the player
	/// or rests on furniture/in chests, which the collider never reaches.
	/// </summary>
	[Export] public float MagnetRadius { get; set; } = 4.0f;
	/// <summary>
	/// Distance at which the pulled item is actually collected.
	/// </summary>
	[Export] public float CollectRadius { get; set; } = 0.6f;
	/// <summary>Fly speed at the edge of the magnet radius.</summary>
	[Export] public float MagnetMinSpeed { get; set; } = 4.0f;
	/// <summary>Fly speed right before collection (the pull accelerates inward).</summary>
	[Export] public float MagnetMaxSpeed { get; set; } = 18.0f;

	/// <summary>Seconds a thrown drop flies from its source to where it lands.</summary>
	[Export] public float ThrowDuration { get; set; } = 0.45f;
	/// <summary>How far a throw arcs above the straight line from its source to where it lands.</summary>
	[Export] public float ThrowArcHeight { get; set; } = 1.2f;
	/// <summary>Seconds a thrown drop rests where it landed, so the player sees it, before the magnet may take it.</summary>
	[Export] public float LandedRestDuration { get; set; } = 0.4f;

	/// <summary>Where a thrown drop leaves its source: inside a chest, or at a corpse's waist.</summary>
	private const float ThrowLaunchHeight = 0.5f;
	/// <summary>Share of the flight over which a thrown drop grows from nothing, so it emerges from its source.</summary>
	private const float ThrowEmergeFraction = 0.3f;
	/// <summary>Height and duration of the hop a thrown drop makes on landing.</summary>
	private const float LandingBounceHeight = 0.2f;
	private const float LandingBounceDuration = 0.2f;

	/// <summary>
	/// Waits for the player to exit first before triggering a pickup.
	/// This is useful when a player just dropped an item and is still within
	/// the trigger zone. In that case we want to wait until the player leaves
	/// before listening for new trigger events.
	///
	/// This must be set before the LootableItem is added to the tree!
	/// </summary>
	public bool WaitForPlayerExited { get; set; }

	/// <summary>
	/// Makes a fresh drop fly out of <paramref name="source"/> (a world position) after <paramref name="delay"/> seconds
	/// instead of appearing in place: it arcs to the pickup's own position, tumbling, lands with a hop and rests long
	/// enough to show what dropped. Only then does it join the magnet.
	///
	/// This must be called before the LootableItem is added to the tree!
	/// </summary>
	public void ThrowFrom(Vector3 source, float delay = 0f)
	{
		_throwing = true;
		_airborne = true;
		_throwSource = source;
		_throwDelay = delay;
	}

	[Export] public Node3D Pivot { get; set; }
	[Export] public TriggerComponent Trigger { get; set; }
	[Export] public AnimationPlayer AnimationPlayer { get; set; }

	private Player _player;
	private ExplorationVisibility _exploration;
	private LootBeam _beam;
	private bool? _discovered;
	private bool _armed;
	private bool _collected;
	private bool _throwing;
	/// <summary>A thrown drop's beam lights up only on touchdown, so it marks the landing rather than giving it away.</summary>
	private bool _airborne;
	private Vector3 _throwSource;
	private float _throwDelay;
	public bool IsCollected => _collected;

	public void CommitCollected()
	{
		_collected = true;
		QueueFree();
	}

	[Export] public ItemCreationPolicy CreationPolicy { get; set; }
	private bool _pickupBlocked;
	private Level _level;

	public override void _EnterTree() => _level = this.GetAncestorOrNull<Level>();

	/// <summary>
	/// Turns an authored pickup's definition into this floor's instance, rolled from the level's item stream so the
	/// same seed restores the same item. Outside a level there is no stream to roll from, so it stays unrolled.
	/// </summary>
	private Item CreateAuthoredInstance()
	{
		if (_level == null) return ItemFactory.Create(Item);
		string source = HasMeta("persistent_id") ? GetMeta("persistent_id").AsString() : GetPath().ToString();
		using var rng = GenerationRandom.Create(_level.Seed, GenerationRandom.Items, source);
		return ItemFactory.Create(Item, CreationPolicy, _level.Depth, rng, HasMeta("persistent_id") ? source + ":0" : null);
	}

	public override void _Ready()
	{
		base._Ready();

		if (!Engine.IsEditorHint())
		{
			Trigger.WaitForBodyExited = WaitForPlayerExited;
			this.SubscribeUntilExit(Trigger, t => t.Triggered += OnTriggered, t => t.Triggered -= OnTriggered);
		}

		if (Item == null)
		{
			GD.PrintErr("LootableItem has no item assigned");
			QueueFree();
			return;
		}
		if (ItemIdentity.ResolveScene(Item) == null)
		{
			GD.PrintErr("LootableItem's item has no model to show");
			QueueFree();
			return;
		}

		if (!Item.IsRuntimeInstance)
		{
			if (Quantity <= 0 || Quantity > (Item.CanStack ? Item.MaxStackSize : 1))
			{ GD.PushError("Authored pickup must fit a single valid item record."); QueueFree(); return; }
			Item = CreateAuthoredInstance();
		}

		// Identifiable items show their type's run appearance model, identified or not.
		var itemScene = ItemIdentity.ResolveScene(Item).Instantiate<Node>();
		UpdateItemNodeProperties(itemScene);

		Pivot.AddChild(itemScene);
		if (CursePresentation != null && Item is EquipableItem equipment && itemScene is Node3D model)
		{
			var aura = CursePresentation.Instantiate<ItemCurseWorldEffectPresenter>();
			aura.Initialize(equipment, model); model.AddChild(aura);
		}
		Pivot.RotateY((float)(GD.Randf() * 2 * Math.PI));

		_exploration = new ExplorationVisibility(this);
		Visible = false; // No spawn-frame flash before the caller assigns the final world position.
		AddLootBeam();
		AddXray();
		// Dungeon spawners position pickups after AddChild. First process/collection checks
		// see that final position; standalone item previews can display immediately.
		if (!_exploration.HasLevelContext) RefreshDiscoveryVisibility();

		// FIXME: Hardcoded check for gold is not ideal
		bool spins = Animate && Item is not Gold;
		if (!spins)
		{
			Pivot.Position = new Vector3(0, 0, 0);
		}
		if (_throwing)
		{
			PlayThrow(spins);
		}
		else if (spins)
		{
			AnimationPlayer.Play("spin");
		}
	}

	/// <summary>
	/// Flies only the pivot, from the source back to its resting pose: the pickup itself already sits where it lands,
	/// so collection and the position a save records never pass through a wall. The flight ends on the idle spin
	/// animation's first pose, so the hand-over to it is seamless.
	/// </summary>
	private void PlayThrow(bool spins)
	{
		Vector3 rest = Pivot.Position;
		float restYaw = spins ? 0f : Pivot.Rotation.Y;
		Vector3 launch = rest;
		Pivot.Scale = Vector3.One * 0.01f; // Hidden until it emerges. Not zero: a singular basis breaks global transforms.

		void Fly(float t)
		{
			Pivot.Position = launch.Lerp(rest, t) + Vector3.Up * (4f * ThrowArcHeight * t * (1f - t));
			Pivot.Rotation = new Vector3(Mathf.Tau * (t - 1f), restYaw, 0); // One end-over-end tumble.
			Pivot.Scale = Vector3.One * Mathf.Lerp(0.01f, 1f, Mathf.Min(t / ThrowEmergeFraction, 1f));
		}

		Tween tween = CreateTween();
		tween.TweenInterval(_throwDelay);
		// Spawners place the pickup right after adding it, so its final position is only known from the first step.
		tween.TweenCallback(Callable.From(() => launch = ToLocal(_throwSource) + Vector3.Up * ThrowLaunchHeight));
		tween.TweenMethod(Callable.From<float>(Fly), 0f, 1f, ThrowDuration);
		tween.TweenCallback(Callable.From(() =>
		{
			_airborne = false;
			_beam?.SetActive(_discovered == true);
		}));
		tween.TweenProperty(Pivot, "position:y", rest.Y + LandingBounceHeight, LandingBounceDuration / 2)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(Pivot, "position:y", rest.Y, LandingBounceDuration / 2)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		if (spins)
		{
			tween.TweenCallback(Callable.From(() => AnimationPlayer.Play("spin")));
		}
		tween.TweenInterval(Mathf.Max(0f, LandedRestDuration - LandingBounceDuration));
		tween.TweenCallback(Callable.From(() => _throwing = false));
	}

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint() || _collected || Item == null)
		{
			return;
		}

		if (!RefreshDiscoveryVisibility()) return;
		// A thrown drop is left alone until it has landed and been shown.
		if (_throwing) return;

		if (_player == null || !GodotObject.IsInstanceValid(_player))
		{
			_player = GetTree().GetFirstNodeInGroup(PlayerGroup) as Player;
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

		if (dist <= MagnetRadius && IsInsideTree())
		{
			if (!WorldSight.IsClear(GetWorld3D(), GlobalPosition + Vector3.Up * 0.8f, target)) return;
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

	/// <summary>Exploration gates the whole loot presentation and collection, including runtime drops outside the generator subtree.</summary>
	private bool RefreshDiscoveryVisibility()
	{
		bool visible = _exploration?.IsRevealed(GlobalPosition) == true;
		if (_discovered == visible) return visible;
		_discovered = visible;
		Visible = visible; // Gates the entire subtree, including the model, helpers, labels and the beam's light.
		_beam?.SetActive(visible && !_airborne);
		return visible;
	}

	private void AddLootBeam()
	{
		_beam = new LootBeam { Name = "LootBeam", BeamColor = LootVisuals.ResolveColor(Item) };
		_beam.SetActive(Visible);
		AddChild(_beam);
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
		if (_collected || _throwing || player == null || !RefreshDiscoveryVisibility())
		{
			return;
		}

		if (player.InventoryController.TryPickup(this).Success)
		{
			// Controller has committed ownership and queued this representation for deletion.
		}
		else
		{
			_pickupBlocked = true;
			var label = new Label3D
			{
				Text = player.InventoryController.LastOperationMessage, Position = Vector3.Up * 1.5f,
				Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 32, NoDepthTest = true, RenderPriority = FloatingLabel.LabelRenderPriority,
			};
			label.AddChild(new ConstantScreenSize3D { Target = label });
			AddChild(label);
			GetTree().CreateTimer(2).Timeout += () => { if (IsInstanceValid(label)) { label.QueueFree(); } };
		}
	}
}