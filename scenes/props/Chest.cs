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
			if (IsNodeReady() && !_isAnimating) { Update(); }
		}
	} = false;

	private const float OpenRotationDegrees = -45f;
	private const float AnimationDuration = 0.5f;

	private InteractiveComponent _interactiveComponent;
	private LootTableComponent _lootTableComponent;
	private MeshInstance3D _chest;
	private MeshInstance3D _chestLid;
	private bool _isAnimating;

	public override void _Ready()
	{
		_chest = GetNode<MeshInstance3D>("chest");
		_chestLid = GetNode<MeshInstance3D>("chest/chest_lid");

		if (!Engine.IsEditorHint())
		{
			_interactiveComponent = GetNode<InteractiveComponent>("InteractiveComponent");
			_interactiveComponent.Interacted += OnInteract;

			_lootTableComponent = GetNodeOrNull<LootTableComponent>("LootTableComponent");
		}

		Update();
	}

	private void Update()
	{
		if (_chestLid == null)
			return;
		_chestLid.RotationDegrees = new Vector3(IsOpen ? OpenRotationDegrees : 0, 0, 0);
	}

	private void OpenChest()
	{
		if (IsOpen || _isAnimating)
			return;

		_isAnimating = true;
		_interactiveComponent.IsInteractive = false;
		IsOpen = true;

		var tween = CreateTween();
		tween.TweenProperty(_chestLid, "rotation_degrees:x", OpenRotationDegrees, AnimationDuration)
			.SetTrans(Tween.TransitionType.Sine)
			.SetEase(Tween.EaseType.Out);
		tween.Finished += () =>
		{
			_isAnimating = false;
			Update();
			OnOpened();
		};
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
