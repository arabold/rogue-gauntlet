using Godot;

/// <summary>A lootable chest. Not a tool script: its gameplay components stay inert in the editor, so none of their
/// resources need editor-side script instances.</summary>
public partial class Chest : Node3D
{
	[Signal] public delegate void OpenedEventHandler(Chest chest);

	[Export]
	public bool IsOpen
	{
		get;
		set
		{
			field = value;
			if (IsNodeReady() && !_isAnimating) { Update(); }
		}
	} = false;

	[Export] public InteractiveComponent Interactive { get; set; }
	/// <summary>Optional: without it the chest opens empty.</summary>
	[Export] public LootTableComponent LootTable { get; set; }
	/// <summary>The hinged lid that swings open.</summary>
	[Export] public Node3D Lid { get; set; }
	/// <summary>Golden occlusion indicator for an unopened chest; optional when authoring other chest variants.</summary>
	[Export] public OcclusionXrayComponent Xray { get; set; }

	private const float OpenRotationDegrees = -45f;
	private const float AnimationDuration = 0.5f;

	private bool _isAnimating;
	private Tween _activeTween;

	public override void _Ready()
	{
		if (Interactive == null || Lid == null)
		{
			GD.PushError($"{Name}: a chest requires assigned Interactive and Lid nodes.");
			return;
		}

		Interactive.OfferProvider = GetChestOffer;
		Interactive.CommitHandler = OnCommitInteraction;
		Update();
	}

	private void Update()
	{
		if (Xray != null) Xray.IsEnabled = !IsOpen;
		if (Lid == null)
			return;
		Lid.RotationDegrees = new Vector3(IsOpen ? OpenRotationDegrees : 0, 0, 0);
	}

	private void OpenChest()
	{
		if (IsOpen || _isAnimating)
			return;

		_isAnimating = true;
		Interactive.IsInteractive = false;
		IsOpen = true;
		if (Xray != null) Xray.IsEnabled = false;
		// Commit the reward with opening, rather than after the animation: a save
		// during the tween must not restore an empty opened chest.
		LootTable?.DropLoot();

		_activeTween?.Kill();
		_activeTween = CreateTween();
		_activeTween.TweenProperty(Lid, "rotation_degrees:x", OpenRotationDegrees, AnimationDuration)
			.SetTrans(Tween.TransitionType.Sine)
			.SetEase(Tween.EaseType.Out);
		_activeTween.Finished += () =>
		{
			_isAnimating = false;
			Update();
			OnOpened();
		};
	}

	private InteractionOffer GetChestOffer(ActorInteractionContext actor)
	{
		if (IsOpen) return InteractionOffer.None;
		return new InteractionOffer(
			Interactive,
			"open_chest",
			"Open Chest",
			1.0f,
			2.5f,
			InteractionPermission.OpenContainers,
			Interactive.StateRevision);
	}

	private InteractionCommitResult OnCommitInteraction(InteractionReservation reservation, ActorInteractionContext actor)
	{
		if (IsOpen || _isAnimating) return InteractionCommitResult.TargetInvalidated;
		OpenChest();
		return InteractionCommitResult.Success;
	}

	private void OnInteract(Player actor)
	{
		OpenChest();
	}

	public void OnOpened()
	{
		EmitSignalOpened(this);
	}

	public void RestoreOpenState()
	{
		_activeTween?.Kill();
		_isAnimating = false;
		IsOpen = true;
		if (Interactive != null) { Interactive.IsInteractive = false; }
		Update();
	}
}