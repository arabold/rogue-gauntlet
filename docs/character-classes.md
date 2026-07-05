# Character Classes

Playable classes give each run a distinct starting identity: a different KayKit adventurer
model, attribute spread, stat-derivation profile, and starting gear. The class is chosen on
the character-select panel after picking a save slot for a new game, stored in the save file,
and re-applied every time the player spawns (new game, load, stairs).

## Data model

Everything is designer-authored resources under `scenes/player/classes/`:

- **`CharacterClass`** (`CharacterClass.cs`, one `.tres` per class) — `Id` (stable, stored in
  saves; never rename), `DisplayName`/`Description` (selector text), `CharacterScene` (the
  `.glb`), `StatProfile`, the four base attributes, `StartingItems`
  (`InventoryItemSlot` list; auto-equip lets the last item of a slot win, so the intended
  main-hand weapon goes last), `PreIdentifiedTypeIds` (item types identified at run start —
  the hero knows what they packed), `PreviewAnimation` (looping selector pose), and
  `AttachmentNodeNames` (per-model `BoneAttachment3D` names, see below).
- **`CharacterCatalog`** (`character_catalog.tres`) — the ordered class list. The first entry
  is the fallback for pre-class saves and unknown ids. `GameSession` loads it from the
  well-known path `CharacterCatalog.DefaultCatalogPath`.
- **Per-class `StatProfile`** (`stat_profile_*.tres`) — the attribute-derivation seam: e.g.
  the Mage's profile has `DamagePerIntelligence > 0` so spell-capable characters scale damage
  with INT, while the Rogue doubles `CritChancePerDexterity` and evasion gain.

## The shipped roster

| Class | STR | DEX | VIT | INT | HP | Profile twist | Starting gear |
|---|---|---|---|---|---|---|---|
| Barbarian | 14 | 8 | 13 | 5 | 115 | +50% damage per STR | Worn Battle Axe (2H) |
| Knight | 12 | 8 | 14 | 6 | 120 | — | Sword + shield |
| Rogue | 8 | 15 | 9 | 8 | 95 | 2× crit/DEX, 2× evasion/DEX | Crossbow + dagger |
| Mage | 6 | 10 | 8 | 16 | 90 | damage scales with INT | Fire staff |

Every class also starts with one small healing potion, pre-identified via
`GameSession.Identification.Identify("potion.healing")`. There is no mana: casting stays
cooldown-based, and caster/non-caster differences come purely from stat profiles.

## Runtime flow

1. **Menu** (`MainMenu` → `CharacterPanel`): picking a slot for a new game (after the
   overwrite confirm, which is lossless until the first save) opens the carousel — a
   `CharacterPreview` SubViewport turntable playing each class's `PreviewAnimation`, plus
   description and stat readout. Confirm calls `GameSession.StartNewGame(slotId, classId)`.
2. **Session** (`GameSession`): resolves the id against the catalog
   (`ResolveCharacterClass`; unknown/empty → default class), pre-identifies the class's item
   types, and stamps `CharacterClassId` into the `SaveGame` (schema v4; older saves
   deserialize to `"barbarian"`). `LoadGame` re-resolves from the save.
3. **Spawn** (`Player._EnterTree`): when `GameSession.ActiveCharacterClass` is set, the
   player applies the class's attributes/profile (`ApplyToStats`), builds the starting
   inventory, and swaps the visual model. With no active class (running `main.tscn`
   directly) the authored Barbarian setup is used unchanged.

## The model swap

All KayKit adventurers share one rig, bone names, and animation names, so
`Player.ApplyCharacterModel` simply replaces the `Pivot/Character` instance during
`_EnterTree` — before any child `_Ready` — and the authored `AnimationTree`
(`root_node = "../Pivot/Character"`) binds to the new model without retargeting.
`CharacterModel` (static helper, shared with the menu preview) instantiates the scene,
applies the player render layer (mask 4, relied on by light/highlight cull masks), and
strips the built-in prop meshes the GLBs ship with (default weapons, shields, spellbooks),
keeping only the class's hat and cape.

The catch: each GLB names its attachment nodes differently (`1H_Axe` vs `1H_Sword` vs
`Knife`…), and some models lack nodes entirely (Rogue has no shield/hat node). The class's
`AttachmentNodeNames` dictionary maps `AttachmentType` → node name;
`CharacterModel.ResolveAttachments` feeds the resolved nodes to `BoneAttachmentManager`.
Gear without a node still equips and applies stats — it just shows no mesh (logged as a
warning). These names are load-bearing strings: `CharacterModelTest` fails if a GLB
re-export or importer change breaks them.

Import detail: Idle/Walking loop modes are per-GLB import settings (`_subresources` in the
`.glb.import` files). All four adventurer imports carry them; without the override an
animation plays once and freezes.

## Adding a class

1. Author `stat_profile_<id>.tres` and `class_<id>.tres` (mind `AttachmentNodeNames` — read
   the node names out of the model's imported scene) and add the class to
   `character_catalog.tres`.
2. If the model is a new GLB, mirror the loop-mode `_subresources` in its `.import`.
3. Run the import step, then `dotnet test` — the catalog and model tests validate ids,
   completeness, attachments, and the preview animation.
4. Eyeball it in the editor: selector framing, in-game props stripped, weapon in the right
   hand.
