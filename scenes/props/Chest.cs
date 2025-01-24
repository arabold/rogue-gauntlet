using Godot;

[Tool]
public partial class Chest : Node3D
{
	[Signal] public delegate void OpenedEventHandler(Chest chest);

	[Export]
	public bool IsOpen
	{
		get => _isOpen;
		set
		{
			if (value != _isOpen)
			{
				_isOpen = value;
				UpdateChestLid();
			}
		}
	}

	private bool _isOpen = false;
	private InteractiveComponent _interactiveComponent;
	private MeshInstance3D _chest;
	private MeshInstance3D _chestLid;

	public override void _Ready()
	{
		base._Ready();

		_chest = GetNode<MeshInstance3D>("chest");
		_chestLid = _chest.GetNode<MeshInstance3D>("chest_lid");

		if (!Engine.IsEditorHint())
		{
			_interactiveComponent = GetNode<InteractiveComponent>("InteractiveComponent");
			_interactiveComponent.Interacted += OnInteract;
		}

		UpdateChestLid();
	}

	private void UpdateChestLid()
	{
		if (_chestLid == null)
			return;
		_chestLid.RotationDegrees = new Vector3(_isOpen ? -45 : 0, 0, 0);
	}

	private void OpenChest()
	{
		if (_isOpen)
			return;

		_isOpen = true;

		var tween = CreateTween();
		tween
			.TweenProperty(_chestLid, "rotation_degrees:x", -45, 0.5f)
			.Finished += () => EmitSignal(SignalName.Opened, this);

		// Disable any interactivity
		_interactiveComponent.QueueFree();
	}

	private void OnInteract(Player actor)
	{
		OpenChest();
	}
}
