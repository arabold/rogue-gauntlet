# User interface

Status: theme, UI kit, front end, depth card, HUD, floor map, run menu (Gear, Pack, System), death and confirmation implemented. The [UI redesign plan](specs/ui-system-plan.md) holds what remains (the touch HUD, credits, settings).

## Visual language

Storybook fantasy in the manner of recent Zelda remakes, cut to fit the chunky KayKit world: hand-carved warm-brown wood whose edges wander and chip, oversized gold curls that differ from corner to corner, recessed slots that glow ochre when filled and white when selected, a tone-on-tone scroll relief behind full-screen cards, and cream serif titles over curl-ended dividers. The hero stands in a dark oval wherever a screen presents them.

**Four rules shape every screen:**

1. **The game owns the screen.** The depth card and death cover the view. The floor map overlays the live game while held. The run menu is a screen of its own over the paused game.
2. **One grid.** Gaps are multiples of 8 px; screen margins are 32 px for the HUD, 64 px for menus and 128 px for the title column. Every text button is 320 × 64 whatever its label, 16 px from the next; icon buttons are 64 × 64.
3. **Few words.** Labels are one or two words; numbers and icons replace sentences.
4. **Three weights of chrome.** Use bare outlined text and icons for the HUD, a feathered shade or thin outline over the live game, and the carved frame (`FramePanel`) for death and confirmations. The header band titles full-screen lists such as Runs and carries the run menu's tabs. Gold marks what matters (the primary action, a filled slot, a title rule). The selection glow is white.

Fonts ship under `assets/fonts/` with their licenses (SIL OFL 1.1, recorded in [the asset manifest](../assets/ATTRIBUTIONS.json)): **Alegreya** (variable; ExtraBold titles, Bold buttons) for display text and **Alegreya Sans** (Bold, ExtraBold) for body text. The theme asks both for lining figures: their default old-style numerals read as letters in a HUD. The OFL requires each font's `OFL.txt` to ship with the game's credits.

## UI kit

`asset-studio/ui/ui_kit.py` renders every frame, plaque and ornament into `assets/ui-kit/` from signed distance fields lit by one top-left light, supersampled and filtered, so the art is the project's own (CC0) and changes by editing the generator, never the PNGs. Its `WOBBLE`, `CHUNK` and `ASYM` constants set how hand-cut, heavy and uneven the carving is.

| Piece | Use |
| --- | --- |
| `panel_frame` | carved frame with gold inlay and corner curls, translucent interior (9-slice 112) |
| `outline_frame`, `shade` | thin carved outline with small corner curls; feathered dark area (9-slice 64 and 56) |
| `button_*`, `button_primary_*` | wooden and gold plaques with end studs, in normal, hover, pressed and disabled states (9-slice 34 × 26) |
| `slot`, `slot_filled`, `slot_glow` | translucent recessed niche, its ochre filled state and the white selection glow (9-slice 26 and 40) |
| `card`, `ribbon`, `divider` | riveted translucent card, swallowtail tag and scroll-ended rule |
| `header_band`, `backdrop` | horizontally tiling carved header with scalloped curls and 88 px of clear space for a title; tiling scroll-relief backdrop |
| `tab_pennant` | dark swallowtail pennant with a gold hem that hangs from the top edge over the header band and marks the open tab (9-slice 44 sides, 8 top, 70 bottom); its two cloth tones are arguments of the generator |
| `arrow_left`, `arrow_right` | carved gold darts for carousels |
| `icon_*`, `coin` | chunky carved placeholders for the gear slots (helmet, armor, weapon, shield, ring, amulet); the gold "use" hand and "dodge" darts for touch; the gold coin |

The pieces are rendered at 1× for a 1080p canvas, so they soften slightly when the window scales well above that.

## Theme

