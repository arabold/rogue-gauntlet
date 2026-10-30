using Godot;
using System.Collections.Generic;

/// <summary>
/// Shows an item model as an inventory or hotbar icon. Poses it by its <see cref="ItemIconPose"/>, or else by
/// <see cref="ItemIconFraming.Auto"/>, frames it to fill the viewport and renders it once, so an idle icon costs
/// nothing per frame. While <see cref="Animated"/> the model gently sways and renders live.
/// </summary>
[Tool]
public partial class Preview : SubViewport
{
	[Export]
	public PackedScene Scene
	{
		get;
		set
		{
			// The preview depends only on the scene, so re-setting the shown one would just rebuild the same model.
			if (field == value && GodotObject.IsInstanceValid(_object))
			{
				return;
			}

			field = value;
			if (IsNodeReady())
			{
				Refresh();
			}
		}
	}

	/// <summary>Lays long models along the slot diagonal, Diablo-style. Meant for weapons; short models and authored poses ignore it.</summary>
	[Export]
	public bool Diagonal
	{
		get;
		set
		{
			if (field == value)
			{
				return;
			}

			field = value;
			if (IsNodeReady())
			{
				Frame();
			}
		}
	}

	/// <summary>
	/// Sways the model and renders every frame, e.g. while its slot is hovered or focused. Turning it off eases back to
	/// the rest pose, renders that once more and goes idle.
	/// </summary>
	[Export]
	public bool Animated
	{
		get;
		set
		{
			field = value;
			if (value && IsNodeReady())
			{
				StartSway();
			}
		}
	}

	/// <summary>How far the model turns each way while <see cref="Animated"/>. The framing leaves room for it.</summary>
	[Export(PropertyHint.Range, "0,60,1,suffix:°")]
	public float SwayDegrees
	{
		get;
		set
		{
			field = value;
			if (IsNodeReady())
			{
				Frame();
			}
		}
	} = 25f;

	[Export(PropertyHint.Range, "0.5,10,0.1,suffix:s")] public float SwayPeriod { get; set; } = 3f;

	/// <summary>Space around the framed model, as a factor of its extent.</summary>
	[Export(PropertyHint.Range, "1,2,0.01")]
	public float Padding
	{
		get;
		set
		{
			field = value;
			if (IsNodeReady())
			{
				Frame();
			}
		}
	} = 1.12f;

	[Export] public Camera3D Camera { get; set; }

	/// <summary>Seconds the sway takes to fade in or out.</summary>
	private const float SwayEaseTime = 0.25f;

	private Node3D _pivot;
	private Node3D _object;
	private readonly List<Vector3> _points = [];
	private Basis? _authoredPose;
	private Transform3D _rest = Transform3D.Identity;
	private Vector3 _swayAxis = Vector3.Up;
	private float _swayWeight;
	private double _swayTime;

	public override void _Ready()
	{
		OwnWorld3D = true;
		// The model is posed from code and _Process, never by physics ticks: interpolating it would render a single
		// once-only frame between its previous and new pose, and make the sway stutter.
		PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
		SetProcess(false);
		// The aspect ratio decides the fit, and a resized render target needs rendering again.
		SizeChanged += Frame;
		Refresh();
		if (Animated)
		{
			StartSway();
		}
	}

	public override string[] _GetConfigurationWarnings() =>
		Camera == null ? ["Camera is required to frame and render the model."] : [];

	/// <summary>Shows <paramref name="item"/>'s run appearance; weapons lie diagonally.</summary>
	public void ShowItem(Item item)
	{
		Diagonal = item is Weapon;
		Scene = ItemIdentity.ResolveScene(item);
	}

	public void Refresh()
	{
		if (Camera == null)
		{
			return;
		}

		if (_pivot != null)
		{
			// Hidden at once so the next render never shows the outgoing model.
			_pivot.Visible = false;
			_pivot.QueueFree();
		}
		_pivot = null;
		_points.Clear();
		_object = Scene?.Instantiate<Node3D>();
		if (_object != null)
		{
			_pivot = new Node3D();
			_pivot.AddChild(_object);
			AddChild(_pivot);
			// Measured once in the tree: model scripts may toggle parts in _Ready (e.g. the coin stack's size).
			_authoredPose = Measure(_object, _points);
		}
		Frame();
	}

	/// <summary>Poses and fits the current model, then renders it once.</summary>
	private void Frame()
	{
		if (_pivot != null && _points.Count > 0)
		{
			ItemIconFraming.Pose pose = _authoredPose is { } marker
				? ItemIconFraming.FromMarker(marker)
				: ItemIconFraming.Auto(_points, Diagonal);
			float aspect = Size.Y > 0 ? (float)Size.X / Size.Y : 1f;
			(_rest, Camera.Size) = ItemIconFraming.Fit(_points, pose, Mathf.DegToRad(SwayDegrees), aspect, Padding);
			_swayAxis = pose.SwayAxis;
			ApplySway();
		}
		RenderOnce();
	}

	/// <summary>
	/// Gathers the visible mesh vertices of <paramref name="model"/> into <paramref name="points"/>, in the space of the
	/// model's parent so its own root transform counts (some wrappers bake a scale there). Lights, particles and other
	/// non-mesh visuals are not part of the item's silhouette and are skipped. Returns the basis of the model's
	/// <see cref="ItemIconPose"/> in the same space, or null when it has none.
	/// </summary>
	private static Basis? Measure(Node3D model, List<Vector3> points)
	{
		Basis? marker = null;
		Visit(model, model.Transform);
		return marker;

		void Visit(Node3D node, Transform3D transform)
		{
			if (!node.Visible)
			{
				return;
			}
			if (node is ItemIconPose)
			{
				marker ??= transform.Basis;
			}
			if (node is MeshInstance3D { Mesh: { } mesh })
			{
				Vector3[] faces = mesh.GetFaces();
				if (faces.Length > 0)
				{
					foreach (Vector3 vertex in faces)
					{
						points.Add(transform * vertex);
					}
				}
				else
				{
					// Without mesh data on the CPU (e.g. the headless renderer) its bounding box still frames it.
					Aabb box = mesh.GetAabb();
					for (int corner = 0; corner < 8; corner++)
					{
						points.Add(transform * box.GetEndpoint(corner));
					}
				}
			}
			foreach (Node child in node.GetChildren())
			{
				if (child is Node3D child3D)
				{
					Visit(child3D, transform * child3D.Transform);
				}
			}
		}
	}

	public override void _Process(double delta)
	{
		_swayTime += delta;
		_swayWeight = Mathf.MoveToward(_swayWeight, Animated ? 1f : 0f, (float)delta / SwayEaseTime);
		ApplySway();
		if (!Animated && _swayWeight <= 0f)
		{
			SetProcess(false);
			RenderOnce();
		}
	}

	private void StartSway()
	{
		if (!IsProcessing())
		{
			_swayTime = 0;
		}
		SetProcess(true);
		RenderTargetUpdateMode = UpdateMode.Always;
	}

	private void ApplySway()
	{
		if (_pivot == null)
		{
			return;
		}

		float angle = Mathf.DegToRad(SwayDegrees) * _swayWeight * Mathf.Sin(Mathf.Tau * (float)(_swayTime / SwayPeriod));
		_pivot.Transform = new Transform3D(new Basis(_swayAxis, angle), Vector3.Zero) * _rest;
	}

	private void RenderOnce()
	{
		// While swaying it already renders every frame.
		if (!IsProcessing())
		{
			RenderTargetUpdateMode = UpdateMode.Once;
		}
	}
}
