using Godot;
using System.Collections.Generic;

public partial class Projectile : Node3D, IPooledNode
{
	private static readonly StringName PositionKey = "position";
	private static readonly StringName NormalKey = "normal";
	private static readonly StringName ColliderKey = "collider";

	private const uint WorldLayer = 1 << 0;
	private const uint WallsLayer = 1 << 1;
	private const uint PlayerLayer = 1 << 2;
	private const uint EnemiesLayer = 1 << 3;
	private const uint PropsLayer = 1 << 4;
	private const uint BlockingCollisionMask = WorldLayer | WallsLayer | PropsLayer;

	[Export] public float Speed { get; set; } = 10f;
	[Export] public float Range { get; set; } = 30f;
	[Export] public Vector3 Direction { get; set; } = Vector3.Forward;
	[Export] public float MinDamage { get; set; }
	[Export] public float MaxDamage { get; set; }
	[Export] public float CritChance { get; set; }
	[Export] public PackedScene ImpactEffectScene { get; set; }
	[Export] public PackedScene ExpireEffectScene { get; set; }
	[Export] public bool SpawnImpactEffectOnExpire { get; set; } = true;
	/// <summary>Optional per-cast owner for impacts that must end with their source.</summary>
	public Node EffectRoot { get; set; }
	public DamageElement Element { get; set; }
	[Export(PropertyHint.Layers3DPhysics)] public uint WorldCollisionMask { get; set; } = BlockingCollisionMask;
	[Export] public float ImpactRadius { get; set; } = 0.0f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float ImpactRadiusMinDamageScale { get; set; } = 0.2f;
	/// <summary>
	/// Depth of the damage lane hanging below the flight line. Only world geometry crossing the flight line blocks a
	/// shot, so it clears low furniture, yet it still hits any actor it passes over, however short. Zero limits contact
	/// to the hitbox, which suits drops whose flight line is vertical.
	/// </summary>
	[Export] public float GroundReach { get; set; } = 1.5f;
	/// <summary>Half-width of the damage lane across the flight direction.</summary>
	[Export] public float GroundReachRadius { get; set; } = 0.25f;

	private HitBoxComponent _hitBoxComponent;
	private float _distanceTravelled;
	private AttackIdentity _executionId;
	private AttackIdentity _parentExecution;
	private AttackDefenseTraits _defenseTraits = AttackDefenseTraits.Dodgeable | AttackDefenseTraits.Guardable;
	private AttackDefenseTraits _areaDefenseTraits;
	/// <summary>The releasing attack's impact, set by its executor before flight; unset projectiles use the default.</summary>
	public AttackImpact Impact { get; set; } = AttackImpact.Default;
	private readonly RandomNumberGenerator _random = new();
	public AttackIdentity ExecutionId => _executionId;
	private static ulong _projectileSequence = 0;
	private Node _attacker;
	private DamageSourceFlags _attackerFaction = DamageSourceFlags.Environment;
	private bool _hasResolvedHit;
	private bool _restrictedTarget;
	private Node _assignedTarget;
	private float _effectScale = 1f;
	private ProjectileFlight _flight = ProjectileFlight.Straight;
	private Node3D _homingTarget;
	private float _flightTime;

	/// <summary>Whether this flight still steers toward its captured target.</summary>
	public bool IsHoming => _homingTarget != null;

	/// <summary>Restricts this flight to one damage owner; null produces a cosmetic-only projectile.</summary>
	public void RestrictToTarget(Node target, float effectScale = 1f)
	{
		_restrictedTarget = true;
		_assignedTarget = target;
		_effectScale = effectScale;
		ImpactRadius = 0;
	}
	private int _returnVersion;
	private uint _damageCollisionMask;
	private float _defaultImpactRadius;
	private float _defaultImpactRadiusMinDamageScale;
	private readonly PhysicsRayQueryParameters3D _worldRayQuery = new()
	{
		CollisionMask = BlockingCollisionMask,
		HitFromInside = false,
	};
	private readonly SphereShape3D _impactShape = new();
	private readonly PhysicsShapeQueryParameters3D _impactQuery = new()
	{
		CollideWithBodies = true,
		CollideWithAreas = true,
	};
	private readonly HashSet<Node> _damagedImpactOwners = [];
	private readonly BoxShape3D _laneShape = new();
	private readonly PhysicsShapeQueryParameters3D _laneQuery = new()
	{
		CollideWithBodies = true,
		CollideWithAreas = true,
	};

