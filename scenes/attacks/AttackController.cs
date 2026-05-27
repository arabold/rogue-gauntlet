using Godot;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// Handles timing-synchronized hit detection, VFX/SFX, and projectile spawning for actors.
/// Replaces the legacy pre-authored attack nodes.
/// </summary>
public partial class AttackController : Node
{
	[Export] public bool DebugDrawEnabled { get; set; } = true;

	private static PackedScene _hitBoxScene = GD.Load<PackedScene>("res://scenes/components/hit_box_component.tscn");
	private static PackedScene _defaultProjectileScene = GD.Load<PackedScene>("res://scenes/attacks/projectile.tscn");

	private bool _isAttacking;
	private float _elapsedTime;
	private AttackDefinition _currentAttack;
	private float _currentMinDamage;
	private float _currentMaxDamage;
	private float _currentAccuracy;
	private float _currentCritChance;
	private uint _targetMask;

	private HitBoxComponent _activeHitBox;
	private bool _hitWindowOpen;
	private bool _projectileSpawned;
	private HashSet<Node> _hitTargets = new HashSet<Node>();
	private MeshInstance3D _debugMesh;

	private Node3D _actor;

	public override void _Ready()
	{
		_actor = GetParent<Node3D>();
	}

	/// <summary>
	/// Begins playing an attack with its stats and targeting parameters.
	/// </summary>
	public void StartAttack(AttackDefinition def, float minDamage, float maxDamage, float accuracy, float critChance, uint targetMask)
	{
		_actor ??= GetParent<Node3D>();
		if (_actor == null)
		{
			GD.PushError($"{Name} cannot start attack because it has no Node3D parent actor.");
			return;
		}

		CancelAttack();

		_currentAttack = def;
		_currentMinDamage = minDamage;
		_currentMaxDamage = maxDamage;
		_currentAccuracy = accuracy;
		_currentCritChance = critChance;
		_targetMask = targetMask;

		_elapsedTime = 0.0f;
		_isAttacking = true;
		_hitWindowOpen = false;
		_projectileSpawned = false;
		_hitTargets.Clear();

		GD.Print($"{_actor.Name} starting attack: {def.AnimationId} (Ranged: {def.IsRanged})");

		// Trigger visual/audio effects
		PlayAttackEffects();

		// Set the process loop active
		SetProcess(true);
	}

	/// <summary>
	/// Cancels any active attack, cleaning up active hitboxes and debug meshes.
	/// </summary>
	public void CancelAttack()
	{
		if (!_isAttacking) return;

		EndHitWindow();

		_isAttacking = false;
		_currentAttack = null;
		SetProcess(false);
	}

	public override void _Process(double delta)
	{
		if (!_isAttacking || _currentAttack == null)
		{
			SetProcess(false);
			return;
		}

		_elapsedTime += (float)delta;

		if (_currentAttack.IsRanged)
		{
			// Spawn projectile exactly when hit window start is reached
			if (_elapsedTime >= _currentAttack.HitWindowStart && !_projectileSpawned)
			{
				SpawnProjectile();
				_projectileSpawned = true;
			}
		}
		else
		{
			// Open hit window
			if (_elapsedTime >= _currentAttack.HitWindowStart && !_hitWindowOpen)
			{
				BeginHitWindow();
			}

			// Close hit window
			if (_elapsedTime >= _currentAttack.HitWindowEnd && _hitWindowOpen)
			{
				EndHitWindow();
			}
		}

		// Finished playing the attack active window
		float totalDuration = _currentAttack.IsRanged ? _currentAttack.HitWindowStart + 0.1f : _currentAttack.HitWindowEnd;
		if (_elapsedTime >= totalDuration)
		{
			CancelAttack();
		}
	}