`scenes/ui/theme.tres` is the project theme (`gui/theme/custom`), so every control inherits it. `asset-studio/godot/build_ui_theme.gd` writes it whole from the kit and the fonts: change a look there and rebuild (the script's header has the command), rather than editing the `.tres`. Screens pick a look with **Theme Type Variation** in the Inspector, never with per-node style, font, size or color overrides: each role has one look and one size everywhere (every plaque button is 320 × 64, every framed title a `TitleLabel`, every stat value a `ValueLabel`). Plain types keep their defaults:

- **Label:** cream body text with a thin ink outline and a soft drop shadow.
- **Button:** the wooden plaque; hover brightens it and its gold rim, pressing sinks it, and keyboard focus draws the white selection glow.
- **PanelContainer / Panel:** a quiet translucent backing without margins, because most panels here are layout wrappers (item slots, HUD groups).
- **PopupMenu, tooltips:** the inset card, padded clear of its corner rivets, with a lit bronze bar on the entry under the pointer or the keys; **embedded dialogs** (Window/AcceptDialog): the carved frame around title and content; **ProgressBar, scroll bars:** flat wood and bronze tones.

| Variation | Base | Use |
| --- | --- | --- |
| `DisplayLabel`, `TitleLabel`, `HeaderLabel` | Label | serif display text at 120, 64 and 44 px |
| `AccentLabel` | Label | gold serif text for values and names |
| `CaptionLabel`, `SmallCapsLabel`, `WarningLabel` | Label | muted body captions; small caps over values; error and status lines |
| `DangerTitleLabel` | Label | red serif title at the `TitleLabel` size (death screen) |
| `TagLabel` | Label | gold ribbon tag |
| `PrimaryButton` | Button | the screen's main action, a gold plaque |
| `ArrowButton` | Button | the kit's arrow as the button's icon, no plaque |
| `CardButton` | Button | card-sized choices such as run slots |
| `FramePanel`, `DangerPanel` | PanelContainer | carved frame with 96 px of padding; the death screen's red-stained frame |
| `ShadePanel`, `OutlinePanel` | PanelContainer | structure over the live game, kept for the touch HUD and unused so far: a feathered dark area with 56 px of padding; a thin carved outline |
| `CardPanel` | PanelContainer | a translucent inset card inside or over other UI |
| `SlotPanel`, `FilledSlotPanel` | PanelContainer | item niches: empty, and lit ochre for what is worn |
| `SlotButton`, `SlotGlowPanel` | Button, Panel | the button over a niche, which draws nothing; the white selection glow, which the slot shows behind its item |
| `TabButton` | Button | a run-menu tab on the header band; the open one sits on the pennant, which its stylebox draws past the band's scallops |
| `ValueLabel`, `BonusLabel` | Label | a stat's value in the serif; the gold bonus gear adds to it |
| `CountLabel` | Label | numbers on the game: bar values, stack counts, costs |
| `WorldLabel`, `CombatWorldLabel` | Label | floating text in the menu buttons' Alegreya serif: one size for item and enemy names, prompts, notices and objectives (32 px); combat numbers, larger because they vanish quickly (64 px) |
| `HealthBar`, `ManaBar` | ProgressBar | colored bar fills |
| `ThinBar`, `CooldownBar` | ProgressBar | a hairline gold bar (XP, small cooldowns); a dark veil that drains off a slot |

Enemy nameplates use one continuous frame around the HUD's red health fill. For enemies with health phases, straight vertical gaps divide the fill at the authored health thresholds. The segments drain from right to left as health falls. Enemies with visible poise have a thin gold meter aligned under the health bar.

Native 3D text is a `WorldLabel3D` (`scenes/components/`): a `Label3D` takes no Control theme, so it reads its font, size and outline from the world-label variation its `ThemeTypeVariation` names, in the editor too, and keeps them read-only. Every world label draws at one pixel size (0.01 m), as the prompt's viewport sprite does, so text of one variation stands the same size everywhere: a 32 px `WorldLabel` is 0.32 m tall.

**Item icons** are the items' own models, shown by one scene in every slot (`scenes/ui/item_icon.tscn`, `ItemIcon`: run menu, HUD hand and quick slots). The icon is a little larger than its niche by its `Overhang`, so the model reaches over the edge, and its `Preview` viewport renders at twice the shown size so it stays sharp when the window scales up. `item_icon.gdshader` draws it with a soft dark halo around the silhouette, which stands it off the niche, a worn slot's gold or the game behind the HUD.

`HSeparator` draws the scroll-ended divider. `LoadingPips` (`scenes/ui/widgets/`) draws a row of hopping gold diamonds.

## Front end

`MainMenu` lays its screens over `MenuStage` (`scenes/menu/stage/`): the dungeon's entrance room, assembled by the game's own `RoomAssembler` from a fixed seed, lit like a floor and seen from the game's diagonal, with a hero standing where a run begins and embers drifting through the air. The hero is a `HeroShowcase` (`scenes/player/showcase/`): the class's model wearing its starting kit through the game's own `BoneAttachmentManager`, idling and every few seconds doing something. On the stage that is one of its `Poses` (`ShowcasePose`: a clip, or several in turn) picked at random: a cheer, a look at something in its hand, a reach, picking something up, sitting down for a moment. Without poses, as in the run menu's Gear tab, it shows off what it carries instead: the held weapon's attack at that weapon's speed, a guard with a shield, a cheer otherwise. It follows an inventory, so equipping something shows at once with that item's flourish; it is never a `Player`. The stage keeps the hero right of center; the screens keep to a column on the left over a soft shade. The stage camera uses its own native `menu_camera_attributes.tres` preset: moderate near/far depth of field keeps the hero and starting equipment in a sharp depth band while the softened room props remain recognizable. Tune **MenuStage → Camera3D → Attributes → DOF Blur** in the Inspector; this is independent of the gameplay elliptical screen blur and does not alter exposure, lighting or the overlaid interface. Retune focus distances if changing the stage camera distance or pitch.

- **Title:** the logo and one button column. The logo (`assets/ui-kit/title_logo.png`, from `asset-studio/ui/title_logo.py`) is a small ROGUE over a large GAUNTLET in chiselled gold with a sword standing behind the G: the display font's own letters turned into distance fields and lit like the kit, rendered at twice its shown size. **Continue** resumes the most recently saved run in progress and names it; without one it is hidden and **New Run** becomes the primary button. **Runs** appears once a slot holds a save.
- **New Run** opens the hero picker, and the run takes the first free slot. Only when all four slots hold runs does it then open Runs to choose one to replace, behind the confirmation; backing out returns to the hero.
- **Runs:** the four slots as `RunCard`s under the header band, each with the run's hero, class, depth and level and, in small type, when it was last saved. A run in progress continues; a fallen one opens its record on the death screen; a free slot starts a run there. A save from another version reads "Old save" and counts as free.
- **Choose Hero** (`CharacterPanel`): arrows either side of the class name, its `Tagline`, one row of stats, Back and Start. The stage shows the selected class.

## In-run screens

- **HUD** (`Hud`): bare readouts, one job per corner; it steps aside while the run menu is open. Top left: level badge, health and mana bars with their values, a hairline XP bar. Top right: the floor's depth and gold. Bottom center: the message feed, three centered lines. Bottom right: the two hand slots and four quick slots with their key glyphs; a cooldown drains off its slot as a dark veil. The interaction's progress sits just below the hero. The bottom-left corner stays empty for a touch screen's steering stick, and the debug menu floats top center without reserving space.

- **Floor map** (`FloorMapOverlay`): hold **Tab** to see an unframed map over the live game. The map follows the gameplay camera's rotation and fits the current floor's [explored walkable tiles](level-design.md#map-occlusion-and-fog-of-war) to the screen with a light translucent fill and hand-drawn walls. Fixed partitions such as prison cell walls use the same thin strokes and appear as their tiles are explored. The fill fades at open edges that lead into unexplored space, which stays blank until explored. A copper stroke shows each discovered closed door and disappears when it opens. A mint ring tracks the hero, blue arrows mark discovered stair landings, amber padlocks mark locked doors, and gold diamonds mark loose loot and unopened treasure chests in explored space.
- **Depth card** (`LoadingScreen`, `scenes/ui/loading/`): an autoload on canvas layer 128, so it survives scene changes and covers everything. It shows the destination ("Depth 3"), a divider, one tip from its exported `Tips` (seven words at most, starting at a random one and then cycling) and the pips. `GameSession` drives it for every floor load: starting or loading a game, and taking the stairs. `Cover` fades the card in and completes only once it has been drawn; the session then changes or reloads the scene. Loading `main.tscn` and generating the floor block the main thread, so the card holds still during that time instead of showing a stalled animation. The hero's arrival saves the run behind the card, then `Reveal` fades it out over the floor. A death while the card comes up reveals the floor the hero left; a floor that fails to generate returns to the menu, which dismisses the card. See [runs, saving and permadeath](run-lifecycle.md).
- **Run menu** (`RunMenu`, `scenes/ui/menu/`): one full screen over the paused, darkened game, with four tabs (Gear, Pack, Notes, System) on the header band beside the gold count; a keeper's trade shows Buy and Sell there instead. The inventory key opens Gear and the pause key System; Q and E turn the tabs; a dead hero cannot open it. Each tab is a `RunMenuTab` that says what it offers for the selection as `MenuPrompt`s. The menu draws them twice and routes the choice back to the tab. The key row bottom right holds the keys that act at once (Off Hand, Drop, the quick slots). Choosing a slot, with the accept key or a click of either button, opens a popup on it listing everything that can be done, the usual choice first and under the keys, so nothing happens before the player has seen what will. Where there is one thing to do (an empty worn slot, a scroll's target) choosing does it. Pointing at a slot selects it, as the arrow keys do, and the selected item's name and lines replace hover tooltips; they sit in a block of fixed height, so nothing moves as the selection changes.
  - **Gear** (`GearTab`): left, the seven worn slots in the hero's shape (helmet; main hand, armor, off hand; ring, amulet, ring), each empty one showing its carved icon, a two-handed weapon filling both hands; beneath them the four attributes with what gear and buffs add in gold, then damage, armor and crit. Center, the hero as a `HeroShowcase` on the run's inventory, so it wears what is equipped and shows new gear off. Right, every wearable thing in the pack, worn ones lit. Standing on a worn slot dims what does not fit it, and only that: the pointer crosses slots on its way to the list. **Choose** on an empty slot moves to the first thing that fits, and gear equipped from there goes into that slot. Otherwise gear goes where it belongs. Prompts: Equip or Unequip; for what either hand can hold, Main Hand and Off Hand, and once it is worn Unequip and the other hand, so a weapon changes hands without coming off; Drop for what is not worn.
  - **Pack** (`PackTab`): left, one niche per place in the pack with its count, and the four quick slots; right, the selected item large with its name, stack count and lines. Prompts: Drink, Read or Eat (or Equip, with the same hand choice as Gear), a quick slot by its number key, the popup or a click on the quick slot, Drop. A consumable takes a moment to use, which the paused game cannot play, so using one closes the menu and the hero does it in the world. A scroll that needs a target turns the grid into a choice: what it cannot act on dims, the banner under the header says what it asks for, and choosing an item is the answer.
  - **Notes** (`NotesTab`): the run's log, every message the hero has seen, kept with the save.
  - **System** (`SystemTab`): Resume and Save & Quit.
  - **Buy** and **Sell** (`BuyTab`, `SellTab`): a keeper's trade opens the run menu on these tabs with the run's own tabs hidden. They share the Pack's layout (`ItemGridTab`): left, the keeper's shelf or the pack; right, the selected item with a price line. See [shops](shops.md#trading) for the trading rules.
- **Death** (`DeathScreen`): the one carved frame in a run, naming the class and depth with level, gold and time beneath. The menu shows the same screen for a fallen run's card.
- **Confirmation** (`ConfirmDialog`): a carved frame with a short question, what is at stake, and two buttons; the safe answer holds the focus.

The debug menu still carries its earlier per-node styles and picks up only the theme's fonts and defaults.
