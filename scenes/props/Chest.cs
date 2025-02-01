using Godot;

[Tool]
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
			if (IsNodeReady()) { Update(); }
		}
	} = false;

	private InteractiveComponent _interactiveComponent;
	private LootTableComponent _lootTableComponent;
	private MeshInstance3D _chest;
	private MeshInstance3D _chestLid;

	public override void _Ready()
	{
		_chest = GetNode<MeshInstance3D>("chest");
		_chestLid = GetNode<MeshInstance3D>("chest_lid");

		if (!Engine.IsEditorHint())
		{
			_interactiveComponent = GetNode<InteractiveComponent>("InteractiveComponent");
			_interactiveComponent.Interacted += OnInteract;

			_lootTableComponent = GetNode<LootTableComponent>("LootTableComponent");
		}

		Update();
	}

	private void Update()
	{
		if (_chestLid == null)
			return;
		_chestLid.RotationDegrees = new Vector3(IsOpen ? -45 : 0, 0, 0);
	}

	private void OpenChest()
	{
		if (IsOpen)
			return;

		IsOpen = true;

		var tween = CreateTween();
		tween
			.TweenProperty(_chestLid, "rotation_degrees:x", -45, 0.5f)
			.Finished += OnOpened;

		// Disable any interactivity (e.g. prevent the player from 
		// opening the chest again)
		_interactiveComponent.QueueFree();
	}

	private void OnInteract(Player actor)
	{
		OpenChest();
	}

	public void OnOpened()
	{
		_lootTableComponent?.DropLoot();
		EmitSignalOpened(this);
	}
}