	private void BeginHitWindow()
	{
		if (_hitWindowOpen || _currentAttack == null) return;

		Node3D attachParent = _actor;
		if (_currentAttack.AttachHitBoxToWeapon)
		{
			attachParent = FindWeaponInstance();
			if (attachParent == null)
			{
				attachParent = FindHandAttachment();
			}
			if (attachParent == null)
			{
				attachParent = _actor;
			}
		}

		GD.Print($"{_actor.Name} opening hit window under: {attachParent.Name}");

		// 2. Instantiate and parent the hitbox
		_activeHitBox = _hitBoxScene.Instantiate<HitBoxComponent>();
		attachParent.AddChild(_activeHitBox);

		// 3. Configure collision mask and shape
		_activeHitBox.CollisionMask = _targetMask;
		_activeHitBox.Monitoring = true;
		_activeHitBox.HitDetected += OnHitDetected;

		var collisionShape = new CollisionShape3D();
		var boxShape = new BoxShape3D();
		boxShape.Size = _currentAttack.HitBoxSize;
		collisionShape.Shape = boxShape;
		collisionShape.Position = _currentAttack.HitBoxOffset;
		_activeHitBox.AddChild(collisionShape);

		SpawnSwingVfx();

		// 4. Spawn debug mesh if enabled
		if (DebugDrawEnabled)
		{
			var debugMeshInstance = new MeshInstance3D();
			var boxMesh = new BoxMesh();
			boxMesh.Size = _currentAttack.HitBoxSize;
			
			var material = new StandardMaterial3D();
			material.AlbedoColor = new Color(1.0f, 0.2f, 0.0f, 0.35f); // transparent orange/red
			material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			
			debugMeshInstance.Mesh = boxMesh;
			debugMeshInstance.MaterialOverride = material;
			debugMeshInstance.Position = _currentAttack.HitBoxOffset;
			_activeHitBox.AddChild(debugMeshInstance);
			_debugMesh = debugMeshInstance;
		}

		// 5. Activate any trail effect on the weapon mesh if it has one
		SetTrailVisible(attachParent, true);

		_hitWindowOpen = true;
	}

	private void EndHitWindow()
	{
		if (!_hitWindowOpen) return;

		GD.Print($"{_actor.Name} closing hit window");

		if (GodotObject.IsInstanceValid(_activeHitBox))
		{
			SetTrailVisible(_activeHitBox.GetParent(), false);
			_activeHitBox.Monitoring = false;
			_activeHitBox.HitDetected -= OnHitDetected;
			_activeHitBox.QueueFree();
		}

		_activeHitBox = null;
		_debugMesh = null;
		_hitWindowOpen = false;
	}

	private void OnHitDetected(Node3D target)
	{
		if (!_hitWindowOpen || _currentAttack == null) return;

		// Prevent hit on the attacker itself
		if (target == _actor || _actor.IsAncestorOf(target) || target.IsAncestorOf(_actor))
		{
			return;
		}

		if (target is IDamageable damageable)
		{
			// Resolve the root entity node to prevent double-hits on multiple collision representations (e.g. HurtBox vs. CharacterBody3D)
			Node damageOwner = target is HurtBoxComponent hurtBox ? (hurtBox.GetParent() ?? target) : target;

			if (_hitTargets.Contains(damageOwner)) return;

			_hitTargets.Add(damageOwner);

			GD.Print($"{_actor.Name} hits {target.Name} (Owner: {damageOwner.Name}) with {_currentAttack.AnimationId}!");

			var direction = -_actor.GlobalTransform.Basis.Z;
			var damage = (float)GD.RandRange(_currentMinDamage, _currentMaxDamage);
			if (GD.Randf() < _currentCritChance)
			{
				damage *= 2;
				GD.Print("Critical hit!");
			}

			damageable.TakeDamage(_currentAccuracy, damage, direction, _actor);
		}
	}

