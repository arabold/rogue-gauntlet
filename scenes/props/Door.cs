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
	private MeshInstance3D _xray;
	private InteractiveComponent _interactiveComponent;
	private bool _isAnimating;

	public override void _Ready()
	{
		_collisionShape = GetNode<CollisionShape3D>("%CollisionShape3D");
		_door = GetNode<MeshInstance3D>("%wall_doorway_door");

		if (!Engine.IsEditorHint())
		{
			_interactiveComponent = GetNode<InteractiveComponent>("InteractiveComponent");
			_interactiveComponent.Interacted += OnInteract;
			CreateIndicator();
		}

		Update();
	}

	/// <summary>
	/// Builds an x-ray silhouette of the door. Its shader only draws where the door
	/// is occluded by scene geometry (a wall or the black fog cap) and fades with
	/// distance, so a hidden door reads from any camera angle without showing when
	/// it is already in plain view.
	/// </summary>
	private void CreateIndicator()
	{
		var material = new ShaderMaterial
		{
			Shader = GD.Load<Shader>("res://scenes/props/door_xray.gdshader"),
			RenderPriority = 8,
		};

		_xray = new MeshInstance3D
		{
			Mesh = _door.Mesh,
			MaterialOverride = material,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Visible = !IsOpen,
		};
		_door.AddChild(_xray);
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
		_xray.Visible = false; // No indicator needed once the door is open.
		SignalBus.EmitDoorOpened(this);

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
		_xray.Visible = true; // Shader decides when it actually appears.

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
