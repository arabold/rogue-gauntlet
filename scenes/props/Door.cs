using Godot;

[Tool]
public partial class Door : Node3D
{
	[Export]
	public bool IsOpen
	{
		get => _isOpen;
		set
		{
			if (value != _isOpen && IsNodeReady())
			{
				_isOpen = value;
				UpdateDoor();
			}
		}
	}

	private bool _isOpen = false;
	private StaticBody3D _staticBody;
	private MeshInstance3D _wall;
	private MeshInstance3D _door;
	private InteractiveComponent _interactiveComponent;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_staticBody = GetNode<StaticBody3D>("StaticBody3D");
		_wall = GetChild<MeshInstance3D>(0);
		_door = _wall.GetChild<MeshInstance3D>(0);

		if (!Engine.IsEditorHint())
		{
			_interactiveComponent = GetNode<InteractiveComponent>("InteractiveComponent");
			_interactiveComponent.Interacted += OnInteract;
		}

		UpdateDoor();
	}

	private void UpdateDoor()
	{
		if (_door == null)
			return;
		_door.RotationDegrees = new Vector3(0, _isOpen ? 90 : 0, 0);
		// _staticBody.ProcessMode = _isOpen ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
		_staticBody.GetNode<CollisionShape3D>("CollisionShape3D").Disabled = _isOpen;
	}

	private void OpenDoor()
	{
		if (_isOpen)
			return;

		GD.Print("Opening door");
		_isOpen = true;
		_interactiveComponent.IsInteractive = false;
		var tween = CreateTween();
		tween.TweenProperty(_door, "rotation_degrees:y", 90, 0.5f);
		tween.Finished += () =>
		{
			_interactiveComponent.IsInteractive = true;
			UpdateDoor();
		};
	}

	private void CloseDoor()
	{
		if (!_isOpen)
			return;

		GD.Print("Closing door");
		_isOpen = false;
		_interactiveComponent.IsInteractive = false;
		var tween = CreateTween();
		tween.TweenProperty(_door, "rotation_degrees:y", 0, 0.5f);
		tween.Finished += () =>
		{
			_interactiveComponent.IsInteractive = true;
			UpdateDoor();
		};
	}

	public void OnInteract(Player actor)
	{
		if (_isOpen)
		{
			CloseDoor();
		}
		else
		{
			OpenDoor();
		}
	}
}