	public Node Attacker => GetValidAttacker();
	public DamageSourceFlags AttackerFaction => _attackerFaction;

	/// <summary>Stop a source-owned projectile immediately without generating an impact.</summary>
	public void Cancel()
	{
		_hasResolvedHit = true;
		SetPhysicsProcess(false);
		SetHitBoxMonitoring(false, deferred: true);
	}

	public override void _Ready()
	{
		ProcessPhysicsPriority = CombatPhysics.Damage;
		AddToGroup("projectile");
		_defaultImpactRadius = ImpactRadius;
		_defaultImpactRadiusMinDamageScale = ImpactRadiusMinDamageScale;
		_impactQuery.Shape = _impactShape;
		_hitBoxComponent = GetNode<HitBoxComponent>("%HitBoxComponent");
		// The hitbox is our own child, so the connection lives as long as we do.
		_hitBoxComponent.HitDetected += OnHitDetected;
		ResetRuntimeState();
	}

	public void OnSpawnedFromPool()
	{
		ResetRuntimeState();
	}

	public void OnDespawnedToPool()
	{
		SetPhysicsProcess(false);
		_attacker = null;
		_homingTarget = null;
		EffectRoot = null;
		Element = DamageElement.None;
		_parentExecution = default;
		_attackerFaction = DamageSourceFlags.Environment;
		SetHitBoxMonitoring(false, deferred: true);
	}

	public void Initialize(Vector3 origin, Vector3 direction, float speed, float range, float minDamage, float maxDamage, float critChance, Node attacker = null, AttackDefenseTraits defenseTraits = AttackDefenseTraits.Dodgeable | AttackDefenseTraits.Guardable, AttackDefenseTraits areaDefenseTraits = AttackDefenseTraits.Dodgeable, ulong randomSeed = 42, AttackIdentity parentExecution = default)
	{
		_parentExecution = parentExecution;
		_areaDefenseTraits = areaDefenseTraits;
		_random.Seed = randomSeed;
		_executionId = new AttackIdentity((ulong)GetInstanceId(), ++_projectileSequence);
		_defenseTraits = defenseTraits;
		Direction = direction.Normalized();
		Speed = speed;
		Range = range;
		MinDamage = minDamage;
		MaxDamage = maxDamage;
		CritChance = critChance;
		_attacker = attacker;
		_attackerFaction = HurtBoxComponent.GetDamageSource(attacker);
		_distanceTravelled = 0.0f;
		_hasResolvedHit = false;
		ResetFlight();
		if (CombatTrace.Enabled) CombatTrace.Write(this, "projectile.spawned", ("execution", _executionId.Sequence), ("execution_source", _executionId.SourceInstanceId),
			("parent_execution", _parentExecution.Sequence), ("parent_source", _parentExecution.SourceInstanceId), ("owner", CombatTrace.Identity(attacker)),
			("origin", CombatTrace.Vector(origin)), ("direction", CombatTrace.Vector(Direction)), ("speed", speed), ("range", range),
			("traits", defenseTraits.ToString()), ("area_traits", areaDefenseTraits.ToString()), ("seed", randomSeed));
		SetPhysicsProcess(true);

		GlobalTransform = new Transform3D(Basis.Identity, origin);
		if (IsInsideTree() && Direction.LengthSquared() > 0)
		{
			LookAt(origin + Direction, GetLookAtUpVector(Direction));
		}
		ResetPhysicsInterpolation();

		if (_hitBoxComponent != null)
		{
			_damageCollisionMask = GetDamageCollisionMask();
			_hitBoxComponent.CollisionMask = _damageCollisionMask;
			SetHitBoxMonitoring(true);
		}
	}

	/// <summary>
	/// Shape the flight started by <see cref="Initialize"/>: its launch ramp, and horizontal homing on
	/// <paramref name="homingTarget"/>. A null target or zero turn rate flies straight. The attack validated the values.
	/// </summary>
	public void ConfigureFlight(in ProjectileFlight flight, Node3D homingTarget)
	{
		_flight = flight;
		_homingTarget = flight.Homes ? homingTarget : null;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_hasResolvedHit) return;

