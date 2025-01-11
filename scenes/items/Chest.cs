using Godot;

[Tool]
public partial class Chest : ItemBase
{
	[Export] public int GoldAmount { get; set; } = 10;
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
	private HealthComponent _healthComponent;
	private HurtBoxComponent _hurtBoxComponent;
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
			_hurtBoxComponent = GetNode<HurtBoxComponent>("HurtBoxComponent");
			_healthComponent = GetNode<HealthComponent>("HealthComponent");
			_healthComponent.Died += OnDie;
		}

		UpdateChestLid();
	}

	private void OnDie()
	{
		OnInteract(null);
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
		tween.TweenProperty(_chestLid, "rotation_degrees:x", -45, 0.5f);

		// Disable any interactivity
		_hurtBoxComponent.Monitoring = false;
		_hurtBoxComponent.Monitorable = false;

		// TODO: Drop gold
	}

	private void OnInteract(Player actor)
	{
		OpenChest();
	}
}
