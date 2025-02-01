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
			if (IsNodeReady()) { Update(); }
		}
	} = false;

	private CollisionShape3D _collisionShape;
	private MeshInstance3D _door;
	private InteractiveComponent _interactiveComponent;

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
		_door.RotationDegrees = new Vector3(0, IsOpen ? 90 : 0, 0);
		_collisionShape.Disabled = IsOpen;
	}

	private void OpenDoor()
	{
		if (IsOpen)
			return;

		GD.Print("Opening door");
		IsOpen = true;
		_interactiveComponent.IsInteractive = false;
		var tween = CreateTween();
		tween.TweenProperty(_door, "rotation_degrees:y", 90, 0.5f);
		tween.Finished += () =>
		{
			_interactiveComponent.IsInteractive = true;
			Update();
		};
	}

	private void CloseDoor()
	{
		if (!IsOpen)
			return;

		GD.Print("Closing door");
		IsOpen = false;
		_interactiveComponent.IsInteractive = false;
		var tween = CreateTween();
		tween.TweenProperty(_door, "rotation_degrees:y", 0, 0.5f);
		tween.Finished += () =>
		{
			_interactiveComponent.IsInteractive = true;
			Update();
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
