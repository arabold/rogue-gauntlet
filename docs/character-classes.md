# Character Classes

Status: current-system documentation. Future ideas, where included, are proposals rather than implemented behavior. See the [documentation index](README.md) for related systems and implementation plans.

Playable classes give each run a distinct starting identity: a different hero model (built in
the Asset Studio on a KayKit adventurer's head), attribute spread, stat-derivation profile, and starting gear. The class is chosen on
the character-select panel after picking a save slot for a new game, stored in the save file,
and re-applied every time the player spawns (new game, load, stairs).

## Data model

Everything is designer-authored resources under `scenes/player/classes/`:

- **`CharacterClass`** (`CharacterClass.cs`, one `.tres` per class): `Id` (stable, stored in
  saves; never rename), `DisplayName`/`Tagline` (selector text), `CharacterScene` (the
  `.glb`), `StatProfile`, the four base attributes, `StartingItems`
  (`InventoryItemSlot` list; auto-equip lets the last item of a slot win, so the intended
  main-hand weapon goes last), `StartingWeaponScale` (worn starting weapons), `PreIdentifiedTypeIds`
  (item types identified at run start: the hero knows what they packed), `PreviewAnimation`
  (looping selector pose), and `AttachmentNodeNames` (per-model `BoneAttachment3D` names, see below).
- **`CharacterCatalog`** (`character_catalog.tres`): the ordered class list. The first entry
  is the fallback for unknown ids. `GameSession` loads it from `CharacterCatalog.DefaultCatalogPath`.
- **Per-class `StatProfile`** (`stat_profile_*.tres`): how the class's attributes pay off and
  grow per level (see [stats](stats-and-itemization.md#experience-and-levels)).

Base attributes, growth and the profiles' per-point values come from the [balance model](balance-model.md#class-parity),
which keeps the classes equally strong and lists each class's attributes, growth and matchups; `params.py` holds the
numbers and `simulate.py derive` writes them. Every profile grants [mana](stats-and-itemization.md#mana) per
Intelligence, so the pool follows INT, and the Rogue gains double crit chance per Dexterity.

## The shipped roster

| Class | Identity | Starting gear |
|---|---|---|
| Barbarian | Strength; cleaves once it finds a two-handed weapon | One-handed axe, Barbarian Harness and Bear Hood (medium) |
| Knight | Sword and shield behind heavy armor | Sword and shield, Knight Armor and Knight Helmet (heavy) |
| Rogue | Dexterity crits, crossbow openers, quick feet | Crossbow, dagger and two lockpicks, Ranger Coat and Rogue Hood (light) |
| Mage | Intelligence; homing wand missiles and Fireball | Wand of Fireball and a pre-identified small healing potion, Mage Coat and Wizard Hat (light) |

All starting equipment is first-tier (T1). Starting weapons are worn: Poor quality at a share of their authored damage
(`StartingWeaponScale`), so the first weapon found is an upgrade. Armor starts at Normal quality.

Only the Mage starts with a healing potion, pre-identified through its `PreIdentifiedTypeIds`. The other three classes
start without one and must identify healing potions the normal way: a balance choice, not a limitation of the system;
any class's `.tres` can add `StartingItems`/`PreIdentifiedTypeIds` entries the same way. Wands and staves spend mana on
their spell; any class can equip and cast any of them, so the Mage simply casts more often. Classes do not lock
elements or spells.

## Runtime flow

1. **Menu** (`MainMenu` → `CharacterPanel`): picking a slot for a new game (after the
   overwrite confirm, which is lossless until the first save) opens the carousel — a
   `CharacterPreview` SubViewport turntable playing each class's `PreviewAnimation`, plus
   description and stat readout. The turntable faces each newly shown class toward the camera
   for a moment before it turns. Confirm calls `GameSession.StartNewGame(slotId, classId)`.
2. **Session** (`GameSession`): resolves the id against the catalog
   (`ResolveCharacterClass`; unknown/empty → default class), pre-identifies the class's item
   types in the run's identification, and stores `CharacterClassId` in the `SaveGame`. `LoadGame`
   re-resolves from the save.
3. **Spawn** (`Player._EnterTree`): when `GameSession.ActiveCharacterClass` is set, the
   player applies the class's attributes/profile (`ApplyToStats`), builds the starting
   inventory, and swaps the visual model. With no active class (running `main.tscn`
   directly) the player keeps its authored stats and gear and wears its `DefaultClass` model.

## The model swap

Each class model is a Studio hero (`assets/custom-heroes/<class>.glb`, see the Studio's
[class heroes](../asset-studio/README.md#class-heroes)). It is a bare body in a tunic, shared by
every class, with KayKit's plain gloved arms and its legs, pants and boots, under the class's own KayKit head. The Studio exports it on the KayKit class template, so all heroes
share one rig, bone names and animation names. `Player.ApplyCharacterModel` therefore simply replaces the
`Pivot/Character` instance during `_EnterTree` (before any child `_Ready`), and the authored `AnimationTree`
(`root_node = "../Pivot/Character"`) binds to the new model without retargeting.

`CharacterModel` is a static helper shared with the menu preview. It instantiates the scene, applies the player
render layer (mask 4, which the light and highlight cull masks rely on), and strips the built-in prop meshes.

**What the hero wears is equipment** (`Armor.WornScene`, mounted by `BoneAttachmentManager` through
`WornEquipment`):

- **Skinned armor** is exported on the hero rig with the heroes' shoulder hinges. Its meshes move onto the hero's
  own skeleton and bind by bone name, so they bend with the body.
- **Rigid helmets** mount on the head bone.
- **Covered head parts:** a helmet hides the parts it covers (`Armor.HiddenModelParts`: the `_Hair` and `_Ears`
  nodes). Hidden hair shows the `_Scalp` the Studio generated under it.
- **Whole suits:** body armor brings a KayKit class's own arms and legs (the starting suits their class's) and hides
  the hero's plain ones (`HiddenModelParts: Arms, Legs`).
- **Taking a piece off** restores the bare hero.
- **Menu preview:** `CharacterModel.Dress` puts the same starting helmet and armor on the preview.

The catch: each GLB names its attachment nodes differently (`1H_Axe` vs `1H_Sword` vs
`Knife`…), and some models lack nodes entirely (Rogue has no shield node). The class's
`AttachmentNodeNames` dictionary maps `AttachmentType` → node name;
`CharacterModel.ResolveAttachments` feeds the resolved nodes to `BoneAttachmentManager`.
Gear without a node still equips and applies stats; it just shows no mesh (logged as a
warning). Helmets and body armor need no node: they go on the skeleton or the head bone. These names are
load-bearing strings: `CharacterModelTest` fails if a GLB re-export or importer change breaks them.

**`Weapon.IsTwoHanded` picks which `AttachmentType` (and therefore which bone) a weapon
resolves to**: `OneHandedWeapon` vs `TwoHandedWeapon` in `BoneAttachmentManager.GetAttachmentType`,
e.g. the Mage's `1H_Wand` vs `2H_Staff`. A wrong flag does not error; it silently attaches the
model to the wrong bone. `IsTwoHanded` is also a gameplay lever: `Inventory.Equip` unequips the
shield slot when a two-handed weapon goes on (and vice versa). When authoring a new weapon, check
its model size against sibling items on the same bone (see the `godot-mcp` skill's in-hand
verification section).

Import detail: Idle and locomotion loop modes are per-GLB import settings (`_subresources` in each
hero's `.glb.import`); `CharacterModel` also loops any Run or Walk clip that imports without one.
Without a loop an animation plays once and freezes.

## Adding a class

Follow the [Asset Studio character workflow](../asset-studio/character-workflow.md#rig-and-fit-for-the-assets-role) for a new hero model or replacement suit. Verify coverage and attachment names on the actual class model.

1. Author `stat_profile_<id>.tres` and `class_<id>.tres` (mind `AttachmentNodeNames`: read
   the node names out of the model's imported scene) and add the class to
   `character_catalog.tres`.
2. Add the class to `params.CLASSES` (attributes, growth and kit), write its numbers with
   `simulate.py derive --only classes`, and check `parity` and `survival`.
3. If the model is a new GLB, mirror the loop-mode `_subresources` in its `.import`.
4. Run the import step, then `dotnet test`: the catalog and model tests validate ids,
   completeness, attachments, and the preview animation.
5. Eyeball it in the editor: selector framing, in-game props stripped, weapon in the right
   hand.
