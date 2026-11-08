using Godot;

/// <summary>
/// A hinged door composed in its scene: optional <see cref="DoorLockComponent"/> and <see cref="SecretDoorComponent"/>
/// own access state and announce changes, while the door turns that state into its offer, prompt, marker and indicator.
/// The frame is ordinary solid collision; the leaf's collider follows the leaf about its hinge, so the doorway closes
/// with it and the open leaf stands solid beside the opening. It only passes through actors while it swings.
/// </summary>
[Tool]
public partial class Door : Node3D
{
	[Export] public DoorLockComponent Lock { get; set; }
	[Export] public InteriorDoorComponent Interior { get; set; }
	[Export] public SecretDoorComponent Secret { get; set; }
	[Export] public InteractiveComponent Interactive { get; set; }
	[Export] public MeshInstance3D KeyMarker { get; set; }
	[Export] public Node3D DoorVisual { get; set; }
	/// <summary>The hinged leaf that swings open and carries the x-ray indicator.</summary>
	[Export] public MeshInstance3D Leaf { get; set; }
	/// <summary>The leaf's collider, authored in its shut pose.</summary>
	[Export] public CollisionShape3D Collision { get; set; }
	[Export] public bool LockEligible { get; set; } = false;
	public string ConnectionId { get; set; } = "";
	public bool IsDiscovered => Secret?.IsDiscovered ?? true;

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

	private const float OpenRotationDegrees = 90f;
	private const float AnimationDuration = 0.5f;
	private const float InteractionRange = 2.5f;

	private MeshInstance3D _xray;
	private bool _indicatorAllowed;
	private bool _isAnimating;
	private Tween _activeTween;
	private StandardMaterial3D _keyMarkerMaterial;
	// The leaf collider's pose in door space with the leaf shut.
	private Transform3D? _closedLeafPose;

	public override void _Ready()
	{
		if (!HasRequiredParts)
		{
			GD.PushError($"{Name}: a door requires assigned Interactive, Leaf and Collision nodes.");
			return;
		}

		// Editor previews conceal generated secrets too, so access changes are observed in both modes.
		this.SubscribeUntilExit(Lock, l => l.Changed += RefreshAccessPresentation, l => l.Changed -= RefreshAccessPresentation);
		this.SubscribeUntilExit(Secret, s => s.Changed += RefreshAccessPresentation, s => s.Changed -= RefreshAccessPresentation);
		if (!Engine.IsEditorHint())
		{
			Interactive.OfferProvider = GetDoorOffer;
			Interactive.CommitHandler = OnCommitInteraction;
			CreateIndicator();

			this.SubscribeUntilExit(
				SignalBus.Instance,
				bus => bus.XraySettingsChanged += RefreshIndicator,
				bus => bus.XraySettingsChanged -= RefreshIndicator);
		}

		CaptureClosedLeaf();
		Update();
		RefreshAccessPresentation();
	}

	/// <summary>Records the leaf collider's current placement as its shut pose; call after changing the leaf's geometry.</summary>
	public void CaptureClosedLeaf()
	{
		if (HasRequiredParts) _closedLeafPose = DoorSpace(Collision);
	}

