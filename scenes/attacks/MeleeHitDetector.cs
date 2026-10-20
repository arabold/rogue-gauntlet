using Godot;
using System.Collections.Generic;

/// <summary>Actor-local immediate overlap queries and debug geometry; never applies damage.</summary>
public partial class MeleeHitDetector : Node
{
	[Export(PropertyHint.Range, "8,1024,8")] public int QueryBatchSize { get; set; } = 128;
	private Node3D _actor;
	private MeleeAttackProfile _profile;
	private Shape3D _shape;
	private uint _targetMask;
	private MeshInstance3D _debugMesh;
	private readonly Godot.Collections.Array<Rid> _self = new();
	public int BlockedContacts { get; private set; }

	/// <summary>Capture runtime geometry; authored definitions are never modified.</summary>
	public void Begin(Node3D actor, MeleeAttackProfile profile, uint mask, bool debug)
	{
		End();
		_actor = actor; _profile = profile; _targetMask = mask;
		_shape = profile.CreateShape();
		_self.Clear();
		CollectRids(actor, _self);
		if (debug)
		{
			_debugMesh = new MeshInstance3D { Mesh = _shape.GetDebugMesh(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			_debugMesh.MaterialOverride = new StandardMaterial3D {
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(0.2f, 1, 0.4f, 0.12f),
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha, NoDepthTest = true
			};
			actor.AddChild(_debugMesh);
			_debugMesh.TopLevel = true;
			_debugMesh.Visible = false;
		}
	}

	/// <summary>Query existing overlaps immediately, paging collider RIDs to avoid crowd truncation.</summary>
	public Godot.Collections.Array<Node3D> Sample(Transform3D frame)
	{
		var contacts = new Godot.Collections.Array<Node3D>();
		if (_shape == null || !_actor.IsInsideTree()) return contacts;
		BlockedContacts = 0;
		if (_debugMesh != null) { _debugMesh.Visible = true; _debugMesh.GlobalTransform = frame; }
		var exclude = new Godot.Collections.Array<Rid>(_self);
		var seen = new HashSet<ulong>();
		var excludedRids = new HashSet<Rid>(_self);
		using var query = new PhysicsShapeQueryParameters3D {
			Shape = _shape, Transform = frame, CollisionMask = _targetMask,
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
				Rid rid = hit["rid"].AsRid();
				if (excludedRids.Add(rid)) { exclude.Add(rid); progressed = true; }
				if (hit["collider"].AsGodotObject() is not Node3D collider) continue;
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
		return contacts;
	}

	private bool IsBlocked(PhysicsDirectSpaceState3D space, Vector3 origin, Node3D target, Rid targetRid)
	{
		if (_profile.BlockingMask == 0 || origin.DistanceSquaredTo(target.GlobalPosition) < .0001f) return false;
		var excluded = new Godot.Collections.Array<Rid>(_self) { targetRid };
		if (target.GetParent() is CollisionObject3D body) excluded.Add(body.GetRid());
		using var ray = PhysicsRayQueryParameters3D.Create(origin, target.GlobalPosition, _profile.BlockingMask, excluded);
		ray.CollideWithAreas = false;
		ray.HitFromInside = true;
		return space.IntersectRay(ray).Count > 0;
	}

	private static void CollectRids(Node node, Godot.Collections.Array<Rid> rids)
	{
		if (node is CollisionObject3D collider) rids.Add(collider.GetRid());
		foreach (Node child in node.GetChildren()) CollectRids(child, rids);
	}

	public void CloseWindow() { if (GodotObject.IsInstanceValid(_debugMesh)) _debugMesh.Visible = false; }

	public void End()
	{
		if (GodotObject.IsInstanceValid(_debugMesh)) _debugMesh.QueueFree();
		_debugMesh = null;
		_shape?.Dispose(); _shape = null;
		_profile = null; _actor = null;
		_self.Clear();
	}

	public override void _ExitTree() => End();
}