	private void SpawnProjectile()
	{
		if (_currentAttack == null) return;

		// 1. Locate the muzzle point, fallback to in front of actor
		Vector3 origin = _actor.GlobalPosition + Vector3.Up * 1.2f; // default center
		Node3D muzzle = FindMuzzlePoint();
		if (muzzle != null)
		{
			origin = muzzle.GlobalPosition;
		}
		else
		{
			origin = _actor.GlobalPosition - _actor.GlobalTransform.Basis.Z * 1.0f + Vector3.Up * 1.2f;
		}

		// 2. Find closest target in LOS to aim at, fallback to actor's facing direction
		Vector3 direction = Aim(_currentAttack.Range);

		// 3. Spawn the projectile
		PackedScene scene = _currentAttack.ProjectileScene ?? _defaultProjectileScene;
		Projectile projectile = scene.Instantiate<Projectile>();
		projectile.Initialize(
			origin,
			direction,
			_currentAttack.ProjectileSpeed,
			_currentAttack.Range,
			_currentAccuracy,
			_currentMinDamage, _currentMaxDamage,
			_currentCritChance,
			_actor);

		// Always add projectile to current scene root so its lifetime is independent of the weapon/actor
		GetTree().CurrentScene.AddChild(projectile);
		GD.Print($"{_actor.Name} spawned projectile flying {direction}");
	}

	private Node3D FindWeaponInstance()
	{
		var authoredWeapon = FindAuthoredWeaponAttachmentInstance();
		if (authoredWeapon != null)
		{
			return authoredWeapon;
		}

		var attachments = GetBoneAttachments(_actor);
		foreach (var attachment in attachments)
		{
			if (attachment.Visible && attachment.GetChildCount() > 0)
			{
				var child = attachment.GetChild<Node3D>(0);
				if (child != null) return child;
			}
		}
		return null;
	}

	private Node3D FindAuthoredWeaponAttachmentInstance()
	{
		var attachmentManager = _actor.GetNodeOrNull<BoneAttachmentManager>("BoneAttachmentManager");
		if (attachmentManager?.AttachmentNodes == null)
		{
			return null;
		}

		AttachmentType[] preferredTypes =
		{
			AttachmentType.OneHandedWeapon,
			AttachmentType.TwoHandedWeapon,
			AttachmentType.OffhandWeapon,
		};

		foreach (var attachmentType in preferredTypes)
		{
			if (!attachmentManager.AttachmentNodes.TryGetValue(attachmentType, out var attachment)
				|| attachment == null
				|| !attachment.Visible
				|| attachment.GetChildCount() == 0)
			{
				continue;
			}

			var child = attachment.GetChild<Node3D>(0);
			if (child != null)
			{
				return child;
			}
		}

		return null;
	}

	private BoneAttachment3D FindHandAttachment()
	{
		var attachments = GetBoneAttachments(_actor);
		// Prefer right hand attachments
		return attachments.FirstOrDefault(a => 
			a.Name.ToString().Contains("1H_Axe") || 
			a.Name.ToString().Contains("2H_Axe") || 
			a.Name.ToString().Contains("wrist") || 
			a.Name.ToString().Contains("hand.r"));
	}

	private Node3D FindMuzzlePoint()
	{
		// Try to find a Marker3D named "Muzzle" or similar on the active weapon mesh or bone attachments
		Node3D weapon = FindWeaponInstance();
		if (weapon != null)
		{
			var muzzle = weapon.FindChild("Muzzle") as Node3D ?? weapon.FindChild("MuzzlePoint") as Node3D;
			if (muzzle != null) return muzzle;
		}
		return null;
	}

	private List<BoneAttachment3D> GetBoneAttachments(Node root)
	{
		var list = new List<BoneAttachment3D>();
		FindBoneAttachmentsRecursive(root, list);
		return list;
	}

	private void FindBoneAttachmentsRecursive(Node node, List<BoneAttachment3D> list)
	{
		if (node is BoneAttachment3D boneAttachment)
		{
			list.Add(boneAttachment);
		}
		foreach (var child in node.GetChildren())
		{
			FindBoneAttachmentsRecursive(child, list);
		}
	}

	private void SetTrailVisible(Node node, bool visible)
	{
		if (node == null) return;
		var trail = node.FindChild("Trail3D") as MeshInstance3D;
		if (trail != null)
		{
			trail.Visible = visible;
		}
	}

