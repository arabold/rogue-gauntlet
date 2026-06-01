using Godot;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// Handles timing-synchronized hit detection and projectile spawning for actors.
/// </summary>
public partial class AttackController : Node
{
	[Export] public PackedScene HitBoxScene { get; set; }
	[Export] public PackedScene DefaultProjectileScene { get; set; }
	[Export] public bool DebugDrawEnabled { get; set; } = false;
	public static bool GlobalDebugDrawEnabled { get; set; } = false;

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

	private Node3D _actor;

	public override void _Ready()
	{
		_actor = GetParent<Node3D>();
		AddToGroup("attack_controller");
	}

	/// <summary>
	/// Begins playing an attack with its stats and targeting parameters.
	/// </summary>
	public void StartAttack(AttackDefinition def, float minDamage, float maxDamage, float accuracy, float critChance, uint targetMask)
	{
		if (def == null)
		{
			GD.PushError($"{Name} cannot start attack without an AttackDefinition.");
			return;
		}

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

		GameDebug.Combat($"{_actor.Name} starting attack: {def.AnimationId} (Ranged: {def.IsRanged})");

		SetProcess(true);
	}

	/// <summary>
	/// Cancels any active attack and cleans up active hitboxes.
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

		if (HitBoxScene == null)
		{
			GD.PushError($"{Name} cannot open hit window without a HitBoxScene.");
			CancelAttack();
			return;
		}

		Node3D attachParent = _currentAttack.AttachHitBoxToWeapon ? FindWeaponInstance() : _actor;
		attachParent ??= _actor;

		GameDebug.Combat($"{_actor.Name} opening hit window under: {attachParent.Name}");

		_activeHitBox = HitBoxScene.Instantiate<HitBoxComponent>();
		attachParent.AddChild(_activeHitBox);

		_activeHitBox.CollisionMask = _targetMask;
		_activeHitBox.Monitoring = true;
		_activeHitBox.HitDetected += OnHitDetected;

		var collisionShape = new CollisionShape3D();
		var boxShape = new BoxShape3D();
		boxShape.Size = _currentAttack.HitBoxSize;
		collisionShape.Shape = boxShape;
		collisionShape.Position = _currentAttack.HitBoxOffset;
		_activeHitBox.AddChild(collisionShape);

		if (DebugDrawEnabled || GlobalDebugDrawEnabled)
		{
			var debugMeshInstance = new MeshInstance3D();
			debugMeshInstance.AddToGroup("debug_mesh");
			var boxMesh = new BoxMesh();
			boxMesh.Size = _currentAttack.HitBoxSize;
			
			var material = new StandardMaterial3D();
			material.AlbedoColor = new Color(1.0f, 0.2f, 0.0f, 0.35f);
			material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			
			debugMeshInstance.Mesh = boxMesh;
			debugMeshInstance.MaterialOverride = material;
			debugMeshInstance.Position = _currentAttack.HitBoxOffset;
			_activeHitBox.AddChild(debugMeshInstance);
		}

