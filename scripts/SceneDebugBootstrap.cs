using Godot;

/// <summary>
/// Adds minimal editor-run staging around standalone scenes so props and small
/// assets are visible when using Godot's Run Current Scene command.
/// </summary>
public partial class SceneDebugBootstrap : Node
{
	private const string DebugRootName = "SceneDebugEnvironment";
	private const string MenuScenePath = "res://scenes/menu/main_menu.tscn";
	private const string GameplayScenePath = "res://scenes/main/main.tscn";
	private const float OrbitSensitivity = 0.01f;
	private const float PanSensitivity = 0.0025f;
	private const float ZoomStep = 1.0f;
	private const float MinCameraDistance = 3.0f;
	private const float MaxCameraDistance = 30.0f;
	private const float MinCameraElevation = 5.0f;
	private const float MaxCameraElevation = 85.0f;

	private Camera3D _debugCamera;
	private Vector3 _cameraTarget = Vector3.Zero;
	private float _cameraYaw = Mathf.DegToRad(45.0f);
	private float _cameraElevation = Mathf.DegToRad(50.0f);
	private float _cameraDistance = 15.0f;
	private bool _isOrbiting;
	private bool _isPanning;

	public override void _Ready()
	{
		if (!OS.IsDebugBuild())
		{
			return;
		}

		CallDeferred(MethodName.SetupDebugEnvironment);
	}

	private void SetupDebugEnvironment()
	{
		Node currentScene = GetTree().CurrentScene;
		if (currentScene == null || ShouldSkip(currentScene))
		{
			return;
		}

		if (currentScene.HasNode(DebugRootName))
		{
			return;
		}

		var debugRoot = new Node3D
		{
			Name = DebugRootName,
		};
		currentScene.AddChild(debugRoot);

		AddFloor(debugRoot, currentScene);
		AddLight(debugRoot);
		AddCamera(debugRoot, currentScene);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_debugCamera == null || !GodotObject.IsInstanceValid(_debugCamera))
		{
			return;
		}

		if (@event is InputEventMouseButton mouseButton)
		{
			HandleMouseButton(mouseButton);
			return;
		}

		if (@event is InputEventMouseMotion mouseMotion)
		{
			HandleMouseMotion(mouseMotion);
		}
	}

	private static bool ShouldSkip(Node currentScene)
	{
		return currentScene.SceneFilePath == MenuScenePath
			|| currentScene.SceneFilePath == GameplayScenePath;
	}

	private static void AddFloor(Node3D debugRoot, Node currentScene)
	{
		if (SceneAlreadyHasFloor(currentScene))
		{
			return;
		}

		var meshInstance = new MeshInstance3D
		{
			Name = "DebugFloor",
			Mesh = new PlaneMesh
			{
				Size = new Vector2(20, 20),
			},
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.24f, 0.24f, 0.24f),
				Roughness = 1.0f,
			},
		};
		debugRoot.AddChild(meshInstance);
	}

	private static bool SceneAlreadyHasFloor(Node scene)
	{
		foreach (Node child in scene.FindChildren("*", nameof(GridMap), true, false))
		{
			if (child.Name.ToString().Contains("Floor"))
			{
				return true;
			}
		}

		return false;
	}

	private static void AddLight(Node3D debugRoot)
	{
		var light = new DirectionalLight3D
		{
			Name = "DebugKeyLight",
			LightEnergy = 2.0f,
			RotationDegrees = new Vector3(-50, -35, 0),
		};
		debugRoot.AddChild(light);
	}

	private void AddCamera(Node3D debugRoot, Node currentScene)
	{
		if (currentScene.FindChildren("*", nameof(Camera3D), true, false).Count > 0)
		{
			return;
		}

		var camera = new Camera3D
		{
			Name = "DebugCamera",
			Current = true,
		};
		debugRoot.AddChild(camera);
		_debugCamera = camera;
		UpdateDebugCamera();
	}

	private void HandleMouseButton(InputEventMouseButton mouseButton)
	{
		if (mouseButton.ButtonIndex == MouseButton.Right)
		{
			_isOrbiting = mouseButton.Pressed;
			GetViewport().SetInputAsHandled();
			return;
		}

		if (mouseButton.ButtonIndex == MouseButton.Middle)
		{
			_isPanning = mouseButton.Pressed;
			GetViewport().SetInputAsHandled();
			return;
		}

		if (mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.WheelUp)
		{
			_cameraDistance = Mathf.Max(MinCameraDistance, _cameraDistance - ZoomStep);
			UpdateDebugCamera();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.WheelDown)
		{
			_cameraDistance = Mathf.Min(MaxCameraDistance, _cameraDistance + ZoomStep);
			UpdateDebugCamera();
			GetViewport().SetInputAsHandled();
		}
	}

	private void HandleMouseMotion(InputEventMouseMotion mouseMotion)
	{
		if (_isOrbiting)
		{
			_cameraYaw -= mouseMotion.Relative.X * OrbitSensitivity;
			_cameraElevation = Mathf.Clamp(
				_cameraElevation - mouseMotion.Relative.Y * OrbitSensitivity,
				Mathf.DegToRad(MinCameraElevation),
				Mathf.DegToRad(MaxCameraElevation));
			UpdateDebugCamera();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (_isPanning)
		{
			Basis basis = _debugCamera.GlobalTransform.Basis;
			Vector3 right = basis.X;
			Vector3 forward = -basis.Z;
			right.Y = 0;
			forward.Y = 0;

			_cameraTarget += (-right.Normalized() * mouseMotion.Relative.X
				+ forward.Normalized() * mouseMotion.Relative.Y)
				* PanSensitivity * _cameraDistance;

			UpdateDebugCamera();
			GetViewport().SetInputAsHandled();
		}
	}

	private void UpdateDebugCamera()
	{
		float horizontalDistance = Mathf.Cos(_cameraElevation) * _cameraDistance;
		Vector3 offset = new(
			Mathf.Sin(_cameraYaw) * horizontalDistance,
			Mathf.Sin(_cameraElevation) * _cameraDistance,
			Mathf.Cos(_cameraYaw) * horizontalDistance);

		_debugCamera.GlobalPosition = _cameraTarget + offset;
		_debugCamera.LookAt(_cameraTarget, Vector3.Up);
	}
}
