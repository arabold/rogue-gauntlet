using Godot;
using System.Collections.Generic;

/// <summary>Actor-local immediate overlap queries and debug geometry; never applies damage.</summary>
[GlobalClass]
public partial class MeleeHitDetector : Node
{
	private static readonly StringName RidKey = "rid";
	private static readonly StringName ColliderKey = "collider";

	[Export(PropertyHint.Range, "8,1024,8")] public int QueryBatchSize { get; set; } = 128;
	private Node3D _actor;
	private MeleeAttackProfile _profile;
	private Shape3D _shape;
	private Shape3D _bodyShape;
	private uint _targetMask;
	private MeshInstance3D _debugMesh, _bodyDebugMesh;
	private readonly Godot.Collections.Array<Rid> _self = new();
	public int BlockedContacts { get; private set; }

	/// <summary>Capture runtime geometry; authored definitions are never modified.</summary>
	public void Begin(Node3D actor, MeleeAttackProfile profile, uint mask, bool debug)
	{
		End();
		_actor = actor; _profile = profile; _targetMask = mask;
		_shape = profile.CreateShape();
		_bodyShape = CreateBodyContactShape(actor);
		_self.Clear();
		CollectRids(actor, _self);
		if (debug)
		{
			_debugMesh = CreateDebugMesh(_shape);
			if (_bodyShape != null) _bodyDebugMesh = CreateDebugMesh(_bodyShape);
		}
	}

	/// <summary>
	/// Cover the physical body's footprint, including its rear, during the same contact window.
	/// Keep this separate from the forward shape so convex hulls cannot fill unintended flank space.
	/// </summary>
	private static Shape3D CreateBodyContactShape(Node3D actor)
	{
		var body = actor.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
		if (body?.Shape == null || body.Disabled) return null;
		float radius = body.Shape switch
		{
			SphereShape3D sphere => sphere.Radius,
			CapsuleShape3D capsule => capsule.Radius,
			CylinderShape3D cylinder => cylinder.Radius,
			BoxShape3D box => new Vector2(box.Size.X, box.Size.Z).Length() * .5f,
			_ => 0
		};
		if (radius <= 0) return null;
		Vector3 scale = body.GlobalBasis.Scale.Abs();
		Vector3 offset = body.GlobalPosition - actor.GlobalPosition;
		radius = radius * Mathf.Max(scale.X, scale.Z) + new Vector2(offset.X, offset.Z).Length();
		// Physical bodies rest just outside each other; allow a small contact skin.
		return new CylinderShape3D { Radius = radius + .1f, Height = MeleeAttackProfile.ContactHeight };
	}