		_flightTime += (float)delta;
		if (_homingTarget != null) Steer((float)delta);
		Vector3 start = GlobalPosition;
		Vector3 movement = Direction * Speed * LaunchSpeedFactor() * (float)delta;
		Vector3 end = start + movement;
		bool hitWorld = TryHitWorld(start, end, out Vector3 hitPosition, out Vector3 hitNormal);
		Vector3 reached = hitWorld ? hitPosition : end;
		GlobalPosition = reached;

		// Actors under this step's flight line are reached before the wall that ends it.
		SweepGroundReach(start, reached);
		if (_hasResolvedHit) return;

		if (hitWorld)
		{
			if (CombatTrace.Enabled) CombatTrace.Write(this, "projectile.world_impact", ("execution", _executionId.Sequence), ("position", CombatTrace.Vector(hitPosition)), ("radius", ImpactRadius));
			StickThenFree(hitNormal);
			return;
		}

		_distanceTravelled += movement.Length();

		if (_distanceTravelled >= Range)
		{
			if (CombatTrace.Enabled) CombatTrace.Write(this, "projectile.expired", ("execution", _executionId.Sequence));
			GameDebug.Combat("Projectile reached max range");
			SpawnExpireEffect();
			CallDeferred(MethodName.ReturnToPoolOrFree);
		}
	}

	/// <summary>
	/// After the homing delay, turn horizontally toward the target's current position by at most the turn rate.
	/// A freed or dead target, or one more than 90 degrees off the nose (overshot or dodged past), ends homing for
	/// good, so a missed projectile flies on instead of circling.
	/// </summary>
	private void Steer(float delta)
	{
		if (_flightTime < _flight.HomingDelay) return;
		if (!AttackController.IsLivingTarget(_homingTarget))
		{
			LoseHoming("target_lost");
			return;
		}
		Vector3 toTarget = _homingTarget.GlobalPosition - GlobalPosition;
		toTarget.Y = 0;
		var heading = new Vector3(Direction.X, 0, Direction.Z);
		if (toTarget.LengthSquared() < 0.0001f || heading.LengthSquared() < 0.0001f) return;
		float angle = heading.SignedAngleTo(toTarget, Vector3.Up);
		if (Mathf.Abs(angle) > Mathf.Pi * 0.5f)
		{
			LoseHoming("overshot");
			return;
		}
		float maxTurn = Mathf.DegToRad(_flight.TurnRate) * delta;
		Direction = Direction.Rotated(Vector3.Up, Mathf.Clamp(angle, -maxTurn, maxTurn)).Normalized();
		LookAt(GlobalPosition + Direction, GetLookAtUpVector(Direction));
	}

	private void LoseHoming(string reason)
	{
		_homingTarget = null;
		if (CombatTrace.Enabled) CombatTrace.Write(this, "projectile.homing_lost", ("execution", _executionId.Sequence), ("reason", reason));
	}

	/// <summary>Fraction of full speed during the launch ramp.</summary>
	private float LaunchSpeedFactor() => _flight.RampDuration > 0
		? Mathf.Lerp(_flight.LaunchSpeedScale, 1, Mathf.Min(1, _flightTime / _flight.RampDuration))
		: 1;

	private void ResetFlight()
	{
		_flight = ProjectileFlight.Straight;
		_homingTarget = null;
		_flightTime = 0;
	}

	private void OnHitDetected(Node body)
	{
		if (_hasResolvedHit) return;

		GameDebug.Combat($"Projectile hit {body.Name}");
		if (TryGetDamageTarget(body, out Node damageOwner, out IDamageable damageable))
		{
			if (_restrictedTarget && (!GodotObject.IsInstanceValid(_assignedTarget) || damageOwner != _assignedTarget)) return;
			var damage = (float)_random.RandfRange(MinDamage, MaxDamage);
			float baseDamage = damage;
			bool critical = _random.Randf() < CritChance;
			if (critical)
			{
				damage *= 2;
				GameDebug.Combat("Critical hit!");
			}

			if (CombatTrace.Enabled) CombatTrace.Write(this, "projectile.contact", ("execution", _executionId.Sequence), ("target", CombatTrace.Identity(damageOwner)), ("base_damage", baseDamage), ("critical", critical), ("raw_damage", damage));
			var context = new DamageContext
			{
				ExecutionId = _executionId,
				SourceNode = this,
				OwnerNode = GetValidAttacker(),
				Faction = _attackerFaction,
				Amount = damage,
				Random = _random,
				IncomingDirection = Direction,
				DefenseTraits = _defenseTraits,
				DeliveryKind = DamageDeliveryKind.Projectile,
				Element = Element,
				Impact = Impact,
				Critical = critical
			};

			if (damageable is ICombatDamageable combatDamageable)
			{
				combatDamageable.ResolveDamage(in context);
			}
			else
			{
				damageable.TakeDamage(damage, Direction, GetValidAttacker(), _attackerFaction);
			}

			_hasResolvedHit = true;
			SetPhysicsProcess(false);
			SetHitBoxMonitoring(false, deferred: true);

			ApplyImpactRadiusDamage(damageOwner);
			SpawnEffect(ImpactEffectScene);
			CallDeferred(MethodName.ReturnToPoolOrFree);
		}
	}

	private bool TryHitWorld(Vector3 start, Vector3 end, out Vector3 hitPosition, out Vector3 hitNormal)
	{
		PhysicsDirectSpaceState3D space = GetWorld3D()?.DirectSpaceState;
		if (space == null)
		{
			hitPosition = default;
			hitNormal = default;
			return false;
		}

		_worldRayQuery.From = start;
		_worldRayQuery.To = end;
		_worldRayQuery.CollisionMask = WorldCollisionMask;
		_worldRayQuery.HitFromInside = false;

		var result = space.IntersectRay(_worldRayQuery);
		if (result.Count == 0)
		{
			hitPosition = default;
			hitNormal = default;
			return false;
		}

		hitPosition = result[PositionKey].AsVector3();
		hitNormal = result[NormalKey].AsVector3();
		return true;
	}

	/// <summary>
	/// Resolve contact with actors anywhere in the upright lane below one flight step. The query is world-aligned and
	/// swept, so it holds whatever way the projectile visual is turned and cannot step over a short actor.
	/// </summary>
	private void SweepGroundReach(Vector3 start, Vector3 end)
	{
		if (GroundReach <= 0 || _damageCollisionMask == 0) return;
		PhysicsDirectSpaceState3D space = GetWorld3D()?.DirectSpaceState;
		if (space == null) return;

		Vector3 travel = end - start;
		travel.Y = 0;
		_laneShape.Size = new Vector3(GroundReachRadius * 2, GroundReach, travel.Length() + GroundReachRadius * 2);
		_laneQuery.Shape = _laneShape;
		_laneQuery.Transform = new Transform3D(new Basis(Vector3.Up, Mathf.Atan2(travel.X, travel.Z)),
			(start + end) * 0.5f + Vector3.Down * (GroundReach * 0.5f));
		_laneQuery.CollisionMask = _damageCollisionMask;
		foreach (Godot.Collections.Dictionary result in space.IntersectShape(_laneQuery, 8))
		{
			OnHitDetected(result[ColliderKey].As<Node>());
			if (_hasResolvedHit) return;
		}
	}

	private uint GetDamageCollisionMask()
	{
		if (_attackerFaction == DamageSourceFlags.Player)
		{
			return EnemiesLayer;
		}

		if (_attackerFaction == DamageSourceFlags.Enemy || _attackerFaction == DamageSourceFlags.Boss)
		{
			return PlayerLayer;
		}

		if (_attackerFaction == DamageSourceFlags.None)
		{
			return 0;
		}

		return PlayerLayer | EnemiesLayer;
	}

	private void StickThenFree(Vector3 hitNormal)
	{
		_hasResolvedHit = true;
		SetPhysicsProcess(false);
		SetHitBoxMonitoring(false, deferred: true);

		ApplyImpactRadiusDamage();
		SpawnSurfaceAlignedEffect(ImpactEffectScene, hitNormal);
		int returnVersion = _returnVersion;
		GetTree().CreateTimer(0.3f).Timeout += () => ReturnToPoolOrFree(returnVersion);
	}

	private void ApplyImpactRadiusDamage(Node excludedDamageOwner = null)
	{
		if (ImpactRadius <= 0.0f)
		{
			return;
		}

		PhysicsDirectSpaceState3D space = GetWorld3D()?.DirectSpaceState;
		if (space == null)
		{
			return;
		}

		_impactShape.Radius = ImpactRadius;
		_impactQuery.Shape = _impactShape;
		_impactQuery.Transform = new Transform3D(Basis.Identity, GlobalPosition + Vector3.Up * 0.6f);
		_impactQuery.CollisionMask = _damageCollisionMask == 0 ? GetDamageCollisionMask() : _damageCollisionMask;
		_impactQuery.CollideWithBodies = true;
		_impactQuery.CollideWithAreas = true;
		_damagedImpactOwners.Clear();
		if (excludedDamageOwner != null)
		{
			_damagedImpactOwners.Add(excludedDamageOwner);
		}

		try
		{
			foreach (Godot.Collections.Dictionary result in space.IntersectShape(_impactQuery, 32))
			{
				Node collider = result[ColliderKey].As<Node>();
				if (!TryGetDamageTarget(collider, out Node damageOwner, out IDamageable damageable))
				{
					continue;
				}

				if (!_damagedImpactOwners.Add(damageOwner))
				{
					continue;
				}

				float damageScale = GetImpactRadiusDamageScale(damageOwner);
				float damage = (float)_random.RandfRange(MinDamage, MaxDamage) * damageScale;
				bool critical = _random.Randf() < CritChance;
				if (critical)
				{
					damage *= 2;
					GameDebug.Combat("Critical impact radius hit!");
				}

				Vector3 pushDirection = damageOwner is Node3D node3D
					? (node3D.GlobalPosition - GlobalPosition).Normalized()
					: Direction;
				if (pushDirection.LengthSquared() < 0.001f)
				{
					pushDirection = Direction;
				}

				var areaContext = new DamageContext
				{
					ExecutionId = _executionId,
					SourceNode = this,
					OwnerNode = GetValidAttacker(),
					Faction = _attackerFaction,
					Amount = damage,
				Random = _random,
					IncomingDirection = pushDirection,
					DefenseTraits = _areaDefenseTraits,
					DeliveryKind = DamageDeliveryKind.AreaImpact,
					Element = Element,
					Impact = Impact with { PoiseDamage = Impact.PoiseDamage * damageScale },
					Critical = critical
				};

				if (damageable is ICombatDamageable areaCombatDamageable)
				{
					areaCombatDamageable.ResolveDamage(in areaContext);
				}
				else
				{
					damageable.TakeDamage(damage, pushDirection, GetValidAttacker(), _attackerFaction);
				}
			}
		}
		finally
		{
			_damagedImpactOwners.Clear();
		}
	}

	private bool TryGetDamageTarget(Node collider, out Node damageOwner, out IDamageable damageable)
	{
		damageOwner = null;
		damageable = null;
		if (collider == null)
		{
			return false;
		}

		if (collider is HurtBoxComponent hurtBox)
		{
			damageOwner = hurtBox.GetParent() ?? hurtBox;
			damageable = hurtBox;
			return true;
		}

		// Detection areas (interaction, pickup, stairs) are not combat surfaces, even on a damageable actor.
		if (collider is Area3D && collider is not IDamageable) return false;

		HurtBoxComponent childHurtBox = collider.GetNodeOrNull<HurtBoxComponent>("%HurtBoxComponent")
			?? collider.GetNodeOrNull<HurtBoxComponent>("HurtBoxComponent");
		if (childHurtBox != null)
		{
			damageOwner = collider;
			damageable = childHurtBox;
			return true;
		}

		if (collider is IDamageable directDamageable)
		{
			damageOwner = collider;
			damageable = directDamageable;
			return true;
		}

		Node parent = collider.GetParent();
		if (parent is IDamageable parentDamageable)
		{
			damageOwner = parent;
			damageable = parentDamageable;
			return true;
		}

		return false;
	}

	private float GetImpactRadiusDamageScale(Node damageOwner)
	{
		if (ImpactRadius <= 0.0f || damageOwner is not Node3D node3D)
		{
			return 1.0f;
		}

		float distance = node3D.GlobalPosition.DistanceTo(GlobalPosition);
		float t = Mathf.Clamp(distance / ImpactRadius, 0.0f, 1.0f);
		return Mathf.Lerp(Mathf.Clamp(ImpactRadiusMinDamageScale, 0.0f, 1.0f), 1.0f, 1.0f - t);
	}

	private void SpawnEffect(PackedScene effectScene)
	{
		if (effectScene == null) return;

		Node3D effect = SpawnImpactVisual(effectScene);
		effect.GlobalPosition = GlobalPosition;
		if (_restrictedTarget) effect.Scale *= _effectScale;
		if (Direction.LengthSquared() > 0)
		{
			effect.LookAt(GlobalPosition + Direction, GetLookAtUpVector(Direction));
		}
		effect.ResetPhysicsInterpolation();
	}

	private static Vector3 GetLookAtUpVector(Vector3 direction)
	{
		return Mathf.Abs(direction.Normalized().Dot(Vector3.Up)) > 0.95f ? Vector3.Forward : Vector3.Up;
	}

	private void SpawnSurfaceAlignedEffect(PackedScene effectScene, Vector3 surfaceNormal)
	{
		if (effectScene == null) return;

		if (surfaceNormal.LengthSquared() < 0.001f)
		{
			SpawnEffect(effectScene);
			return;
		}

		Vector3 normal = surfaceNormal.Normalized();
		Vector3 forward = Direction - normal * Direction.Dot(normal);
		if (forward.LengthSquared() < 0.001f)
		{
			forward = Mathf.Abs(normal.Dot(Vector3.Forward)) > 0.95f ? Vector3.Right : Vector3.Forward;
			forward = (forward - normal * forward.Dot(normal)).Normalized();
		}
		else
		{
			forward = forward.Normalized();
		}

		Vector3 right = forward.Cross(normal).Normalized();
		forward = normal.Cross(right).Normalized();

		Node3D effect = SpawnImpactVisual(effectScene);
		effect.GlobalTransform = new Transform3D(new Basis(right, forward, normal).Scaled(Vector3.One * _effectScale), GlobalPosition + normal * 0.03f);
		effect.ResetPhysicsInterpolation();
	}

	private Node3D SpawnImpactVisual(PackedScene scene)
	{
		Node parent = GodotObject.IsInstanceValid(EffectRoot) && EffectRoot.IsInsideTree() ? EffectRoot : GetTree().CurrentScene ?? GetParent();
		if (!_restrictedTarget) return ScenePool.Spawn<Node3D>(scene, parent);
		// Scaled spell impacts must not leak their transform into the shared effect pool.
		Node3D effect = scene.Instantiate<Node3D>();
		parent.AddChild(effect);
		return effect;
	}

	private void SpawnExpireEffect()
	{
		if (ExpireEffectScene != null)
		{
			SpawnEffect(ExpireEffectScene);
			return;
		}

		if (SpawnImpactEffectOnExpire)
		{
			SpawnEffect(ImpactEffectScene);
		}
	}

	private void ResetRuntimeState()
	{
		_distanceTravelled = 0.0f;
		_hasResolvedHit = false;
		ResetFlight();
		_restrictedTarget = false;
		_assignedTarget = null;
		_effectScale = 1f;
		EffectRoot = null;
		Element = DamageElement.None;
		_returnVersion++;
		ImpactRadius = _defaultImpactRadius;
		ImpactRadiusMinDamageScale = _defaultImpactRadiusMinDamageScale;
		Impact = AttackImpact.Default;
		SetPhysicsProcess(false);
		if (_hitBoxComponent != null)
		{
			SetHitBoxMonitoring(false);
			_damageCollisionMask = GetDamageCollisionMask();
			_hitBoxComponent.CollisionMask = _damageCollisionMask;
		}
	}

	private Node GetValidAttacker()
	{
		return _attacker != null && GodotObject.IsInstanceValid(_attacker) ? _attacker : null;
	}

	private void SetHitBoxMonitoring(bool enabled, bool deferred = false)
	{
		if (_hitBoxComponent == null)
		{
			return;
		}

		if (deferred)
		{
			_hitBoxComponent.SetDeferred(Area3D.PropertyName.Monitoring, enabled);
			return;
		}

		_hitBoxComponent.Monitoring = enabled;
	}

	private void ReturnToPoolOrFree()
	{
		if (ScenePool.IsTracked(this))
		{
			ScenePool.Despawn(this);
			return;
		}

		QueueFree();
	}

	private void ReturnToPoolOrFree(int returnVersion)
	{
		if (returnVersion != _returnVersion)
		{
			return;
		}

		ReturnToPoolOrFree();
	}
}
