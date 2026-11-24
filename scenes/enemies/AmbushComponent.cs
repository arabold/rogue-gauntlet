using System.Linq;
using Godot;

/// <summary>
/// Keeps a monster lying in wait until the hero gives it away. While dormant it stays out of the hero's targeting, hides
/// its nameplate and does not think or move (<see cref="EnemyBehaviorComponent"/>). It either shows a
/// <see cref="Disguise"/> (a real chest, gold outline and all) in place of its <see cref="Model"/> and offers the
/// disguise's "Open Chest", or rests in its <see cref="DormantClip"/>. Opening the disguise, coming within
/// <see cref="WakeRadius"/> in its sight for <see cref="WakeDelay"/>, or hitting it wakes it: it drops any disguise,
/// plays its <see cref="WakeClip"/> and goes for the hero. The chest mimic waits disguised; the snake lies coiled and
/// strikes at a hero who comes close.
/// </summary>
[GlobalClass]
public partial class AmbushComponent : Node
{
	private static readonly StringName EnemyGroup = "enemy";
	private static readonly StringName PlayerGroup = "player";

	[Export] public EnemyBase Actor { get; set; }
	/// <summary>The monster's own model, hidden while a disguise stands in for it.</summary>
	[Export] public Node3D Model { get; set; }
	/// <summary>Optional chest scene shown in the monster's place; its interaction becomes the trap.</summary>
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

	/// <summary>Lying in wait: from spawn until it wakes, including its wake clip.</summary>
	public bool IsDormant { get; private set; } = true;
	/// <summary>Whether something gave it away; its wake clip may still be playing.</summary>
	public bool IsAwake { get; private set; }

	[Signal] public delegate void WokeEventHandler();

	private Chest _disguise;
	private float _near;

	public override void _Ready()
	{
		if (Actor == null || (Disguise != null && Model == null))
		{
			GD.PushError($"{GetParent()?.Name}/{Name}: assign Actor, and a Model for the Disguise; the monster does not wait.");
			IsDormant = false;
			return;
		}
		Actor.RemoveFromGroup(EnemyGroup);
		if (Actor.Nameplate != null) Actor.Nameplate.Enabled = false;
		Actor.HurtBoxComponent.DamageTaken += OnDamageTaken;
		if (Disguise != null) PutOnDisguise();
		else if (!string.IsNullOrEmpty(DormantClip) && AnimationPlayer?.HasAnimation(DormantClip) == true)
		{
			if (AnimationTree != null) AnimationTree.Active = false;
			AnimationPlayer.Play(DormantClip);
		}
	}

	private void PutOnDisguise()
	{
		_disguise = Disguise.Instantiate<Chest>();
		// Beside the model, so it stands and faces exactly where the monster does.
		Model.GetParent().AddChild(_disguise);
		Model.Visible = false;
		foreach (var body in _disguise.FindChildren("*", "PhysicsBody3D", true, false).OfType<PhysicsBody3D>())
			Actor.AddCollisionExceptionWith(body);
		// The chest wires its own interaction when it is ready; take it over after that.
		if (_disguise.IsNodeReady()) ArmTrap();
		else _disguise.Ready += ArmTrap;
	}

	/// <summary>Opening the chest springs the trap instead of the lid.</summary>
	private void ArmTrap() => _disguise.Interactive.CommitHandler = (reservation, actor) =>
	{
		Wake(reservation.Actor);
		return InteractionCommitResult.Success;
	};

	private void OnDamageTaken(float amount, Vector3 attackDirection) => Wake(ClosestPlayer());

	public override void _PhysicsProcess(double delta)
	{
		if (IsAwake || !IsDormant) return;
		var player = ClosestPlayer();
		bool near = player != null && player.GlobalPosition.HorizontalDistanceTo(Actor.GlobalPosition) <= WakeRadius
			&& WorldSight.IsClear(Actor.GetWorld3D(), Actor.GlobalPosition + Vector3.Up, player.GlobalPosition + Vector3.Up);
		_near = near ? _near + (float)delta : 0;
		if (near && _near >= WakeDelay) Wake(player);
	}

	/// <summary>Gives the monster away: it drops any disguise, plays its wake clip, then hunts <paramref name="target"/>.</summary>
	public void Wake(Node3D target)
	{
		if (IsAwake || !IsDormant) return;
		IsAwake = true;
		SetPhysicsProcess(false);
		if (GodotObject.IsInstanceValid(_disguise)) _disguise.QueueFree();
		if (Model != null) Model.Visible = true;
		Actor.AddToGroup(EnemyGroup);
		if (Actor.Nameplate != null) Actor.Nameplate.Enabled = true;
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
		IsDormant = false;
		var behavior = Actor.EnemyBehaviorComponent;
		if (GodotObject.IsInstanceValid(target) && target.IsInsideTree())
		{
			behavior.SetTarget(target);
			behavior.SetBehavior(EnemyBehaviorState.Chasing);
		}
	}

	private Player ClosestPlayer() => GetTree().GetNodesInGroup(PlayerGroup).OfType<Player>()
		.Where(player => !player.IsDead)
		.OrderBy(player => player.GlobalPosition.DistanceSquaredTo(Actor.GlobalPosition)).FirstOrDefault();
}