	/// <summary>The leaf collider's pose in door space, shut or swung open about the leaf's hinge.</summary>
	public Transform3D LeafPose(bool open)
	{
		if (_closedLeafPose == null) CaptureClosedLeaf();
		var shut = _closedLeafPose ?? Transform3D.Identity;
		if (!open) return shut;
		var hinge = DoorSpace(Leaf.GetParentNode3D()) * Leaf.Position;
		var swing = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(OpenRotationDegrees)), Vector3.Zero);
		return Transform3D.Identity.Translated(hinge) * swing * Transform3D.Identity.Translated(-hinge) * shut;
	}

	/// <summary>The leaf's collision outline, shut or open, mapped by <paramref name="doorToSpace"/> from door space.</summary>
	public SolidOutline LeafOutline(bool open, Transform3D doorToSpace) =>
		CollisionOutlines.FromShape(Collision.Shape, doorToSpace * LeafPose(open));

	/// <summary>A descendant's transform relative to this door.</summary>
	private Transform3D DoorSpace(Node3D node)
	{
		var transform = Transform3D.Identity;
		for (Node current = node; current != null && current != this; current = current.GetParent())
			if (current is Node3D spatial) transform = spatial.Transform * transform;
		return transform;
	}

	private bool HasRequiredParts => Interactive != null && Leaf != null && Collision != null;

	public void RefreshAccessPresentation()
	{
		if (DoorVisual != null) DoorVisual.Visible = IsDiscovered;
		if (Interactive != null) Interactive.IsInteractive = IsDiscovered && !_isAnimating;
		if (KeyMarker != null)
		{
			var key = Lock?.Definition?.RequiredKey;
			KeyMarker.Visible = IsDiscovered && _indicatorAllowed && Lock?.IsLocked == true && key != null;
			// Tinting duplicates the shared material; never bake that per-instance override into an edited scene.
			if (key != null && !Engine.IsEditorHint())
			{
				_keyMarkerMaterial ??= (StandardMaterial3D)KeyMarker.MaterialOverride.Duplicate();
				_keyMarkerMaterial.AlbedoColor = key.MarkerColor;
				KeyMarker.MaterialOverride = _keyMarkerMaterial;
			}
		}
		RefreshIndicator();
	}

	/// <summary>The access state a save records; the inverse of <see cref="RestoreAccess"/>.</summary>
	public DoorAccessSaveData CaptureAccess() => new()
	{
		ConnectionId = ConnectionId, IsDiscovered = IsDiscovered, IsLocked = Lock?.IsLocked ?? false, IsOpen = IsOpen,
	};

	public void RestoreAccess(DoorAccessSaveData state)
	{
		state.Validate();
		_activeTween?.Kill();
		_isAnimating = false;
		Lock?.Restore(state.IsLocked);
		Secret?.Restore(state.IsDiscovered);
		IsOpen = state.IsOpen;
		Update(); RefreshAccessPresentation();
	}

	/// <summary>
	/// Builds an x-ray silhouette of the door. Its shader only draws where the door
	/// is occluded by scene geometry (a wall or the black fog cap) or its visible
	/// surface is still unexplored, and fades with distance, so a hidden door reads
	/// from any camera angle without showing when it is already in plain view. It draws above the fog, so walls still under fog
	/// do not hide it; <see cref="SetIndicatorVisible"/> keeps undiscovered doors dark.
	/// </summary>
	private void CreateIndicator()
	{
		var material = new ShaderMaterial
		{
			Shader = GD.Load<Shader>("res://scenes/props/door_xray.gdshader"),
			RenderPriority = FloorDiscovery.AboveFogRenderPriority,
		};

		_xray = new MeshInstance3D
		{
			Mesh = Leaf.Mesh,
			MaterialOverride = material,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Visible = false, // Stays hidden until an adjacent tile is revealed.
		};
		VisualHelper.Mark(_xray);
		Leaf.AddChild(_xray);
	}

	/// <summary>
	/// Allows the x-ray indicator to show (it still only renders when the door is
	/// closed). Driven by whether the area next to the door has been discovered.
	/// </summary>
	public void SetIndicatorVisible(bool allowed)
	{
		_indicatorAllowed = allowed;
		RefreshAccessPresentation();
	}

	/// <summary>
	/// Applies the current indicator visibility: shown only when the area is discovered,
	/// the door is closed, and the door x-ray toggle is enabled. The shader still decides
	/// when it actually renders (only where occluded).
	/// </summary>
	private void RefreshIndicator()
	{
		if (_xray != null)
		{
			_xray.Visible = IsDiscovered && _indicatorAllowed && !IsOpen && GameDebug.DoorXrayEnabled;
		}
	}

	private void Update()
	{
		if (!HasRequiredParts) return;
		Leaf.RotationDegrees = new Vector3(0, IsOpen ? OpenRotationDegrees : 0, 0);
		// The editor previews the leaf only; moving the collider there would save the open pose into the scene.
		if (Engine.IsEditorHint()) return;
		Collision.Transform = DoorSpace(Collision.GetParentNode3D()).AffineInverse() * LeafPose(IsOpen);
		Collision.Disabled = false;
	}

	private void OpenDoor()
	{
		GD.Print("Opening door");
		_isAnimating = true;
		Collision.Disabled = true;
		IsOpen = true;
		RefreshAccessPresentation(); // Blocks interaction and hides the indicator while the leaf swings.
		SignalBus.EmitDoorOpened(this);
		Swing(OpenRotationDegrees);
	}

	private void CloseDoor()
	{
		GD.Print("Closing door");
		_isAnimating = true;
		Collision.Disabled = true;
		IsOpen = false;
		RefreshAccessPresentation(); // The shader decides when the indicator actually appears.
		SignalBus.EmitDoorClosed(this);
		Swing(0);
	}

	private void Swing(float degrees)
	{
		_activeTween?.Kill();
		_activeTween = CreateTween();
		_activeTween.TweenProperty(Leaf, "rotation_degrees:y", degrees, AnimationDuration)
			.SetTrans(Tween.TransitionType.Sine)
			.SetEase(Tween.EaseType.Out);
		_activeTween.Finished += () =>
		{
			_isAnimating = false;
			Update();
			RefreshAccessPresentation();
		};
	}

	/// <summary>Reached only while <see cref="Interactive"/> is interactive, i.e. discovered and not swinging.</summary>
	private InteractionOffer GetDoorOffer(ActorInteractionContext actor)
	{
		if (IsOpen) return Offer(DoorOperation.Close, "Close Door", 1);
		if (Lock?.IsLocked != true) return Offer(DoorOperation.Open, "Open Door", 0.5f);
		var method = Lock.SelectMethod(actor);
		return method == null
			? Offer(DoorOperation.Locked, Lock.MissingLabel, 1, blocked: true)
			: Offer(method.OperationId, method.Label, method.Duration, method.Permission);
	}

	private InteractionOffer Offer(string operation, string label, float duration,
		InteractionPermission permission = InteractionPermission.OpenDoors, bool blocked = false) =>
		new(Interactive, operation, label, duration, InteractionRange, permission, Interactive.StateRevision, blocked);

	/// <summary>The interaction layer has already checked the offer's permission and this target's interactivity.</summary>
	private InteractionCommitResult OnCommitInteraction(InteractionReservation reservation, ActorInteractionContext actor)
	{
		using var update = actor.Inventory?.DeferChanges();
		if (reservation.OperationId == DoorOperation.Close)
		{
			if (!IsOpen) return InteractionCommitResult.TargetInvalidated;
			CloseDoor();
			return InteractionCommitResult.Success;
		}
		if (IsOpen) return InteractionCommitResult.TargetInvalidated;
		if (Lock?.IsLocked == true && !Lock.TryUnlock(actor, reservation.OperationId)) return InteractionCommitResult.PrerequisitesFailed;
		OpenDoor();
		return InteractionCommitResult.Success;
	}
}