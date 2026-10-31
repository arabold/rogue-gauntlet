using Godot;
using System.Collections.Generic;

/// <summary>Explicit identity and geometry of an operable interior doorway, independent of room-exit connectors.</summary>
[Tool, GlobalClass]
public partial class InteriorDoorComponent : Node
{
	[Export] public Door Door { get; set; }
	[Export] public string Id { get; set; } = "";
	private float _openingWidth = 2.4f;
	[Export(PropertyHint.Range, "2.1,4,0.1")]
	public float OpeningWidth
	{
		get => _openingWidth;
		set { _openingWidth = value; if (IsNodeReady()) ApplyGeometry(); }
	}

	public override void _Ready() => ApplyGeometry();

	/// <summary>The authored gate mesh spans local X 0..1 from its hinge; collision and navigation remain instance-local.</summary>
	public void ApplyGeometry()
	{
		if (Door?.Leaf == null || Door.Collision == null || Door.NavigationLink == null) return;
		Door.Leaf.Position = new(-OpeningWidth / 2, 0, 0);
		Door.Leaf.Scale = new(OpeningWidth, 1, 1);
		Door.Collision.Shape = new BoxShape3D { Size = new(OpeningWidth, 4, .3f) };
		Door.Collision.Position = new(0, 2, 0);
		if (string.IsNullOrEmpty(Door.ConnectionId)) Door.ConnectionId = "interior:" + Id;
	}

	/// <summary>Protect the moving leaf and both approaches from furniture without treating an openable door as a permanent wall.</summary>
	public Aabb FurnitureClearance => new(-OpeningWidth / 2 - .1f, 0, -OpeningWidth - .15f,
		OpeningWidth + .2f, 2.5f, OpeningWidth + 2.15f);

	public static IReadOnlyList<Aabb> CollectClearances(Node root, Transform3D toTarget)
	{
		var result = new List<Aabb>();
		Collect(root, toTarget);
		return result;
		void Collect(Node node, Transform3D transform)
		{
			if (node is Door door)
			{
				if (door.Interior != null) result.Add(transform * door.Interior.FurnitureClearance);
				return;
			}
			foreach (var child in node.GetChildren()) Collect(child, transform * (child is Node3D spatial ? spatial.Transform : Transform3D.Identity));
		}
	}
}