		_hitWindowOpen = true;
	}

	private void EndHitWindow()
	{
		if (!_hitWindowOpen) return;

		GameDebug.Combat($"{_actor.Name} closing hit window");

		if (GodotObject.IsInstanceValid(_activeHitBox))
		{
			_activeHitBox.Monitoring = false;
			_activeHitBox.HitDetected -= OnHitDetected;
			_activeHitBox.QueueFree();
		}

		_activeHitBox = null;
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

			GameDebug.Combat($"{_actor.Name} hits {target.Name} (Owner: {damageOwner.Name}) with {_currentAttack.AnimationId}!");

			var direction = -_actor.GlobalTransform.Basis.Z;
			var damage = (float)GD.RandRange(_currentMinDamage, _currentMaxDamage);
			if (GD.Randf() < _currentCritChance)
			{
				damage *= 2;
				GameDebug.Combat("Critical hit!");
			}

			damageable.TakeDamage(_currentAccuracy, damage, direction, _actor);
		}
	}

	private void SpawnProjectile()
	{
		if (_currentAttack == null) return;

		PackedScene scene = _currentAttack.ProjectileScene ?? DefaultProjectileScene;
		if (scene == null)
		{
			GD.PushError($"{Name} cannot spawn projectile without a projectile scene.");
			return;
		}

		Vector3 origin = GetProjectileOrigin();
		Vector3 aimedDirection = Aim(origin, _currentAttack.Range, _currentAttack.AimingAngle);
		SpawnEffect(_currentAttack.CastEffectScene, _actor.GlobalPosition, GetActorForward());
		SpawnEffect(_currentAttack.MuzzleEffectScene, origin, aimedDirection);

		foreach (Vector3 direction in GetProjectileDirections(aimedDirection))
		{
			Projectile projectile = ScenePool.Spawn<Projectile>(scene, GetTree().CurrentScene);
			projectile.Initialize(
				origin,
				direction,
				_currentAttack.ProjectileSpeed,
				_currentAttack.Range,
				_currentAccuracy,
				_currentMinDamage * _currentAttack.ProjectileDamageScale,
				_currentMaxDamage * _currentAttack.ProjectileDamageScale,
				_currentCritChance,
				_actor);

			GameDebug.Combat($"{_actor.Name} spawned projectile flying {direction}");
		}
	}

	private Vector3 GetProjectileOrigin()
	{
		Node3D muzzle = FindMuzzlePoint();
		if (muzzle != null)
		{
			return muzzle.GlobalPosition;
		}

		return _actor.GlobalPosition - _actor.GlobalTransform.Basis.Z * 1.0f + Vector3.Up * 1.2f;
	}

	private IEnumerable<Vector3> GetProjectileDirections(Vector3 aimedDirection)
	{
		int count = Mathf.Max(1, _currentAttack.ProjectileCount);

		if (_currentAttack.ProjectilePattern == ProjectilePattern.Radial)
		{
			float step = 360.0f / count;
			for (int i = 0; i < count; i++)
			{
				yield return RotateHorizontal(GetActorForward(), step * i);
			}
			yield break;
		}

		if (_currentAttack.ProjectilePattern == ProjectilePattern.Spread && count > 1)
		{
			float startAngle = -_currentAttack.SpreadAngle * 0.5f;
			float step = _currentAttack.SpreadAngle / (count - 1);
			for (int i = 0; i < count; i++)
			{
				yield return RotateHorizontal(aimedDirection, startAngle + step * i);
			}
			yield break;
		}

		yield return aimedDirection;
	}

	private Vector3 RotateHorizontal(Vector3 direction, float degrees)
	{
		return direction.Rotated(Vector3.Up, Mathf.DegToRad(degrees)).Normalized();
	}

	private void SpawnEffect(PackedScene effectScene, Vector3 origin, Vector3 direction)
	{
		if (effectScene == null) return;

		Node3D effect = ScenePool.Spawn<Node3D>(effectScene, GetTree().CurrentScene);
		effect.GlobalPosition = origin;
		if (direction.LengthSquared() > 0)
		{
			effect.LookAt(origin + direction, Vector3.Up);
		}
	}

	private Node3D FindWeaponInstance()
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

	private Node3D FindMuzzlePoint()
	{
		Node3D weapon = FindWeaponInstance();
		if (weapon != null)
		{
			var muzzle = weapon.FindChild("Muzzle") as Node3D ?? weapon.FindChild("MuzzlePoint") as Node3D;
			if (muzzle != null) return muzzle;
		}
		return null;
	}

	private Vector3 Aim(Vector3 origin, float range, float aimingAngle)
	{
		bool isPlayer = _actor.IsInGroup("player");
		string targetGroup = isPlayer ? "enemy" : "player";

		var targets = GetTree().GetNodesInGroup(targetGroup).OfType<Node3D>()
			.Where(target => IsValidAimTarget(target) && TestLineOfSight(target, range, aimingAngle))
			.OrderBy(GetAimAngle)
			.ThenBy(target => _actor.GlobalPosition.DistanceTo(target.GlobalPosition));

		foreach (var target in targets)
		{
			return GetDirectionToTarget(origin, target, 1.0f);
		}

		if (isPlayer)
		{
			var damageables = GetTree().GetNodesInGroup("damageable").OfType<Node3D>()
				.Where(node => node is IDamageable
					&& !IsOwnedByActor(node)
					&& TestLineOfSight(node, range, aimingAngle))
				.OrderBy(GetAimAngle)
				.ThenBy(node => _actor.GlobalPosition.DistanceTo(node.GlobalPosition));

			foreach (var node in damageables)
			{
				return GetDirectionToTarget(origin, node, 0.5f);
			}
		}

		return GetActorForward();
	}

	private Vector3 GetDirectionToTarget(Vector3 origin, Node3D target, float fallbackHeight)
	{
		Vector3 direction = GetTargetCenter(target, fallbackHeight) - origin;
		direction.Y = 0;
		return direction.LengthSquared() > 0 ? direction.Normalized() : GetActorForward();
	}

	private Vector3 GetTargetCenter(Node3D target, float fallbackHeight)
	{
		var collisionShape = target.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
		return collisionShape?.GlobalPosition ?? target.GlobalPosition + Vector3.Up * fallbackHeight;
	}

	private bool IsOwnedByActor(Node node)
	{
		return node == _actor || _actor.IsAncestorOf(node) || node.IsAncestorOf(_actor);
	}

	private bool IsValidAimTarget(Node node)
	{
		if (IsOwnedByActor(node)) return false;
		if (node is EnemyBase enemy) return !enemy.IsDead;
		if (node is Player player) return !player.IsDead;
		if (node.GetParent() is EnemyBase parentEnemy) return !parentEnemy.IsDead;
		if (node.GetParent() is Player parentPlayer) return !parentPlayer.IsDead;
		return true;
	}

	private float GetAimAngle(Node3D target)
	{
		Vector3 directionToTarget = target.GlobalPosition - _actor.GlobalPosition;
		directionToTarget.Y = 0;
		if (directionToTarget.LengthSquared() == 0) return 0.0f;

		return Mathf.RadToDeg(GetActorForward().AngleTo(directionToTarget.Normalized()));
	}

	private Vector3 GetActorForward()
	{
		Vector3 forward = -_actor.GlobalTransform.Basis.Z;
		forward.Y = 0;
		return forward.LengthSquared() > 0 ? forward.Normalized() : Vector3.Forward;
	}

	private bool TestLineOfSight(Node3D target, float range, float aimingAngle)
	{
		if (!IsValidAimTarget(target)) return false;

		float distance = _actor.GlobalPosition.DistanceTo(target.GlobalPosition);
		if (distance > range) return false;

		Vector3 directionToTarget = target.GlobalPosition - _actor.GlobalPosition;
		directionToTarget.Y = 0;
		if (directionToTarget.LengthSquared() == 0) return true;
		directionToTarget = directionToTarget.Normalized();

		float angle = Mathf.RadToDeg(GetActorForward().AngleTo(directionToTarget));
		if (angle > aimingAngle) return false;

		return true;
	}
}
