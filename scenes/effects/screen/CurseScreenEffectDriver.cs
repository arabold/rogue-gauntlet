using Godot;
using System;

/// <summary>
/// Plays a brief <see cref="ScreenEffect"/> flash when the observed <see cref="PlayerInventoryController"/>
/// equips a cursed item.
/// </summary>
[GlobalClass]
public partial class CurseScreenEffectDriver : ScreenEffectDriver
{
	private PlayerInventoryController _inventoryController;

	/// <summary>
	/// Observed inventory controller. Runtime context, normally assigned by <see cref="ScreenEffectsLayer.Bind"/>
	/// once the player spawns.
	/// </summary>
	[Export]
	public PlayerInventoryController InventoryController
	{
		get => _inventoryController;
		set => SetSource(ref _inventoryController, value);
	}

	/// <summary>Seconds to ramp in when the curse triggers.</summary>
	[Export(PropertyHint.Range, "0,1,0.01,suffix:s")] public float FadeInTime { get; set; } = 0.08f;

	/// <summary>Seconds to hold at peak intensity before fading out.</summary>
	[Export(PropertyHint.Range, "0,2,0.05,suffix:s")] public float HoldTime { get; set; } = 0.35f;

	/// <summary>Seconds to fade back out after the hold.</summary>
	[Export(PropertyHint.Range, "0,3,0.05,suffix:s")] public float FadeOutTime { get; set; } = 0.75f;

	protected override void Bind()
	{
		Rebind(
			_inventoryController,
			controller => controller.CursedItemEquipped += OnCursedItemEquipped,
			controller => controller.CursedItemEquipped -= OnCursedItemEquipped);
	}

	private void OnCursedItemEquipped(EquipableItem item)
	{
		Effect?.Pulse(FadeInTime, HoldTime, FadeOutTime);
	}
}
