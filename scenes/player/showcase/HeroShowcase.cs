using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// A hero on display: a class's model wearing whatever an inventory has equipped, idling and every so often showing
/// off what it carries: an attack with the held weapon, a guard with the shield, a cheer. Equipping something shows
/// at once and plays that item's flourish. Given <see cref="Poses"/>, a hero that is only waiting strikes one of those
/// at random instead: nobody swings a weapon at nothing while standing at the dungeon's door. It is a model and its animations, never a <see cref="Player"/>: nothing
/// here fights, moves or takes input, so menus can show it freely.
/// </summary>
[GlobalClass]
public partial class HeroShowcase : Node3D
{
	/// <summary>Dresses the model from the inventory, with the game's own grips and mounts.</summary>
	[Export] public PackedScene AttachmentManagerScene { get; set; }
	/// <summary>Maps a weapon's attack style to the rig's clip, as the player's attacks do.</summary>
	[Export] public AttackAnimationProfile Animations { get; set; }
	/// <summary>Seconds of idling between flourishes.</summary>
	[Export(PropertyHint.Range, "1,30,0.5,suffix:s")] public float FlourishInterval { get; set; } = 4.5f;
	[Export(PropertyHint.Range, "0,1,0.05,suffix:s")] public float BlendSeconds { get; set; } = 0.2f;
	/// <summary>The clip for a held shield.</summary>
	[Export] public StringName GuardClip { get; set; } = "Block";
	/// <summary>The clip for worn armor, and for a hero with nothing to show off.</summary>
	[Export] public StringName CheerClip { get; set; } = "Cheer";
	/// <summary>
	/// What an idle hero does every <see cref="FlourishInterval"/>, one picked at random and never the same twice
	/// running. Empty, it shows off its gear in turn instead. Newly equipped gear is shown off either way.
	/// </summary>
	[Export] public ShowcasePose[] Poses { get; set; } = [];

	/// <summary>The inventory the hero wears; null until a hero is shown.</summary>
	public Inventory Inventory { get; private set; }
	/// <summary>The clip playing now: the idle, or the current flourish.</summary>
	public StringName CurrentClip => _animation?.CurrentAnimation ?? "";

	private CharacterClass _class;
	private Node3D _model;
	private AnimationPlayer _animation;
	private Action _unsubscribe = () => { };
	private double _untilFlourish;
	private double _flourishRemaining;
	private int _nextFlourish;
	private ShowcasePose _lastPose;
	/// <summary>The clips still to play of the pose under way.</summary>
	private readonly Queue<StringName> _poseClips = new();

	/// <summary>
	/// Shows <paramref name="characterClass"/>'s hero wearing what <paramref name="inventory"/> has equipped, and keeps
	/// following that inventory.
	/// </summary>
	public void Show(CharacterClass characterClass, Inventory inventory)
	{
		Clear();
		_class = characterClass;
		Inventory = inventory;

		_model = CharacterModel.Instantiate(characterClass);
		AddChild(_model);
		_animation = CharacterModel.FindAnimationPlayer(_model);
		var attachments = AttachmentManagerScene.Instantiate<BoneAttachmentManager>();
		attachments.Subject = _model;
		attachments.Inventory = inventory;
		attachments.AttachmentNodes = new Godot.Collections.Dictionary<AttachmentType, BoneAttachment3D>(
			CharacterModel.ResolveAttachments(_model, characterClass));
		_model.AddChild(attachments);
		_unsubscribe = this.SubscribeUntilExit(inventory, i => i.ItemEquipped += OnItemEquipped, i => i.ItemEquipped -= OnItemEquipped);

		_nextFlourish = 0;
		PlayIdle();
	}

	/// <summary>
	/// Takes the hero off display: its model goes and it stops following the inventory, so a showcase nobody is
	/// looking at neither animates nor dresses itself as gear changes.
	/// </summary>
	public void Clear()
	{
		_unsubscribe();
		_unsubscribe = () => { };
		_poseClips.Clear();
		if (_model != null)
		{
			RemoveChild(_model);
			_model.QueueFree();
		}
		_model = null;
		_animation = null;
		Inventory = null;
	}

	/// <summary>A class's starting kit with everything wearable worn, as a new run begins.</summary>
	public static Inventory StartingGear(CharacterClass characterClass)
	{
		Inventory inventory = characterClass.CreateStartingInventory(Mathf.Max(1, characterClass.StartingItems.Count));
		inventory.EquipAll();
		return inventory;
	}

