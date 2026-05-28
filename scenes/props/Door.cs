using Godot;

[Tool]
public partial class Door : Node3D
{
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

	private CollisionShape3D _collisionShape;
	private MeshInstance3D _door;
	private InteractiveComponent _interactiveComponent;
	private bool _isAnimating;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_collisionShape = GetNode<CollisionShape3D>("%CollisionShape3D");
		_door = GetNode<MeshInstance3D>("%wall_doorway_door");

		if (!Engine.IsEditorHint())
		{
			_interactiveComponent = GetNode<InteractiveComponent>("InteractiveComponent");
			_interactiveComponent.Interacted += OnInteract;
		}

		Update();
	}

	private void Update()
	{
		_door.RotationDegrees = new Vector3(0, IsOpen ? OpenRotationDegrees : 0, 0);
		_collisionShape.Disabled = IsOpen;
	}

	private void OpenDoor()
	{
		if (IsOpen || _isAnimating)
			return;

		GD.Print("Opening door");
		_isAnimating = true;
		_interactiveComponent.IsInteractive = false;
		_collisionShape.Disabled = true;
		IsOpen = true;

		var tween = CreateTween();
		tween.TweenProperty(_door, "rotation_degrees:y", OpenRotationDegrees, AnimationDuration)
			.SetTrans(Tween.TransitionType.Sine)
			.SetEase(Tween.EaseType.Out);
		tween.Finished += () =>
		{
			_isAnimating = false;
			Update();
			_interactiveComponent.IsInteractive = true;
		};
	}

	private void CloseDoor()
	{
		if (!IsOpen || _isAnimating)
			return;

		GD.Print("Closing door");
		_isAnimating = true;
		_interactiveComponent.IsInteractive = false;
		IsOpen = false;

		var tween = CreateTween();
		tween.TweenProperty(_door, "rotation_degrees:y", 0, AnimationDuration)
			.SetTrans(Tween.TransitionType.Sine)
			.SetEase(Tween.EaseType.Out);
		tween.Finished += () =>
		{
			_isAnimating = false;
			Update();
			_interactiveComponent.IsInteractive = true;
		};
	}

	public void OnInteract(Player actor)
	{
		if (IsOpen)
		{
			CloseDoor();
		}
		else
		{
			OpenDoor();
		}
	}
}
