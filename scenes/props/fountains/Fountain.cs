using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;

/// <summary>
/// A fountain whose liquid grants its <see cref="Blessing"/> once. Drinking drains it for good: the liquid stops
/// glowing and its pool effects fade. Generation gives each fountain a <c>persistent_id</c>, and a drained fountain is
/// one of the run's cleared entities, so it stays drained across revisits and save/load without its own save data.
/// </summary>
public partial class Fountain : Node3D
{
	private static readonly StringName PersistentIdMeta = "persistent_id";

	private bool _isDrained;
	private Level _level;
	private Tween _drainTween;
	// Each liquid surface with its imported material and the private copy that dims when drained.
	private readonly List<(MeshInstance3D Mesh, int Surface, BaseMaterial3D Original, BaseMaterial3D Drained)> _liquid = [];

	[Export] public InteractiveComponent Interactive { get; set; }
	[Export] public FountainBlessing Blessing { get; set; }
	/// <summary>The fountain art. Its emissive surfaces are the liquid: Studio fountains emit nowhere else.</summary>
	[Export] public Node3D Model { get; set; }
	/// <summary>Pool effects that fade out when the fountain is drained.</summary>
	[Export] public Array<FountainSparkles> Pools { get; set; } = [];
	/// <summary>Seconds of drinking; long enough for most of the drinker's use-item gesture.</summary>
	[Export(PropertyHint.Range, "0,5,0.05")] public float DrinkDuration { get; set; } = 1.5f;
	/// <summary>Action id the drinker's animation tree plays while drinking; the potion gesture by default.</summary>
	[Export] public string DrinkAnimationId { get; set; } = "drink_potion";
	/// <summary>Horizontal reach from the fountain's center; wide basins need more than a chest.</summary>
	[Export(PropertyHint.Range, "0.5,5,0.1")] public float InteractionRange { get; set; } = 2.6f;
	/// <summary>Albedo multiplier for drained liquid: still water without its magic.</summary>
	[Export] public Color DrainedTint { get; set; } = new(0.32f, 0.32f, 0.36f);
	[Export(PropertyHint.Range, "0,5,0.05")] public float DrainDuration { get; set; } = 1.5f;

	/// <summary>Whether the fountain has been drunk from. Setting it shows the state at once; drinking animates it.</summary>
	[Export]
	public bool IsDrained
	{
		get => _isDrained;
		set
		{
			_isDrained = value;
			if (IsNodeReady()) ShowDrained(animate: false);
		}
	}

	private string SourceId => GetMeta(PersistentIdMeta, "").AsString();

	public override void _EnterTree() => _level = this.GetAncestorOrNull<Level>();

	public override void _Ready()
	{
		if (Interactive == null || Blessing == null || Model == null)
		{
			GD.PushError($"{Name}: a fountain requires assigned Interactive, Blessing and Model.");
			return;
		}

		Interactive.OfferProvider = GetOffer;
		Interactive.CommitHandler = OnCommit;
		foreach (var mesh in Model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
		{
			for (int surface = 0; surface < mesh.GetSurfaceOverrideMaterialCount(); surface++)
			{
				if (mesh.GetActiveMaterial(surface) is BaseMaterial3D { EmissionEnabled: true } liquid)
					_liquid.Add((mesh, surface, liquid, (BaseMaterial3D)liquid.Duplicate()));
			}
		}

		if (!string.IsNullOrEmpty(SourceId) && _level?.Run?.IsCleared(SourceId) == true) _isDrained = true;
		ShowDrained(animate: false);
	}

	private InteractionOffer GetOffer(ActorInteractionContext actor)
	{
		if (_isDrained) return InteractionOffer.None;
		bool helps = actor.Actor is Player drinker && Blessing.CanBenefit(drinker);
		return new InteractionOffer(Interactive, "drink_fountain", Blessing.DrinkLabel,
			DrinkDuration, InteractionRange, InteractionPermission.None, Interactive.StateRevision, IsBlocked: !helps,
			AnimationId: DrinkAnimationId);
	}

	private InteractionCommitResult OnCommit(InteractionReservation reservation, ActorInteractionContext actor)
	{
		if (_isDrained || actor.Actor is not Player drinker || !Blessing.CanBenefit(drinker))
			return InteractionCommitResult.TargetInvalidated;

		Blessing.Grant(drinker);
		_isDrained = true;
		if (!string.IsNullOrEmpty(SourceId)) _level?.Run?.MarkCleared(SourceId);
		if (Blessing.DrinkEffect != null)
			drinker.WorldEffects?.PlayBurst(Blessing.DrinkEffect, (uint)GenerationRandom.DeriveSeed(_level?.Seed ?? 0, GenerationRandom.Cosmetic, SourceId));
		ShowDrained(animate: true);
		return InteractionCommitResult.Success;
	}

	private void ShowDrained(bool animate)
	{
		if (Interactive != null) Interactive.IsInteractive = !_isDrained;
		foreach (var pool in Pools.Where(GodotObject.IsInstanceValid))
		{
			if (_isDrained && animate) pool.FadeOut();
			else pool.Flowing = !_isDrained;
		}

		_drainTween?.Kill();
		_drainTween = _isDrained && animate && _liquid.Count > 0 ? CreateTween().SetParallel() : null;
		foreach (var (mesh, surface, original, drained) in _liquid)
		{
			// The private copy dims, so the shared imported material stays untouched.
			mesh.SetSurfaceOverrideMaterial(surface, _isDrained ? drained : null);
			drained.EmissionEnergyMultiplier = _isDrained && !animate ? 0 : original.EmissionEnergyMultiplier;
			drained.AlbedoColor = _isDrained && !animate ? DrainedTint : original.AlbedoColor;
			_drainTween?.TweenProperty(drained, (string)BaseMaterial3D.PropertyName.EmissionEnergyMultiplier, 0f, DrainDuration);
			_drainTween?.TweenProperty(drained, (string)BaseMaterial3D.PropertyName.AlbedoColor, DrainedTint, DrainDuration);
		}
	}
}