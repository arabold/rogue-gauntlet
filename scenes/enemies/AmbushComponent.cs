using System.Linq;
using Godot;

/// <summary>
/// Keeps a monster lying in wait until the hero gives it away. While dormant it is concealed
/// (<see cref="EnemyBase.IsConcealed"/>): aim-assist and seeking spells pass it over, it is hidden from the hero so its
/// nameplate (name and health bar) and monster x-ray stay off (<see cref="IHideable"/>), and it does not think or move
/// (<see cref="EnemyBehaviorComponent"/>). Blasts and blows
/// still hit it, which wakes it. It either shows a <see cref="Disguise"/> prop in place of its <see cref="Model"/> (a
/// real chest, gold outline and all, whose "Open Chest" springs the trap), or rests in its <see cref="DormantClip"/>:
/// a coil, or a pose sunk below the floor or into a wall that its <see cref="WakeClip"/> emerges from. Using the
/// disguise, coming within
/// <see cref="WakeRadius"/> in its sight for <see cref="WakeDelay"/>, or hitting it wakes it: it drops any disguise,
/// plays its <see cref="WakeClip"/> and goes for the hero. The chest mimic waits disguised; the snake lies coiled and
/// strikes at a hero who comes close.
/// </summary>
[GlobalClass]
public partial class AmbushComponent : Node
{
	private static readonly StringName PlayerGroup = "player";
	// How often a waiting monster looks for the hero; well under any wake delay, and cheaper than every tick.
	private const float ScanInterval = .15f;

	[Export] public EnemyBase Actor { get; set; }
	/// <summary>The monster's own model, hidden while a disguise stands in for it.</summary>
	[Export] public Node3D Model { get; set; }
	/// <summary>Optional prop scene shown in the monster's place, such as a chest; using its interaction springs the trap.</summary>
	[Export] public PackedScene Disguise { get; set; }
	[Export] public AnimationPlayer AnimationPlayer { get; set; }
	[Export] public AnimationTree AnimationTree { get; set; }
	/// <summary>The clip an undisguised monster rests in while it waits, such as a coil; none keeps its idle.</summary>
	[Export] public StringName DormantClip { get; set; } = "";
	/// <summary>The clip it wakes with, before its AI takes over; none wakes it at once.</summary>
	[Export] public StringName WakeClip { get; set; } = "Wake";
	/// <summary>How close the hero must come, horizontally and in its sight, to wake it.</summary>
	[Export(PropertyHint.Range, "0.5,10,0.1,suffix:m")] public float WakeRadius { get; set; } = 1.8f;
	/// <summary>Seconds the hero may stay that close before it wakes; 0 wakes it the moment the hero arrives.</summary>
	[Export(PropertyHint.Range, "0,10,0.1,suffix:s")] public float WakeDelay { get; set; } = 2.5f;

	public AmbushState State { get; private set; } = AmbushState.Waiting;
	/// <summary>Waiting or waking: the monster's own behavior has not taken over yet.</summary>
	public bool IsDormant => State != AmbushState.Hunting;
	/// <summary>Whether something gave it away; its wake clip may still be playing.</summary>
	public bool IsAwake => State != AmbushState.Waiting;

	[Signal] public delegate void WokeEventHandler();

	private Node3D _disguise;
	private float _near;
	private float _scan;

	public override void _Ready()
	{
		if (Actor == null || (Disguise != null && Model == null))
		{
			GD.PushError($"{GetParent()?.Name}/{Name}: assign Actor, and a Model for the Disguise; the monster does not wait.");
			State = AmbushState.Hunting;
			return;
		}
		Actor.HurtBoxComponent.DamageTaken += OnDamageTaken;
		// Deferred: the actor is still readying its children, and the disguise must not join the monster's x-ray.
		if (Disguise != null) Callable.From(PutOnDisguise).CallDeferred();
		else if (!string.IsNullOrEmpty(DormantClip) && AnimationPlayer?.HasAnimation(DormantClip) == true)
		{
			if (AnimationTree != null) AnimationTree.Active = false;
			AnimationPlayer.Play(DormantClip);
		}
	}

