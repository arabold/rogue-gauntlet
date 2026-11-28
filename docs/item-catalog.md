# Gameplay Item Catalog

The Godot `.tres` definitions under [`scenes/items/`](../scenes/items/) are the gameplay catalog; this page maps their folders, families and rules. Stats, timing, prices and weights live in the definitions and loot Resources, not here. Exported art under `assets/custom-items/` supplies models through each item's wrapper scene; it does not determine effects. The [visual item catalog](item-catalog/README.md) pictures every weapon and shield by tier, and the [Asset Studio catalogs](../asset-studio/catalogs/items/README.md) show the published models.

Definitions and stateless strategies are authored in Godot. `ItemFactory` creates runtime copies once; `ItemSaveCodec` restores exact state without rolling. See [stats and itemization](stats-and-itemization.md) for runtime creation, quality and inventory rules.

## Authoring rules

- Food and equipment are individual. Potions, scrolls, keys and lockpicks stack to 20 of the exact same definition; gold pickups cap at 999. [Door access](door-access.md) consumes one matching key or pick on a successful unlock.
- A hidden type ID identifies an effect family across visual and size variants, not one mesh. Potency and intrinsic modifiers remain definition data; different definitions do not merge.
- All active hidden families are in `identity_catalog.tres`; each category has enough distinct appearances for a one-to-one per-run assignment (see [item identification](item-identification-system.md)).
- Weapons, armor and jewelry drop in their `Tier`'s [depth band](stats-and-itemization.md#loot-eligibility); tier-0 jewelry drops at every depth. The [quality](stats-and-itemization.md#equipment-quality-affixes-and-enchantment), its adjective, value scale and affixes are rolled from the definition's `EquipmentRollTable`, which every equipment definition references. Base names carry no condition words such as "Old" or "Worn"; the quality names the condition, and each base name belongs to one tier. `RequiredStrength` is an advisory value, not an equip restriction.
- Equipment numbers come from the [balance model](balance-model.md#gear) through `simulate.py derive`; consumable potency and source weights are authored tuning.
- Every weapon names an attack style and a grip. Polearms use the shared melee styles (the spear and trident thrust, the halberd chops, the scythe slashes); their names imply no reach or cleave rules. Wands and staves are spell weapons (see [weapon attacks](weapon-attack-system.md#spell-weapons-wands-and-staves)).
- A pickup shows its model at the size its wrapper scene sets. Weapons and shields lie at the size the hero holds them and body armor at the size the hero wears it. Helmets and hats lie at three quarters of their worn size: at full size the pack's big heads look oversized on the floor. Every other pickup only ever shows on the floor and as an icon, so its wrapper sizes it to the pack's props beside the 2.2 m hero (KayKit's bottles and key are about 0.9 m), never below what reads from the game camera. `ItemCatalogTest` holds every pickup to a minimum size and floor-only ones to a maximum.

## Effects

Potions follow Rogue and NetHack (see [potion effects](item-identification-system.md#potion-effects)): Healing, Strength, Dexterity, Vitality, Intelligence, Poison, Slowness, Blindness and Confusion. Each type has small, medium and large definitions (`potion_<type>_<size>.tres`); size scales healing, damage, attribute gain or duration. No model or color belongs to a type: each run assigns every type a random color from the Asset Studio potion models, shared by all three sizes.

Scrolls work the same way: one `Scroll` definition per type (`scroll_<type>.tres`), so the UI offers Read. Identify, enchant, remove curse, teleport and summon are `ScrollEffect` strategies; haste, protection, rage, weakness, vitality and clarity are plain buffs. Each run assigns every type one of the 12 Asset Studio rune scrolls.

Rings and amulets are identity types too. Each jewelry type with points has one definition per tier (1–5), sharing its name and appearance; quality scales or extends each item with affixes. Swiftness, Lethargy and Dread change speed or damage by a percentage and have one tier-0 definition. The three harmful types (Frailty, Lethargy, Dread) have no positive effect and are always created cursed, so wearing one is a trap. Material and gem are part of the run disguise, never a sign of strength. Potion, scroll and jewelry definitions have no model of their own: they only ever show their run appearance.

Food recovery and per-piece contamination use their authored buffs; there is no hunger system.

## Source budgets and eligibility

Source tables contain seven Inspector-editable `LootTableGroup`s (gold, food, potions, scrolls, weapons, armor, jewelry), each a weight plus a shared `LootPool` from [`scenes/items/loot/pools/`](../scenes/items/loot/pools/). A pool lists one category's varieties with their depth windows and quantities, so adding an item is one edit for every table; the weapon, armor and jewelry pools use the tier depth bands instead of per-entry windows. A roll selects a positive-weight eligible group first, then a positive-weight eligible variety inside it, so adding weapon models cannot displace the potion or food budget. Empty or ineligible groups redistribute their weight among the remaining eligible groups. Flat `LootTable.Items` remains available for simple custom tables; `LootTable.Validate()` rejects mixing flat items and groups.

A source's `LootDropProfile` names its table, drop chance, roll count and item creation policy. Monster definitions and chest scenes reference shared profiles in [`scenes/items/loot/drops/`](../scenes/items/loot/drops/); gatekeepers carry their own beside their scenes. Humanoid enemies favor currency and recovery, beasts drop only raw meat, and rarer chests favor equipment. Base tier eligibility and rolled quality are independent, and both are fenced by depth. Every jewelry type is eligible from depth 1, through its tier-1 or tier-0 definition. Guaranteed matching keys are placed by the level access profile separately from random loot. Lockpicks never spawn on floors or in loot tables; the Rogue starts with two, and the [Quartermaster](shops.md) sells them. Portable lights and tools stay out of normal loot (see below). `loot_enemy_elite.tres` is an unused table.

## Active definitions

| Category | Folder | Definitions | Notes |
| --- | --- | ---: | --- |
| Weapons | [weapons/](../scenes/items/weapons/) | 80 | Tiers 1–5; one subfolder per kind for the newer models. Pictured by tier in the [visual catalog](item-catalog/README.md#weapons) |
| Shields | [armor/shield/](../scenes/items/armor/shield/) | 28 | Tiers 1–5; pictured in the [visual catalog](item-catalog/README.md#shields) |
| Body armor and helmets | [armor/body/](../scenes/items/armor/body/), [armor/helmet/](../scenes/items/armor/helmet/) | 14 | Listed below |
| Jewelry | [jewelry/ring/](../scenes/items/jewelry/ring/), [jewelry/amulet/](../scenes/items/jewelry/amulet/) | 68 | 16 types; listed below |
| Food and drink | [food/food/](../scenes/items/food/food/), [food/drink/](../scenes/items/food/drink/) | 13 | Listed below |
| Potions | [potions/](../scenes/items/potions/) | 27 | 9 types in three sizes; listed below |
| Scrolls | [scrolls/](../scenes/items/scrolls/) | 11 | Listed below |
| Gold | [gold/](../scenes/items/gold/) | 1 | `currency`; pile size grows with depth |
| Keys and lockpicks | [keys/key/](../scenes/items/keys/key/), [tools/tool/](../scenes/items/tools/tool/) | 7 | See [access keys and picks](#access-keys-and-picks) |
| Portable lights | [tools/lamp/](../scenes/items/tools/lamp/) | 6 | Debug only; see [portable lights](#portable-lights) |
| Deferred tools | [tools/tool/](../scenes/items/tools/tool/) | 5 | See [deferred definitions](#deferred-definitions) |

### Body armor and helmets (14)

Body armor and helmets show on the hero. Body armor is a whole suit: the torso plus a KayKit class's own arms and legs (gauntlets, sleeves, greaves, boots), worn on the hero's own skeleton in place of the hero's plain limbs; its floor and inventory model is the torso alone. Helmets are the classes' own KayKit hats, mounted on the head bone, and a helmet hides the head parts it covers. Each class starts in its own pair (see [character classes](character-classes.md#the-shipped-roster)). Each weight has suits at tiers 1, 3 and 5, its limbs from one KayKit class: light the rogue's (the mage coat the mage's), medium the barbarian's, heavy the knight's.

| Definition | Slot | Weight | Tier |
| --- | --- | --- | --- |
| [Mage Coat](../scenes/items/armor/body/mage_coat.tres) | Body | Light | 1 |
| [Ranger Coat](../scenes/items/armor/body/ranger_coat.tres) | Body | Light | 1 |
| [Studded Leather](../scenes/items/armor/body/studded_leather.tres) | Body | Light | 3 |
| [Nightweave Armor](../scenes/items/armor/body/nightweave_armor.tres) | Body | Light | 5 |
| [Barbarian Harness](../scenes/items/armor/body/barbarian_harness.tres) | Body | Medium | 1 |
| [Scale Mail](../scenes/items/armor/body/scale_mail.tres) | Body | Medium | 3 |
| [Mithril Hauberk](../scenes/items/armor/body/mithril_hauberk.tres) | Body | Medium | 5 |
| [Knight Armor](../scenes/items/armor/body/knight_armor.tres) | Body | Heavy | 1 |
| [Steel Plate](../scenes/items/armor/body/steel_plate.tres) | Body | Heavy | 3 |
| [Gilded Full Plate](../scenes/items/armor/body/gilded_full_plate.tres) | Body | Heavy | 5 |
| [Rogue Hood](../scenes/items/armor/helmet/rogue_hood.tres) | Helmet | Light | 1 |
| [Wizard Hat](../scenes/items/armor/helmet/wizard_hat.tres) | Helmet | Light | 1 |
| [Bear Hood](../scenes/items/armor/helmet/bear_hood.tres) | Helmet | Medium | 1 |
| [Knight Helmet](../scenes/items/armor/helmet/knight_helmet.tres) | Helmet | Heavy | 1 |

### Jewelry (16 types, 68 definitions)

Tiered types have five definitions, `<type>_1.tres` to `<type>_5.tres`; the link shows tier 1.

| Definition | Family | Tier |
| --- | --- | --- |
| [Amulet of Dread](../scenes/items/jewelry/amulet/amulet_dread.tres) | `amulet.dread` (harmful) | 0 |
| [Amulet of Health](../scenes/items/jewelry/amulet/amulet_health_1.tres) | `amulet.health` | 1–5 |
| [Amulet of Mind](../scenes/items/jewelry/amulet/amulet_mind_1.tres) | `amulet.mind` | 1–5 |
| [Amulet of Protection](../scenes/items/jewelry/amulet/amulet_protection_1.tres) | `amulet.protection` | 1–5 |
| [Amulet of Strength](../scenes/items/jewelry/amulet/amulet_strength_1.tres) | `amulet.strength` | 1–5 |
| [Amulet of Swiftness](../scenes/items/jewelry/amulet/amulet_swiftness.tres) | `amulet.swiftness` | 0 |
| [Amulet of Vitality](../scenes/items/jewelry/amulet/amulet_vitality_1.tres) | `amulet.vitality` | 1–5 |
| [Ring of Dexterity](../scenes/items/jewelry/ring/ring_dexterity_1.tres) | `ring.dexterity` | 1–5 |
| [Ring of Frailty](../scenes/items/jewelry/ring/ring_frailty_1.tres) | `ring.frailty` (harmful) | 1–5 |
| [Ring of Health](../scenes/items/jewelry/ring/ring_health_1.tres) | `ring.health` | 1–5 |
| [Ring of Lethargy](../scenes/items/jewelry/ring/ring_lethargy.tres) | `ring.lethargy` (harmful) | 0 |
| [Ring of Mind](../scenes/items/jewelry/ring/ring_mind_1.tres) | `ring.mind` | 1–5 |
| [Ring of Protection](../scenes/items/jewelry/ring/ring_protection_1.tres) | `ring.protection` | 1–5 |
| [Ring of Savagery](../scenes/items/jewelry/ring/ring_savagery_1.tres) | `ring.savagery` | 1–5 |
| [Ring of Strength](../scenes/items/jewelry/ring/ring_strength_1.tres) | `ring.strength` | 1–5 |
| [Ring of Vitality](../scenes/items/jewelry/ring/ring_vitality_1.tres) | `ring.vitality` | 1–5 |

### Food (13)

Twelve foods and drinks drop from depth 1 with per-piece contamination: [drinks](../scenes/items/food/drink/) (beer stein, water canteen, red wine) and [food](../scenes/items/food/food/) (apple, bread loaf, sweet roll, cheese, grapes, drumstick, sausage, steak, golden pear). [Raw Meat](../scenes/items/food/food/food_meat_raw.tres) is the beasts' only drop and is often tainted. The `food_poison_*.tres` files beside them are the food-poisoning buffs, not items.

### Potions (27)

Small potions drop at depths 1–12, medium from 9 and large from 17. Healing has the largest share of the potion budget, then blindness and confusion.

| Type | Family | Definitions |
| --- | --- | --- |
| Healing | `potion.healing` | [small](../scenes/items/potions/potion_healing_small.tres), [medium](../scenes/items/potions/potion_healing_medium.tres), [large](../scenes/items/potions/potion_healing_large.tres) |
| Strength | `potion.strength` | [small](../scenes/items/potions/potion_strength_small.tres), [medium](../scenes/items/potions/potion_strength_medium.tres), [large](../scenes/items/potions/potion_strength_large.tres) |
| Dexterity | `potion.dexterity` | [small](../scenes/items/potions/potion_dexterity_small.tres), [medium](../scenes/items/potions/potion_dexterity_medium.tres), [large](../scenes/items/potions/potion_dexterity_large.tres) |
| Vitality | `potion.vitality` | [small](../scenes/items/potions/potion_vitality_small.tres), [medium](../scenes/items/potions/potion_vitality_medium.tres), [large](../scenes/items/potions/potion_vitality_large.tres) |
| Intelligence | `potion.intelligence` | [small](../scenes/items/potions/potion_intelligence_small.tres), [medium](../scenes/items/potions/potion_intelligence_medium.tres), [large](../scenes/items/potions/potion_intelligence_large.tres) |
| Poison | `potion.poison` | [small](../scenes/items/potions/potion_poison_small.tres), [medium](../scenes/items/potions/potion_poison_medium.tres), [large](../scenes/items/potions/potion_poison_large.tres) |
| Slowness | `potion.slowness` | [small](../scenes/items/potions/potion_slowness_small.tres), [medium](../scenes/items/potions/potion_slowness_medium.tres), [large](../scenes/items/potions/potion_slowness_large.tres) |
| Blindness | `potion.blindness` | [small](../scenes/items/potions/potion_blindness_small.tres), [medium](../scenes/items/potions/potion_blindness_medium.tres), [large](../scenes/items/potions/potion_blindness_large.tres) |
| Confusion | `potion.confusion` | [small](../scenes/items/potions/potion_confusion_small.tres), [medium](../scenes/items/potions/potion_confusion_medium.tres), [large](../scenes/items/potions/potion_confusion_large.tres) |

### Scrolls (11)

Targeted scrolls open the Pack's targeting mode when read (see [scroll targeting](item-identification-system.md#scrolls-and-targeting)). Summoning and Enchantment drop from depth 2, the rest from depth 1.

| Definition | Family | Effect |
| --- | --- | --- |
| [Scroll of Identification](../scenes/items/scrolls/scroll_identify.tres) | `scroll.identify` | Reveals a chosen item's type, attributes and curse status (targeted) |
| [Scroll of Enchantment](../scenes/items/scrolls/scroll_enchant.tres) | `scroll.enchant` | Adds one affix to a chosen item and lifts its curse (targeted) |
| [Scroll of Remove Curse](../scenes/items/scrolls/scroll_remove_curse.tres) | `scroll.remove_curse` | Lifts the curse from a chosen item (targeted) |
| [Scroll of Teleportation](../scenes/items/scrolls/scroll_teleport.tres) | `scroll.teleport` | Moves the reader to a random free spot |
| [Scroll of Summoning](../scenes/items/scrolls/scroll_summon.tres) | `scroll.summon` | Summons monsters around the reader (harmful) |
| [Scroll of Haste](../scenes/items/scrolls/scroll_haste.tres) | `scroll.haste` | Timed speed |
| [Scroll of Protection](../scenes/items/scrolls/scroll_protection.tres) | `scroll.protection` | Timed armor |
| [Scroll of Rage](../scenes/items/scrolls/scroll_rage.tres) | `scroll.rage` | Timed damage |
| [Scroll of Vitality](../scenes/items/scrolls/scroll_vitality.tres) | `scroll.vitality` | Timed Vitality |
| [Scroll of Clarity](../scenes/items/scrolls/scroll_clarity.tres) | `scroll.clarity` | Timed Intelligence |
| [Scroll of Weakness](../scenes/items/scrolls/scroll_weakness.tres) | `scroll.weakness` | Timed loss of Strength and speed (harmful) |

## Access keys and picks

Six typed keys live in [`keys/key/`](../scenes/items/keys/key/): arcane crystal, bone skull, bronze ornate, gold treasury, iron prison and silver crypt. Floors currently lock doors with the gold, silver and arcane crystal keys (see [door access](door-access.md)); matching keys are guaranteed outside their locks. The [Thief's Lockpick Set](../scenes/items/tools/tool/tool_lockpick_set.tres) opens any lock as a rare consumable alternative. Keys and picks are spent one per successful unlock and work through door interactions, not quick slots.

## Portable lights

Six `PortableLightItem` definitions in [`tools/lamp/`](../scenes/items/tools/lamp/) light the hero's way from the off hand: a candelabrum, a tallow candle, an oil lamp, a brass lantern, an iron brazier torch and a wooden pitch torch. They are spawned from the debug menu (**Items → Lamps and torches**) and stay out of normal loot and quality rolls; fuel and manual ignition are not designed yet. See [portable-light equipment](stats-and-itemization.md#portable-light-equipment).

## Deferred definitions

These remain valid inventory definitions and can be inspected deliberately, but are excluded from normal loot and the debug spawn menu. They have no equip or use capability until the required mechanic exists; tools are plain `Item` Resources.

| Definition | Missing mechanic |
| --- | --- |
| [Silver Ritual Bell](../scenes/items/tools/tool/tool_bell_silver.tres) | Noise/attention interaction and use rules |
| [Iron Grappling Hook](../scenes/items/tools/tool/tool_grappling_hook.tres) | Traversal anchors and movement action |
| [Brass Tooled Horn](../scenes/items/tools/tool/tool_horn_bugle.tres) | Noise/attention interaction and use rules |
| [Gilded Looking Glass](../scenes/items/tools/tool/tool_mirror_hand.tres) | Reflection/inspection interaction |
| [Dwarven Pickaxe](../scenes/items/tools/tool/tool_pickaxe_dwarven.tres) | Mineable terrain and tool actions |

## Attack animation coverage

All 80 weapon definitions assign an attack style and a KayKit grip adapter. Player profiles select distinct one- and two-handed slash, chop, thrust, crossbow and casting clips; monsters use actor-specific melee and ranged mappings. Timing and interruption are shared with the attack executor. See [attack styles, rig profiles and timing](weapon-attack-system.md#attack-styles-rig-profiles-and-timing).

All non-ranged weapons assign contact profiles with authored reach and geometry. Optional slash and chop swooshes trace editable blade markers on the shared contact timeline; gameplay volumes keep independently authored geometry. Thrusts omit them. See [melee contact](weapon-attack-system.md#reliable-melee-contact). Bow authoring has a profile entry but no active bow definition.

## Equipped orientation

Original KayKit item scenes define the attachment coordinate system. Keep the grip at the local origin, the shaft or blade pointing along +Y, and the blade's broad face in the XY plane (thickness along Z). Single-sided axe heads extend toward -X. Shields use X for width, Y for height, and +Z for the decorated front. These are **item-local axes**, not world axes: the character's existing bone pose determines the equipped direction.

The custom melee weapons, staffs, wands, and shields follow those axes and use identity wrapper transforms. The custom crossbow is authored with its stock along +Y, bow span along Z, and vertical axis along X; its wrapper maps it onto the KayKit axes, preserving the grip origin and scale. Keep such corrections in the gameplay wrapper; do not change the shared attachment bones or rotate items merely to look upright in a still image.

The weapon's `WeaponGrip` Resource (`scenes/items/grips/`) adapts the hand socket to these item-local axes; the melee, off-hand, shield, crossbow and portable-light adapters reproduce the original KayKit prop sockets across player classes.

Use `asset-studio/godot/render_held_items.gd` to render definitions equipped on a frozen player pose from four views, so comparisons share the same attachment frame. Floating catalog thumbnails cannot verify this contract.

## Inventory icon

Inventory slots and the HUD's hand and quick slots show each item's world model through one [item icon](user-interface.md#theme) scene. The automatic pose relies on the item-local axes above: models stand +Y up and face +Z, and weapons hold their grip at the origin. Long weapons lie on the slot diagonal, head top-right; flat items (scrolls, shields) show their broad face; everything else keeps its authored up with a slight turn. When a model still reads badly, add an `ItemIconPose` marker to its scene, placed like a camera (View > Align Transform with View), instead of rotating the model and breaking its equipped orientation. The crossbows carry one, and so do the held lights, which stand +X up for their grip.

## Verification

`ItemCatalogTest` validates every definition through the actual factory and codec, every model wrapper and pickup size, active and deferred loot coverage, category budgets, hidden-family appearances, readable scrolls and casting strategies. `LootTableDepthTest` checks group eligibility and that supply selection survives new weapon variants. Definition, identity and table changes alter seeded baselines; bump `SaveGame.CurrentVersion` rather than migrating saves.