	private MeshInstance3D CreateDebugMesh(Shape3D shape)
	{
		var mesh = new MeshInstance3D { Mesh = shape.GetDebugMesh(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		mesh.MaterialOverride = new StandardMaterial3D {
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(0.2f, 1, 0.4f, 0.12f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha, NoDepthTest = true
		};
		_actor.AddChild(mesh);
		mesh.TopLevel = true;
		mesh.Visible = false;
		return mesh;
	}

	/// <summary>Query existing overlaps immediately, paging collider RIDs to avoid crowd truncation.</summary>
	public Godot.Collections.Array<Node3D> Sample(Transform3D frame)
	{
		var contacts = new Godot.Collections.Array<Node3D>();
		if (_shape == null || !_actor.IsInsideTree()) return contacts;
		BlockedContacts = 0;
		// Presentation can sit at the weapon's height; collision stays centered on the actor.
		frame.Origin = new Vector3(frame.Origin.X, _actor.GlobalPosition.Y, frame.Origin.Z);
		if (_debugMesh != null) { _debugMesh.Visible = true; _debugMesh.GlobalTransform = frame; }
		if (_bodyDebugMesh != null) { _bodyDebugMesh.Visible = true; _bodyDebugMesh.GlobalTransform = frame; }
		var seen = new HashSet<ulong>();
		if (_shape != null) SampleShape(_shape, frame, seen, contacts);
		if (_bodyShape != null) SampleShape(_bodyShape, frame, seen, contacts);
		return contacts;
	}

	private void SampleShape(Shape3D shape, Transform3D frame, HashSet<ulong> seen, Godot.Collections.Array<Node3D> contacts)
	{
		var exclude = new Godot.Collections.Array<Rid>(_self);
		var excludedRids = new HashSet<Rid>(_self);
		using var query = new PhysicsShapeQueryParameters3D {
			Shape = shape, Transform = frame, CollisionMask = _targetMask,
			CollideWithAreas = true, CollideWithBodies = true, Exclude = exclude
		};
		var space = _actor.GetWorld3D().DirectSpaceState;
		int batch = Mathf.Max(8, QueryBatchSize);
		while (true)
		{
			var results = space.IntersectShape(query, batch);
			bool progressed = false;
			foreach (var hit in results)
			{
				Rid rid = hit[RidKey].AsRid();
				if (excludedRids.Add(rid)) { exclude.Add(rid); progressed = true; }
				if (hit[ColliderKey].AsGodotObject() is not Node3D collider) continue;
				Node3D target = collider is HurtBoxComponent ? collider : collider.GetNodeOrNull<HurtBoxComponent>("HurtBoxComponent") ?? collider;
				Node owner = target is HurtBoxComponent ? target.GetParent() : target;
				if (target is not IDamageable || owner == _actor || _actor.IsAncestorOf(owner)
					|| owner.IsAncestorOf(_actor) || !seen.Add(owner.GetInstanceId())) continue;
				if (IsBlocked(space, frame.Origin, target, rid)) { BlockedContacts++; continue; }
				contacts.Add(target);
			}
			if (results.Count < batch || !progressed) break;
			query.Exclude = exclude;
		}
	}

	private bool IsBlocked(PhysicsDirectSpaceState3D space, Vector3 origin, Node3D target, Rid targetRid)
	{
		// Ignore target/weapon elevation for obstruction as well: low targets must not
		// send a ray into the floor, and high targets must not bypass a closed door.
		origin.Y = MeleeAttackProfile.BlockingHeight;
		var destination = new Vector3(target.GlobalPosition.X, origin.Y, target.GlobalPosition.Z);
		if (_profile.BlockingMask == 0 || origin.DistanceSquaredTo(destination) < .0001f) return false;
		var excluded = new Godot.Collections.Array<Rid>(_self) { targetRid };
		if (target.GetParent() is CollisionObject3D body) excluded.Add(body.GetRid());
		using var ray = PhysicsRayQueryParameters3D.Create(origin, destination, _profile.BlockingMask, excluded);
		ray.CollideWithAreas = false;
		ray.HitFromInside = true;
		return space.IntersectRay(ray).Count > 0;
	}

	private static void CollectRids(Node node, Godot.Collections.Array<Rid> rids)
	{
		if (node is CollisionObject3D collider) rids.Add(collider.GetRid());
		foreach (Node child in node.GetChildren()) CollectRids(child, rids);
	}

	public void CloseWindow()
	{
		if (GodotObject.IsInstanceValid(_debugMesh)) _debugMesh.Visible = false;
		if (GodotObject.IsInstanceValid(_bodyDebugMesh)) _bodyDebugMesh.Visible = false;
	}

	public void End()
	{
		if (GodotObject.IsInstanceValid(_debugMesh)) _debugMesh.QueueFree();
		if (GodotObject.IsInstanceValid(_bodyDebugMesh)) _bodyDebugMesh.QueueFree();
		_debugMesh = _bodyDebugMesh = null;
		_shape?.Dispose(); _shape = null;
		_bodyShape?.Dispose(); _bodyShape = null;
		_profile = null; _actor = null;
		_self.Clear();
	}

	public override void _ExitTree() => End();
}