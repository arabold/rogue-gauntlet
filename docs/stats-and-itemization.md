# Stats and itemization

Implemented item ownership, inventory and stat rules. Godot Resources supply definitions and tuning; [item identification](item-identification-system.md) owns hidden type knowledge, the [gameplay catalog](item-catalog.md) owns content references, and [randomness and saves](randomness-and-seeds.md) owns seeded reconstruction.

## Attributes and modifiers

Player stats are copied per actor. Strength, Dexterity, Vitality and Intelligence feed secondary stats through an authored `StatProfile`; class profiles choose their coefficients. Modifiers apply flat contributions before percentage contributions. Equipment and buffs register contributions by source, so removing one source reverses its effects without changing another's.

Supported secondary stats include maximum health, speed, damage range, critical chance and armor. Dexterity does not grant passive accuracy or evasion. Resolved values drive health, movement, damage and the HUD; maximum-health derivation must not be fed back into base stats. See [active defense](combat-system.md#contact-resolution) for hit handling.

Timed stat buffs expire once; permanent buffs remain until explicitly removed. Periodic healing/poison use their own authored effects. Class starting profiles and gear are described in [character classes](character-classes.md).

## Buffs and status conditions

A buff can declare the status it imposes in `Buff.Condition`, a `StatusCondition` Resource such as
`scenes/buffs/conditions/poisoned.tres`. The condition file is the identity and is compared by
reference, so always reference it as an external resource: `ItemFactory`'s deep duplicate copies
embedded buffs but keeps external conditions shared. `BuffController.HasCondition` is true while
any active buff carries the condition, so overlapping sources, such as poisoned food followed by a
poison potion, read as one continuous condition. `BuffController` fires one `BuffsChanged` per
application, removal, expiry sweep or `ClearBuffs`, and observers re-read what they need.
Presentation, such as [screen effects](screen-effects.md), and gameplay react to conditions for
statuses without a magnitude, and to buff subclasses for effects with one:

- A plain `Buff` with only a `Condition` is a status with no behavior of its own: blindness
  (`blinded`) and confusion (`confused`). The blindness overlay and the player's
  `ConfusionComponent` react to those conditions.
- `TimeScaleBuff` (Slowness) slows the whole game. The player's `TimeScaleComponent` applies the
  slowest active one to `Engine.TimeScale` and restores normal speed when the last one ends or the
  scene unloads. Durations count game time.
- `Buff.CuredConditions` ends conditions when any buff is applied; healing potions cure blindness.

## Equipment quality, affixes and enchantment

Equipment has two independent properties:

1. **Base item.** The authored definition: kind, model and baseline values. Weapons and armor carry a `Tier` (1–5) that loot-table depth windows fence. Jewelry has no tier.
2. **Quality.** `ItemQuality` is rolled once at creation, stored on the instance and never secret, not even on unidentified jewelry. Poor and Superior scale base stats; Magic and Rare add affixes instead. One color per quality is shared by loot beams, slot tints, names and tooltips (`ItemQualityPalette`); orange is reserved for future Unique items.

| Quality | Color | Base scale | Affixes | Introduced | Share at depth 12 / 24 |
| --- | --- | --- | --- | --- | --- |
| Poor | grey | ×0.70–0.90 | 0 | 1 | 10% / 5% |
| Normal | white | ×1.00 | 0 | 1 | 57% / 49% |
| Superior | white | ×1.10–1.25 | 0 | 1 | 10% / 10% |
| Magic | green | ×1.00 | 1–2 | 2 | 19% / 28% |
| Rare | blue | ×1.00 | 3–4 | 5 | 4% / 7.5% |

All numbers live in [quality_ladder.tres](../scenes/items/quality/quality_ladder.tres). Each `QualityLevel` is introduced at `MinDepth`, and from there its weight is `max(0, StartWeight + WeightPerDepth × (depth − MinDepth))`. No quality fades out entirely: later qualities simply outweigh earlier ones, so a Normal item can still drop at depth 20. Depth mainly makes items better at what they are, through base tiers and affix tiers, rather than more often magical.

**Everything rolls at creation.** `LootRoller.ApplyRoll` draws from the source's loot RNG in a fixed order: quality, adjective, scale, affix count, affixes, curse, penalty. Nothing rolls later; copying, transferring, equipping or restoring an item never rerolls it. One scale applies to all of an item's intrinsic values, so its name and stats agree:
- weapons scale `DamageMin`/`DamageMax`, rounded to whole points with the maximum never below the minimum;
- armor scales `ArmorPoints`;
- jewelry scales every intrinsic modifier, harmful types included, so a superior Ring of Frailty is the worst one.

Flat values round half away from zero and keep a nonzero value at least 1 in magnitude; percentages and critical chance round to whole percents. Critical chance and armor speed penalties are not scaled. Fixed creation (starting gear, plain debug spawns) keeps Normal at a scale of exactly 1, so authored values show through unchanged.

**Roll tables.** Each definition references one `EquipmentRollTable` in [scenes/items/quality/tables/](../scenes/items/quality/tables/): ring, amulet, metal weapon, wooden weapon or shield. A table is plain authored arrays: `PoorAdjectives` and `SuperiorAdjectives`, one picked at random for those qualities, and `Prefixes` and `Suffixes` for Magic and Rare. The array an affix sits in decides whether it reads before or after the name. A new kind of equipment is a new table, not new code.

**Affixes.** Each `Affix` is one tier of a family, authored as its own file in [scenes/items/affixes/](../scenes/items/affixes/) so several tables can share it. Tiers open by depth (`MinDepth`), and every tier at or below the drop depth stays eligible and weighted, which keeps power creep slow. An item holds at most one affix per family (`Group`) and at most half its affix count, rounded up, of each kind: Magic gets one prefix, one suffix or both; Rare gets two of one kind and one or two of the other.

| Family | Kind | Tiers (introduction depth) |
| --- | --- | --- |
| sharpness | prefix | Sharp (1), Vicious (6), Brutal (14): flat damage |
| ferocity | prefix | Savage (8), Merciless (18): damage % |
| keenness | prefix | Keen (4), Deadly (12), Vorpal (20): critical chance |
| plating | prefix | Sturdy (1), Fortified (8), Mithril (16): flat armor |
| warding | prefix | Warded (6), Impenetrable (16): armor % |
| strength, dexterity, vitality, intelligence | suffix | of the Ox / Fox / Bear / Owl (1), of the Titan / Elves / Troll / Sage (8), of the Giant / Shadows / Colossus / Magi (16) |
| health | suffix | of Vigor (1), of Life (8), of the Juggernaut (16) |
| speed | suffix | of Haste (6), of Alacrity (14): speed % |

Weapons roll sharpness, ferocity and keenness; shields roll plating and warding; jewelry rolls plating and keenness. Every table rolls the attribute and health suffixes, and shields and jewelry add speed.

**Names.** Poor and Superior lead with their adjective ("Cracked Broadsword", "Polished Broadsword"); Normal shows the base name alone. Magic and Rare wrap the base name in their first prefix and first suffix ("Vicious Broadsword of the Ox"); further affixes add stats but no name. Identified jewelry reads `{adjective or first prefix} {true name} {signed primary value}`, e.g. "Ring of Strength +3", "Shiny Ring of Strength +4" or "Keen Ring of Strength +3". Unidentified jewelry leads with its quality word: "Shiny golden ruby ring", "Magic golden ruby ring" (see [identification](item-identification-system.md#model-and-size-two-independent-channels)). Base names carry no condition words, since the quality names the condition.

An `ItemCreationPolicy` chooses fixed creation or randomized equipment/food initialization; rolling equipment requires its `QualityLadder` and a roll table on the definition. Starting supplies use fixed creation; dungeon sources use an explicit policy and RNG.

Enchantment adds one affix from the item's table, eligible at the current depth and never stacking a family it already holds. The item becomes Magic or Rare to match its affix count, dropping any Poor or Superior adjective but keeping its base scale; an item at Rare's maximum is not a valid target. Equipped targets are safely rebound so resolved stats reflect the changed item.

Magic and Rare items drop with hidden attributes. They read "Magic Broadsword" (green) or "Rare Broadsword" (blue) and show their quality but no stat lines. Wearing them, reading Identify on them or enchanting them reveals everything (see [per-item knowledge](item-identification-system.md#per-item-knowledge)).
**Curses.** Any equipment can carry a hidden curse, rolled at creation like everything else.
- **Chance:** the creation policy's `CurseChance` (10%) applies independently of quality. Harmful types (`AlwaysCursed`: Ring of Frailty, Ring of Lethargy, Amulet of Dread) are always cursed, and fixed creation never is.
- **Penalty:** every cursed item, harmful types included, carries one hidden penalty, picked from the policy's `CursePenalties` and stored as the item's `CursePenalty` modifiers (an item is cursed exactly when it has them). These are unnamed, depth-tiered affixes in [scenes/items/affixes/curses/](../scenes/items/affixes/curses/): −health, −Strength, −Dexterity, −speed %, −damage % or −armor. The penalty applies while worn and shows in the HUD's stats, but the tooltip lists it only once the curse is revealed.
- **Sticky:** once worn, a cursed item cannot be unequipped, displaced by equipping something else (including the other hand of a two-handed combination), dropped or destroyed. `Inventory` returns `ItemFailure.Cursed` without changing anything, and names the blocking item as the result's subject.
- **Revealed:** the player's first blocked attempt reveals the curse ("You can't remove it. It is cursed."), as does the scroll of identify. Wearing an item never reveals it. A revealed curse leads the name ("Cursed Vicious Broadsword") and the tooltip shows "Cursed" with the penalty; a known clean item shows "Uncursed".
- **Lifted:** Enchantment lifts the curse from its target. So does the Scroll of Remove Curse, which targets one item, worn or not. Any item not known to be uncursed is a valid target, and both leave the item known as uncursed, so neither reveals whether a curse was there. Both change the item in place through `Inventory.ChangeItem`; a worn item stays worn and only its stats are re-registered.

## Loot eligibility

Source tables own depth bounds, quantities and category budgets. Items do not infer drop eligibility from their model name. Grouped loot selects an eligible category before a variety inside it, so adding weapon variants cannot displace the authored recovery-supply budget. Simple tables may use flat entries; do not author flat and grouped forms together.

A source's `LootTableComponent.CreationPolicy` decides how its items roll. The Forsaken Warden uses its own [loot table](../scenes/enemies/forsaken_warden/forsaken_warden_loot.tres) of tier-2 weapons and the [gatekeeper policy](../scenes/items/gatekeeper_item_policy.tres), whose ladder rolls only Magic (70%) or Rare (30%) and never curses. Its guaranteed drop is a clearly better weapon to carry into depth 4.

Weapon and armor base tiers are fenced over the 24-depth run (8 realms of 3 depths), so the highest tier belongs to the final two realms. Neighboring tiers overlap to smooth transitions:

| Base tier | Depths | Realms |
| --- | --- | --- |
| T1 | 1–6 | 1–2 |
| T2 | 4–12 | 2–4 |
| T3 | 10–18 | 4–6 |
| T4 | 16–21 | 6–7 |
| T5 | 19–24 | 7–8 |

Every jewelry type is eligible from depth 1; its quality carries the progression. See the [catalog](item-catalog.md#source-budgets-and-eligibility) and the source `.tres` files for actual eligibility and weights. Monster threat and item tier are selection/tuning concepts, not automatic depth-based stat scaling.

## Portable-light equipment

Six `PortableLightItem` definitions provide held illumination. Each explicitly assigns a `HeldScene` and `WeaponHandGrip`/`ShieldHandGrip` adapters; its ordinary `Scene` remains the unlit pickup/inventory model. The attachment manager instances the held scene only while equipped, so removing, dropping or displacing a light removes its model, flame and illumination together. `FloorLightingComponent` switches the explicitly assigned normal/dark lighting subtrees on the level's local darkness signal. Normal floors use the original broad player lights and suppress held illumination. Dark floors use a dim player baseline and enable held lights. Flame visuals remain visible while equipped.

The inventory menu offers left (shield) and right (weapon) hands. Automatic equip prefers an empty hand, checking the shield hand first; when both hands are occupied it replaces the shield. A light displaces that hand's occupant, and a shield-hand light also displaces a two-handed weapon. Equipping a two-handed weapon removes either held light. Existing curse checks keep these transfers atomic. Displaced gear stays in the backpack. Current-version saves preserve the item identity and hand assignment; restoring equipment recreates the held light.

Lights currently illuminate while equipped on dark floors, with no fuel consumption or manual ignition. They are available in the debug spawn menu but excluded from normal loot and quality rolls pending supply and fuel design. Asset Studio recipes export these models with their tops pointing upward in idle and walking poses, so their model wrappers need no extra rotation. The right hand reuses the existing melee grip; the left-hand adapter accounts for its mirrored socket. Light and flame placements follow the authored model rotation.

## Item ownership, food, and persistence

Authored definitions remain shared and immutable. Every runtime individual or homogeneous stack owns its Resource, definition provenance and instance identity. Equipment references inventory entries rather than a second copy. Whole-item transfers preserve identity; splitting creates another record, and merging retires the source record.

The initial backpack holds 20 entries, including equipped gear. Currency remains in player stats. Potions and scrolls stack to 20 of the exact same definition; equipment and food remain individual. Capacity is saved. Compatible merges can succeed in a full backpack without exceeding a stack cap. Larger-backpack acquisition remains future work.

Pickup, use, drop and equipment operations validate ownership, quantity, targets and space before committing. Failed pickup preserves the world source; a successful use applies one effect and consumes one unit. UI and quick slots use the same ownership rules. Identification knowledge does not determine stack equality.

Food recovery and contamination are per-piece state. Dungeon creation may roll contamination once; fixed starting supplies do not. Copying or loading preserves it. Food has no type-wide disguise, stacking cannot disclose contamination, and there is no hunger system.

Inventory restoration validates the complete state before replacement and preserves quantities, quality, adjective, value scale, affixes, curse and penalty and what is known about them, food state and equipment references. Floors regenerate their untouched baseline and restore saved differences. Current-version compatibility is defined by `SaveGame.CurrentVersion`; older development saves require a new run, without conversion or silent overwrite. Active timed buffs and living-enemy injuries/positions are outside the current persistence scope.

## Authoring guide

- Add buffs through `StatModifierBuff` and its modifier list; set duration to distinguish timed and permanent effects.
- Tune quality odds, introduction depths, base-stat bands and affix counts in `quality_ladder.tres`. Add affix tiers as files and list them in the roll tables that may roll them, rather than in hardcoded gameplay paths.
- Assign an `EquipmentRollTable` on every equipment definition and keep condition words out of base names.
- Create item definitions with explicit stack limits, effects, slots, tier and visual wrapper. Assign creation policy and loot eligibility at the source.
- Verify handedness and held scale on the actual player rig. See [equipped orientation](item-catalog.md#equipped-orientation); floating previews are insufficient.
- Use the Remote Inspector to inspect runtime copies. Use supported equipment/enchantment commands instead of editing live modifier collections without rebinding.

Run [item_lifecycle_harness.tscn](../scenes/debug/items/item_lifecycle_harness.tscn) with F6 in Godot to exercise creation, equipment, food, stacks and capacity. Its Inspector exposes definitions, policy, seed, depth and quantity. In the main game, the debug menu's "Spawn quality" picker rolls spawned equipment like a drop with a chosen class, and its "Cursed" toggle forces a curse. Review save/resume and floor revisits in gameplay; the [roadmap](development-roadmap.md) owns remaining acceptance and extensions.