	private void SpawnSwingVfx()
	{
		if (_currentAttack.SwingVfx != null)
		{
			var vfx = _currentAttack.SwingVfx.Instantiate<Node3D>();
			vfx.Position = _currentAttack.HitBoxOffset;
			_activeHitBox.AddChild(vfx);
			return;
		}

		var slashMesh = new MeshInstance3D();
		var mesh = new BoxMesh();
		mesh.Size = new Vector3(
			Mathf.Max(_currentAttack.HitBoxSize.X, 0.15f),
			0.03f,
			Mathf.Max(_currentAttack.HitBoxSize.Z, 0.15f));

		var material = new StandardMaterial3D();
		material.AlbedoColor = new Color(0.8f, 0.95f, 1.0f, 0.35f);
		material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;

		slashMesh.Mesh = mesh;
		slashMesh.MaterialOverride = material;
		slashMesh.Position = _currentAttack.HitBoxOffset;
		slashMesh.RotationDegrees = new Vector3(0.0f, 0.0f, 25.0f);
		_activeHitBox.AddChild(slashMesh);
	}

	private void PlayAttackEffects()
	{
		if (_currentAttack == null) return;

		// Play SFX
		if (_currentAttack.SwingSfx != null)
		{
			// Try to find AudioStreamPlayer3D on actor, or create one dynamically
			var player = _actor.GetNodeOrNull<AudioStreamPlayer3D>("AudioStreamPlayer3D");
			if (player == null)
			{
				player = new AudioStreamPlayer3D();
				player.Name = "DynamicAudioPlayer3D";
				_actor.AddChild(player);
			}
			player.Stream = _currentAttack.SwingSfx;
			player.Play();
		}
	}

	private Vector3 Aim(float range)
	{
		bool isPlayer = _actor.IsInGroup("player");
		string targetGroup = isPlayer ? "enemy" : "player";

		var targets = GetTree().GetNodesInGroup(targetGroup).OfType<Node3D>().OrderBy(n =>
				_actor.GlobalPosition.DistanceTo(n.GlobalPosition));

		foreach (var target in targets)
		{
			if (target is EnemyBase enemy && enemy.IsDead) continue;
			if (target is Player player && player.IsDead) continue;

			if (TestLineOfSight(target, range))
			{
				var collisionShape = target.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
				Vector3 targetCenter = target.GlobalPosition;
				if (collisionShape != null)
				{
					targetCenter = target.GlobalPosition + collisionShape.Transform.Origin;
				}
				else
				{
					targetCenter += Vector3.Up * 1.0f;
				}
				return (targetCenter - _actor.GlobalPosition).Normalized();
			}
		}

		if (isPlayer)
		{
			var damageables = GetTree().GetNodesInGroup("damageable").OfType<Node3D>().OrderBy(n =>
					_actor.GlobalPosition.DistanceTo(n.GlobalPosition));
			foreach (var node in damageables)
			{
				if (node is IDamageable && TestLineOfSight(node, range))
				{
					var collisionShape = node.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
					Vector3 targetCenter = node.GlobalPosition;
					if (collisionShape != null)
					{
						targetCenter = node.GlobalPosition + collisionShape.Transform.Origin;
					}
					else
					{
						targetCenter += Vector3.Up * 0.5f;
					}
					return (targetCenter - _actor.GlobalPosition).Normalized();
				}
			}
		}

		return -_actor.GlobalTransform.Basis.Z;
	}

	private bool TestLineOfSight(Node3D target, float range)
	{
		float distance = _actor.GlobalPosition.DistanceTo(target.GlobalPosition);
		if (distance > range) return false;

		// Corrected mathematically: direction FROM actor TO target (ignoring vertical differences)
		Vector3 directionToTarget = target.GlobalPosition - _actor.GlobalPosition;
		directionToTarget.Y = 0;
		if (directionToTarget.LengthSquared() == 0) return true; // overlapping perfectly
		directionToTarget = directionToTarget.Normalized();

		Vector3 forward = -_actor.GlobalTransform.Basis.Z;
		forward.Y = 0;
		forward = forward.Normalized();

		float angle = Mathf.RadToDeg(forward.AngleTo(directionToTarget));
		if (angle > 60.0f) return false; // within front 120-degree cone (60 degrees each side)

		return true;
	}
}