	private void PutOnDisguise()
	{
		if (State != AmbushState.Waiting || !GodotObject.IsInstanceValid(Actor)) return;
		_disguise = Disguise.Instantiate<Node3D>();
		// Beside the model's pivot, so it stands and faces exactly where the monster does, yet outside the pivot whose
		// meshes the monster's x-ray outlines: the prop shows only its own.
		_disguise.Transform = Model.GetParent<Node3D>().Transform;
		Actor.AddChild(_disguise);
		Model.Visible = false;
		foreach (var body in _disguise.FindChildren("*", "PhysicsBody3D", true, false).OfType<PhysicsBody3D>())
			Actor.AddCollisionExceptionWith(body);
		// The prop wires its own interaction when it is ready; take it over after that.
		if (_disguise.IsNodeReady()) ArmTrap();
		else _disguise.Ready += ArmTrap;
	}

	/// <summary>Using the prop (opening the chest) springs the trap instead; a prop without an interaction has none.</summary>
	private void ArmTrap()
	{
		var interaction = _disguise.FindChildren("*", "", true, false).OfType<InteractiveComponent>().FirstOrDefault();
		if (interaction == null) return;
		interaction.CommitHandler = (reservation, actor) =>
		{
			Wake(reservation.Actor);
			return InteractionCommitResult.Success;
		};
	}

	private void OnDamageTaken(float amount, Vector3 attackDirection) => Wake(ClosestPlayer());

	public override void _PhysicsProcess(double delta)
	{
		if (State != AmbushState.Waiting) return;
		_scan -= (float)delta;
		if (_scan > 0) return;
		_scan = ScanInterval;
		var player = ClosestPlayer();
		bool near = player != null && player.GlobalPosition.HorizontalDistanceTo(Actor.GlobalPosition) <= WakeRadius
			&& WorldSight.IsClear(Actor.GetWorld3D(), Actor.GlobalPosition + Vector3.Up, player.GlobalPosition + Vector3.Up);
		_near = near ? _near + ScanInterval : 0;
		if (near && _near >= WakeDelay) Wake(player);
	}

	/// <summary>
	/// Gives the monster away: it drops any disguise, plays its wake clip, then hunts <paramref name="target"/>. It stops
	/// being concealed at once, so <see cref="Woke"/> also tells its actor it may show again.
	/// </summary>
	public void Wake(Node3D target)
	{
		if (State != AmbushState.Waiting) return;
		State = AmbushState.Waking;
		SetPhysicsProcess(false);
		if (GodotObject.IsInstanceValid(_disguise)) _disguise.QueueFree();
		if (Model != null) Model.Visible = true;
		if (CombatTrace.Enabled) CombatTrace.Write(Actor, "ambush.woke", ("target", CombatTrace.Identity(target)));
		EmitSignalWoke();
		double duration = AnimationPlayer?.HasAnimation(WakeClip) == true ? AnimationPlayer.GetAnimation(WakeClip).Length : 0;
		if (duration <= 0)
		{
			Hunt(target);
			return;
		}
		if (AnimationTree != null) AnimationTree.Active = false;
		AnimationPlayer.Play(WakeClip);
		GetTree().CreateTimer(duration, false, true).Timeout += () => Hunt(target);
	}

	/// <summary>After the wake clip the monster's own behavior takes over, already on the hero.</summary>
	private void Hunt(Node3D target)
	{
		if (!GodotObject.IsInstanceValid(Actor) || Actor.IsDead) return;
		if (AnimationTree != null) AnimationTree.Active = true;
		State = AmbushState.Hunting;
		var behavior = Actor.EnemyBehaviorComponent;
		if (GodotObject.IsInstanceValid(target) && target.IsInsideTree())
		{
			behavior.SetTarget(target);
			behavior.SetBehavior(EnemyBehaviorState.Chasing);
		}
	}

	private Player ClosestPlayer()
	{
		Player closest = null;
		float best = float.PositiveInfinity;
		foreach (Node node in GetTree().GetNodesInGroup(PlayerGroup))
		{
			if (node is not Player { IsDead: false } player) continue;
			float distance = player.GlobalPosition.DistanceSquaredTo(Actor.GlobalPosition);
			if (distance < best) (best, closest) = (distance, player);
		}
		return closest;
	}
}

/// <summary>Where a monster lying in wait is: still waiting, playing its wake clip, or hunting with its own behavior.</summary>
public enum AmbushState
{
	Waiting,
	Waking,
	Hunting,
}
