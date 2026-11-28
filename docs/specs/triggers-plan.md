# Triggers: switches, puzzles and linked doors

Status: partly implemented. The trigger core (sources, `TriggerOutputComponent` with Pulse, Hold and Latch modes, `ITriggerable` receivers), the spike trap rebuilt as a concealed `PressurePlate`, wall launchers and plate traps on generated floors landed: [level design](../level-design.md#triggers) describes the model and plate-trap placement, and [combat](../combat-system.md#trap-hazards) the traps. What remains is switches, puzzle conditions, item-weighted plates, saved latches, doors as receivers and a quest seam.

## Goals

- **Switches and puzzles.** A switch is a visible lever the hero pulls between two positions. It may open a gate across the map, and several switches or plates can have to be set a certain way together. Plates also take dropped items as weight, so an item left on a plate can hold a gate open.
- **One model.** Plates, switches and conditions are sources; launchers, spikes and doors are receivers. Neither side knows the other's type, and a quest system later listens to the same changes.

## New sources and receivers

| Piece | Behavior |
| --- | --- |
| **Switch** | On while in position B. The hero pulls it with a short timed interaction (the door's), from A to B and back; its authored start position is part of the floor. |
| **Condition** | On while every source it watches is in its required state (`RequiredOn`, `RequiredOff`); itself a source, so conditions nest. |
| **Item-weighted plate** | A puzzle plate whose trigger mask also accepts dropped items and, if its author chooses, monsters. Trap plates stay hero-only. |
| **Door receiver** | `DoorTriggerComponent` makes its door a receiver: a pulse toggles it, on opens it, off shuts it. |

```csharp
/// <summary>A lever the hero pulls between positions A and B; on in B.</summary>
[GlobalClass] public partial class Switch : Node3D
{
	[Export] public InteractiveComponent Interactive { get; set; }
	[Export] public TriggerOutputComponent Output { get; set; }
	[Export] public Node3D Lever { get; set; }
	[Export] public bool StartsInB { get; set; }
	public void RestorePosition(bool inB);       // no swing
}

/// <summary>A puzzle: on while every watched output is in its required state; itself a source.</summary>
[Tool, GlobalClass] public partial class TriggerCondition : Node
{
	[Export] public TriggerOutputComponent[] RequiredOn { get; set; } = [];
	[Export] public TriggerOutputComponent[] RequiredOff { get; set; } = [];
	[Export] public TriggerOutputComponent Output { get; set; }
}

[GlobalClass] public partial class DoorTriggerComponent : Node, ITriggerable { [Export] public Door Door { get; set; } }
```

## Saving and restore

- Each floor saves its switches out of their start position (`FlippedSwitchIds`) and its latched outputs (`LatchedOutputIds`); outputs gain a persistent ID.
- `TriggerActivation` gains `Restored`: restoring a saved floor replays Hold and Latch state to receivers without effects, so no shot fires and doors snap instead of swinging.
- Plate occupancy is not saved: items left on a plate are item deltas already, and the plate re-reads them on load.
- Latch-mode plate traps (a flame jet that never stops) wait for saved latches.
- Bump `SaveGame.CurrentVersion` with the slice that changes the save.

## Slices

1. **Switches, conditions and items.** `Switch`, `TriggerCondition`, item-weighted plates, saved switch positions and latches, restore without effects. Verify: a pull toggles its output and survives save and load; a condition turns on only when every requirement holds; an item dropped on a plate holds it on.
2. **Doors as receivers, and authored puzzles.** `DoorTriggerComponent` and one or two authored uses: a switch that opens a gate elsewhere on the floor, placed with its gate by a [room group](../level-design.md#floor-plans), and a switch-combination vault. A gate held by a trigger is never also a lock, and generated access (locks and keys) must still solve.
3. **Quest seam.** `TriggerOutputComponent` also broadcasts `SignalBus.TriggerSwitched(persistentId, on)` for a quest system. Deferred until quests are planned.

## Open decisions

- **Do monsters press trap plates?** Proposed no: monsters walking into their own traps would need trap-aware navigation. Yes would let the hero lure monsters onto plates. Puzzle plates choose for themselves.
- **Switch-to-gate placement:** which room groups pair a switch with a gate elsewhere, and how far apart, is a level-design choice for slice 2.
