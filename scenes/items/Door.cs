using Godot;

[Tool]
public partial class Door : ItemBase
{
	[Export]
	public bool IsOpen
	{
		get => _isOpen;
		set
		{
			if (value != _isOpen)
			{
				_isOpen = value;
				UpdateDoor();
			}
		}
	}

	private bool _isOpen = false;
	private MeshInstance3D _wall;
	private MeshInstance3D _door;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_wall = GetChild<MeshInstance3D>(0);
		_door = _wall.GetChild<MeshInstance3D>(0);

		if (!Engine.IsEditorHint())
		{
			// ...
		}

		UpdateDoor();
	}

	private void UpdateDoor()
	{
		if (_door == null)
			return;
		_door.RotationDegrees = new Vector3(0, _isOpen ? 90 : 0, 0);
	}

	private void OpenDoor()
	{
		if (_isOpen)
			return;

		_isOpen = true;
		var tween = CreateTween();
		tween.TweenProperty(_door, "rotation_degrees:y", 90, 0.5f);
	}

	private void CloseDoor()
	{
		if (!_isOpen)
			return;

		_isOpen = false;
		var tween = CreateTween();
		tween.TweenProperty(_door, "rotation_degrees:y", 0, 0.5f);
	}

	public void OnInteract(Player actor)
	{
		GD.Print("Interacting with the door");
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
