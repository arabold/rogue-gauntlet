# Character Classes

Status: current-system documentation. Future ideas, where included, are proposals rather than implemented behavior. See the [documentation index](README.md) for related systems and implementation plans.

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
- **Per-class `StatProfile`** (`stat_profile_*.tres`) — how the class's attributes pay off and
  grow per level (see [stats](stats-and-itemization.md#experience-and-levels)). The values come
  from the [balance model](specs/balance-model.md); the Rogue doubles `CritChancePerDexterity`.

## The shipped roster

| Class | STR | DEX | VIT | INT | HP | MP | Growth per level | Starting gear |
|---|---|---|---|---|---|---|---|---|
| Barbarian | 12 | 8 | 10 | 5 | 50 | 45 | STR 0.45, VIT 0.25 | Battle Axe (T1, 2H) |
| Knight | 9 | 8 | 10 | 6 | 50 | 50 | STR 0.3, VIT 0.3 | Sword + shield |
| Rogue | 9 | 15 | 11 | 8 | 52 | 60 | DEX 0.5, VIT 0.25 | Crossbow + dagger + two lockpicks |
| Mage | 6 | 10 | 10 | 15 | 50 | 95 | INT 0.5, VIT 0.3 | Wand of Fireball (T1) + healing potion (pre-identified) |

Each class gains one attribute point per level in all, split as its growth says (the table lists the larger shares).
The [balance model](specs/balance-model.md#class-parity) keeps the classes equally strong; `params.py` holds the
numbers and `simulate.py derive` writes them.

All starting equipment is first-tier (T1), Normal quality at its authored values; better gear comes from the dungeon.

Only the Mage starts with a healing potion, pre-identified via
`GameSession.Run.Identification.Identify("potion.healing")` (`PreIdentifiedTypeIds` on its
`CharacterClass`). The other three classes start without one and must identify healing
potions the normal way — a deliberate balance choice, not a limitation of the system;
any class's `.tres` can add `StartingItems`/`PreIdentifiedTypeIds` entries the same way.
Every profile grants 5 [mana](stats-and-itemization.md#mana) per Intelligence, so the pool
(MP) follows INT. Wands and staves spend it on their spell; any class can equip and cast any of them, so the
Mage simply casts more often.

## Runtime flow

1. **Menu** (`MainMenu` → `CharacterPanel`): picking a slot for a new game (after the
   overwrite confirm, which is lossless until the first save) opens the carousel — a
   `CharacterPreview` SubViewport turntable playing each class's `PreviewAnimation`, plus
   description and stat readout. The turntable faces models forward toward the camera,
   pausing rotation for 0.5s when a class is loaded or switched before turntable rotation resumes.
   Confirm calls `GameSession.StartNewGame(slotId, classId)`.
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

**`Weapon.IsTwoHanded` picks which `AttachmentType` (and therefore which bone) a weapon
resolves to** — `OneHandedWeapon` vs `TwoHandedWeapon` in `BoneAttachmentManager.
GetAttachmentType`, e.g. the Mage's `1H_Wand` vs `2H_Staff`. Getting the flag wrong doesn't
error; it silently attaches the model to the wrong bone (the Mage's staffs did exactly this
for a while — flagged one-handed, they attached to the wand bone that was never sized/posed for
a full-length staff, until `IsTwoHanded` was corrected to route them to the already-authored
`2H_Staff` bone). `IsTwoHanded` is also not cosmetic-only: `Inventory.Equip` auto-unequips the
shield slot when a two-handed weapon goes on (and vice versa), so it's a real gameplay/balance
lever. When authoring a new weapon, check its actual model size against sibling items on the
same bone (see the `godot-mcp` skill's in-hand verification section) rather than guessing from the
name alone.

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


## Magic and mana

Every class has its Intelligence-sized mana pool, and every wand and staff casts its bound spell from it; the Mage starts with a Wand of Fireball. Classes do not lock elements or spells.