	public override void _Process(double delta)
	{
		if (_animation == null)
		{
			return;
		}

		if (_flourishRemaining > 0)
		{
			_flourishRemaining -= delta;
			if (_flourishRemaining <= 0 && !PlayNextPoseClip())
			{
				PlayIdle();
			}
			return;
		}

		_untilFlourish -= delta;
		if (_untilFlourish <= 0 && Poses.Length > 0)
		{
			StrikePose();
		}
		else if (_untilFlourish <= 0)
		{
			// Each flourish in turn, so a hero with a weapon and a shield shows both.
			List<(StringName Clip, float Speed)> flourishes = Flourishes();
			var (clip, speed) = flourishes[_nextFlourish++ % flourishes.Count];
			Flourish(clip, speed);
		}
	}

	/// <summary>Putting something on shows it off at once; jewelry does not show on the body.</summary>
	private void OnItemEquipped(EquipableItem item, EquipmentSlot slot)
	{
		_poseClips.Clear();
		switch (item)
		{
			case Weapon weapon when AttackOf(weapon) is { } attack:
				Flourish(attack.Clip, attack.Speed);
				break;
			case Armor when slot == EquipmentSlot.ShieldHand:
				Flourish(GuardClip, 1);
				break;
			case Armor:
				Flourish(CheerClip, 1);
				break;
		}
	}

	/// <summary>What the hero can show off right now, in the order it does.</summary>
	private List<(StringName Clip, float Speed)> Flourishes()
	{
		var flourishes = new List<(StringName, float)>();
		if (Inventory.EquippedItems[EquipmentSlot.WeaponHand]?.Item is Weapon weapon && AttackOf(weapon) is { } attack)
		{
			flourishes.Add(attack);
		}
		if (Inventory.EquippedItems[EquipmentSlot.ShieldHand]?.Item is Armor)
		{
			flourishes.Add((GuardClip, 1));
		}
		if (flourishes.Count == 0)
		{
			flourishes.Add((CheerClip, 1));
		}
		return flourishes;
	}

	/// <summary>The weapon's attack on this rig, sped to the weapon's own swing time; null if the rig has no clip for it.</summary>
	private (StringName Clip, float Speed)? AttackOf(Weapon weapon)
	{
		if (Animations == null || !Animations.TryGetBinding(weapon.AttackStyle, out AttackAnimationBinding binding)
			|| !_animation.HasAnimation(binding.Clip))
		{
			return null;
		}

		float length = (float)_animation.GetAnimation(binding.Clip).Length;
		return (binding.Clip, Mathf.Clamp(length / Mathf.Max(0.1f, (float)weapon.PerformDuration), 0.75f, 1.75f));
	}

	/// <summary>Starts a pose picked at random from those the rig can play, never the one just struck.</summary>
	private void StrikePose()
	{
		ShowcasePose[] playable = [.. Poses.Where(pose => pose != null && pose.Clips.Length > 0 && pose.Clips.All(clip => _animation.HasAnimation(clip)))];
		ShowcasePose[] fresh = [.. playable.Where(pose => pose != _lastPose)];
		ShowcasePose[] choices = fresh.Length > 0 ? fresh : playable;
		if (choices.Length == 0)
		{
			_untilFlourish = FlourishInterval;
			return;
		}

		_lastPose = choices[GD.RandRange(0, choices.Length - 1)];
		foreach (StringName clip in _lastPose.Clips)
		{
			_poseClips.Enqueue(clip);
		}
		PlayNextPoseClip();
	}

	private bool PlayNextPoseClip()
	{
		if (!_poseClips.TryDequeue(out StringName clip))
		{
			return false;
		}

		Flourish(clip, 1);
		return true;
	}

	private void Flourish(StringName clip, float speed)
	{
		if (!_animation.HasAnimation(clip))
		{
			return;
		}

		_animation.Play(clip, BlendSeconds, speed);
		_flourishRemaining = _animation.GetAnimation(clip).Length / speed;
	}

	private void PlayIdle()
	{
		_poseClips.Clear();
		_flourishRemaining = 0;
		_untilFlourish = FlourishInterval;
		if (_animation.HasAnimation(_class.PreviewAnimation))
		{
			_animation.Play(_class.PreviewAnimation, BlendSeconds);
		}
	}
}
