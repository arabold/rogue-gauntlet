# Balance model

**Status: realm 1 implemented.** This defines where the game's balance is going and how every number is derived.
Realm 1 follows it; the [implementation plan](#implementation-plan) lists what the game already takes from it and the
remaining steps. The simulator in
[`scripts/balance/`](../../scripts/balance/) implements these formulas, and its
[`params.py`](../../scripts/balance/params.py) holds every number. This document explains them; when a number here
and the file disagree, the file wins.

## Goals

- **Hard, like Rogue, NetHack and Shattered Pixel Dungeon.** Most new players die on depths 1–3, beating the first
  gatekeeper takes several runs, and even experts lose most late runs.
- **One currency.** Every danger and every defense is measured in the hero's health, so armor, weapons, spells,
  monsters and supplies trade against each other.
- **Classes differ in style, not strength.** At equal skill every class survives as deep as the others, while each
  has fights it handles better or worse.
- **Content derives from the model.** Each tier, slot and monster archetype gets target values. New items, spells and
  monsters can be checked against the model before anyone plays them.

## Difficulty targets

Skill is the one input the game cannot know, so the model plays four players. *Avoid* is the share of monster attacks
they avoid by dodging, guarding or spacing. *Efficiency* is the share of their own attacks that land. They differ in
when they drink potions and how many found potions they can identify.

| Skill | Avoid melee / ranged / homing | Efficiency | Arrival odds: target | Simulated, 3,000 runs (class range) |
| --- | --- | --- | --- | --- |
| Novice | 51% / 35% / 27% | 29% | depth 2: 75%, 3: 35%, 4: 3% | 72% (61–94%), 31% (15–68%), 1% |
| Regular | 67% / 55% / 43% | 36% | depth 4: 40%, 7: 10% | 36% (32–43%), 14% (11–18%) |
| Skilled | 79% / 69% / 55% | 41% | depth 4: 85%, 13: 35%, win: 3% | 83% (81–86%), 34% (28–40%), 1% |
| Expert | 88% / 80% / 67% | 44% | depth 13: 85%, win: 35% | 89% (87–91%), 36% (29–44%) |

"Arriving at depth 4" means beating the depth-3 gatekeeper; "win" means beating the depth-24 one. The skill values are
preliminary (see [calibration](#calibration)): *skilled* is one developer's measured arena profile, and the other tiers
keep their earlier spacing. Players avoid most attacks by moving, but many of their own swings miss, so efficiency is
low at every tier.

## Combat rules the model assumes

- A hit rolls uniformly in its damage range; a critical hit doubles it.
- **Armor** absorbs a uniform roll in `[0, armor]` from every hit, as implemented. A hit `h` against armor `A` deals
  `h − A/2` on average when `h ≥ A`, and `h²/(2A)` below that. Flat armor is strong against many small hits and weak
  against a few big ones: it trivializes swarms, matters less against gatekeepers, and would crush multi-dart
  missiles.
- **Magic ignores armor.** Elemental damage (missiles, spells, burning) skips the armor roll, as wand zaps do in
  Shattered Pixel Dungeon, for heroes and monsters alike. Otherwise a wand's three small darts lose most of their
  damage to an armored gatekeeper, and no Mage tuning reaches parity there. Monster casters' missiles and gatekeepers'
  area attacks therefore ignore the hero's armor too, so the caster's damage is set low.
- **Attributes scale; levels and gear grow.** Each attribute changes a shared curve by a
  percentage, so a class's advantage keeps its size at every depth. Flat per-point bonuses fade as levels add health
  and gear adds damage, so a class balanced at depth 1 drifts out of balance by depth 24.
- **Class and armor bonuses remove a share of the hits that skill lets through.** If they added to the avoid chance
  instead, the same +5% footwork would be worth far more to an expert than to a novice.

## The hero

The hero arrives on each depth at about that level: the [XP ladder](#experience) is fitted to make this true. Every
class follows the same curves:

- **Health** `= (50 + 7 × (level − 1)) × (1 + 4% × (Vitality − 10))`.
- **Strength:** +3% physical damage per point above 10. **Dexterity:** 1% crit chance per point (2% for the Rogue).
  **Intelligence:** +3% missile and spell damage per point above 10; mana `= 20 + 5 × Intelligence`, regenerating
  1% of the maximum per second.
- Each level adds one attribute point, split by class (`growth` in `params.py`).
- **Supplies:** healing potions heal fixed points, so a size is worth less as the hero grows and the next size takes
  over: small 30 (60% of a level 1 hero, dropping until depth 12), medium 85 (from depth 9), large 200 (from depth
  17), equally likely while eligible. The hero finds 0.45 per floor at depth 1, rising to 1.0 at depth 24, as measured
  with `supply`. Healing potions are only about a quarter of the potions found, so their bottle stays a guess until
  one is drunk or identified. Food heals 3, 5 or 8 by its portion (small: fruit and drinks; medium: bread and cheese; large: meat), about one per floor; three health
  fountains per run. Health never regenerates on its own. Classes start without potions (today only the Mage starts with one).
- **Harmful supplies:** poison takes fixed points as healing gives them, in five ticks over 10 s: a poison potion
  takes half of what the healing potion of its size gives (15, 40, 100), spoiled food twice what its portion heals
  (5, 10, 15), so a bad bite costs more than a good one gives. Any healing potion cures poison and blindness.
  Blindness lasts 30, 45 or 60 s and confusion 15, 20 or 30 s by potion size, long enough that a pack finds the hero
  before they pass (Rogue blinds for hundreds of turns and confuses for about twenty). The survival simulation does
  not charge them yet.

### Gear

The expected hero wears gear that is on curve for its depth, plus a quality and affix bonus that grows from 0% at
depth 1 to 25% at depth 24. Every class starts with worn (Poor) weapons at 70% of a tier 1 weapon's damage, so the
first weapon found is an upgrade; the expected hero's weapon climbs back to the curve over depths 1–4 as found
weapons replace them. The Barbarian starts with a one-handed axe and wields its class's two-handed axe from depth 4,
once the starter is fully replaced.

- **Weapons:** the reference one-handed sword's mean hit is `6 × 1.08^(depth − 1)` with a ±40% range. Other kinds are
  multiples of it: dagger 0.72× every 0.36 s; sword 1.0× every 0.5 s; two-handed axe 1.5× every 0.72 s, clipping a
  second engaged monster 15% of the time; crossbow 1.0× every 0.95 s at range; wand 3 homing darts of 0.16× every
  0.6 s. Ranged kinds hit less because the hero kites: it runs at 7.5 m/s and no monster faster than 4.2, so a hero
  whose main weapon is ranged backs off between shots and avoids another `kite` share of the melee hits its skill lets
  through (novice 20%, regular 40%, skilled 50%, expert 60%; unmeasured). The wand is set for parity with kiting; the
  crossbow so a Rogue who never draws the dagger loses about 15% more health than one who opens with it. The kinds no class starts with share the sword's damage per second when one-handed (axe 1.15×, blunt 1.3×:
  bigger, slower hits that cut through armor) and add cleave when two-handed (sword, axe, blunt); spears trade the off-hand
  for a larger hit, and a staff fires four missiles for a volley 1.5× a wand's. Their exact multiples are proposals in `params.py`.
  Wood is a terrible weapon: wooden melee weapons (practice swords, clubs, mallets: the wooden roll table) deal and
  stagger 20% of their kind and tier, 1–2 damage at tier 1; crossbows, wands and staves are wooden by nature and keep
  full strength.
- **Hit reactions** (`POISE`): monster attacks commit halfway through their wind-up, after which only a poise break
  stops them. A hit removes 10 poise times its weapon kind's hit share, blunt weapons 1.3× more (dagger 7, sword 10,
  two-handed axe 15, two-handed blunt 23, a wand dart 1.5); monsters hold 2 (fodder, shooters, casters), 3
  (standard), 4 (brutes) or 8 (gatekeepers) such hits, rats none. Each break grows the next meter by half for 10 s.
  Only two-handed blunt weapons push on a stagger. The model does not count staggers or lost attacks yet; playtests
  measure them as uptime.
- **Armor slots** are sized against the standard monster's mean hit `m` at that depth. A Normal medium body piece
  holds `0.30 m` armor, a helmet `0.15 m`, a shield `0.22 m`. Light pieces carry half the armor and remove 5% of
  landed hits; heavy pieces carry 1.3× and let 10% more land. The game has no passive evasion, so it expresses that footwork as movement speed: a light body piece moves its wearer 5% faster, a heavy one 8% slower.
- **Curses:** a quarter of dropped equipment is cursed. A curse's hidden penalty takes points, like the bonuses beside
  it (damage, armor, health, Strength, Dexterity), in tiers 1–5 like the gear, each up to twice what a ring of its tier
  gives (Dread, on damage, three times: its strongest roll takes over half an average hit); the speed penalty is 10–15%, since speed is a percentage everywhere on gear. The model does not count curses, since a careful player avoids most of them.
- **Jewelry** comes in tiers 1–5 that drop in the same depth bands. At its tier's on-curve depth a ring adds 12% of
  the standard monster's mean hit as armor (Protection), 10% of the hero's health (Health), 20% of the reference
  weapon's range to both ends of the hero's damage (Savagery), or attribute points (2 at tier 1, one more per tier).
  Amulets give 1.5 times as much; a Ring of Frailty takes 20% of health. Swiftness, Lethargy and Dread change speed
  or damage by a percentage and have no tiers. Jewelry counts toward the found bonus, like quality and affixes.
- **Tiers T1–T5** are introduced at depths 1, 6, 11, 16 and 21, drop for 8 depths and are on curve one depth after
  introduction. `simulate.py gear` prints the authoring targets: T1 sword 4–9, T3 sword 8–20, T5 sword 18–42; T3
  heavy body armor 11, and so on.
- **Spells** scale off the focus's hit like missiles. The baseline Fireball deals 1.3× to about 2.2 monsters for 24
  mana.

## Monsters

Monsters never scale at runtime. An authored monster keeps its stats wherever it spawns; deeper floors spawn monsters
authored for deeper depths. A realm authors its ordinary monsters once, for its middle floor (realm 1: depth 2), and
its gatekeeper for its last. The model fights and prices every floor's monsters at their realm's depth, so a realm's
first floor holds fewer, relatively stronger monsters and its last more, weaker ones, for the same budget. Authoring targets come from the **standard monster** at depth `D`, built against the
reference hero:

- **Health** `= hits_to_kill(D) ×` the on-curve reference sword's mean hit after the monster's armor (10% of that
  hit), but for every archetype except the gatekeeper at least 1.25 average blows of the heaviest class weapon or
  opener (`min_hits`), so no class kills ordinary monsters with one routine blow. With worn starters the floor binds
  only from depth 2; on depth 1 the sword-sized health already exceeds it.
- **Mean hit** `= hero health / (hits_to_die(D) × (1 − hero armor share / 2))`, so the reference hero dies after
  `hits_to_die` landed hits. One attack every 2.2 s, ±35% range.
- **The difficulty dial:** `hits_to_kill` rises from 2.2 to 3.2 landed hits (misses come on top) and `hits_to_die`
  falls from 9.6 to 4.4 across the run.
  Monsters therefore outgrow the hero slowly, and later floors are deadlier at the same skill.

Archetypes are multiples of the standard monster at the same depth:

| Archetype | Health | Armor (share of hero hit) | Damage | Attack every | Wind-up | Notes | Threat level vs depth |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Fodder | 0.55 | 0 | 0.325 | 2.0 s | 0.35 s | fast; soft bites armor mostly stops | −10 to −6 |
| Swarmer | 0.45 | 0 | 0.35 | 1.9 s | 0.25 s | 35% venom worth 1.2 bites, passing armor | −7 to −4 |
| Standard | 1 | 0.10 | 1 | 2.2 s | 0.35 s | | 0 |
| Brute | 1.7 | 0.35 | 1.3 | 2.5 s | 0.45 s | guards 25% | +3 to +4 |
| Shooter | 0.8 | 0.05 | 0.95 | 3.2 s | 0.63 s | ranged | +2 to +3 |
| Caster | 0.9 | 0 | 0.35 in 3 darts | 3.3 s | 0.6 s | heals an ally 30% every 9 s; darts ignore armor | +2 to +3 |
| Gatekeeper | 6 | 0.35 | 1.45 | 2.4 s | 0.51 s | guards 25%, ×1.25 below 65% and ×1.55 below 30% health, 35% area attacks, which ignore armor | +12 to +13 |

Wind-ups are the time from an attack's start to its contact. `derive` sets the primary attack's `Duration` so its clip's
contact lands there. Rats bite in 0.25 s and skeletons strike in 0.35 s, the Normal tier's floor being 0.25 s: Diablo II
runs at 25 frames per second and its heroes swing in 15–20 frames, so a quick monster tell is a fraction of that.
Shooters, casters and the gatekeeper keep their longer, authored tells.

## Cost, threat level and XP

Cost turns any monster into one number in the hero's currency. The **duel cost** of monster `m` is the share of the
hero's maximum health it takes before it dies:

`cost = monster attacks that land before it dies × damage per attack after armor and avoidance / hero max health`

Blows land one at a time, as in the game: the hero's first 0.15 s after the two meet (a swing's contact), then one
per swing that lands; the monster's first its archetype's wind-up after, then one per attack cycle. The chance the
monster is still alive after each of the hero's blows follows the weapon's roll and crits, so a monster felled by the
first blow costs nothing, and a slow, heavy weapon is worth what its blows achieve, not its damage per second. The
survival simulation plays fights with the same timing. An ordinary melee monster starts only half of those attacks,
the first included (`uptime`): the hero's footwork, hits and guard keep the rest from starting. Shooters, casters and
gatekeepers attack on every cycle. Playtests measured 10–43% before monster attacks committed and hits stopped
pushing monsters back, so 50% is a preliminary allowance for those rules. Ranged heroes get free shots while melee monsters close the
4 m gap; melee heroes take extra shots while walking up to shooters. Healers add what they heal, burned through again, while an ally keeps attacking.

- **Cost units** `u(m, D)` = the duel cost against the reference hero at depth `D` ÷ the standard monster's. Floors
  spend their encounter budget in these units.
- **Threat level** = the depth where `u = 1`: the depth at which `m` is exactly a standard fight. As the hero grows, a
  fixed monster gets about 22% cheaper per depth, so threat levels outside depths 1–24 extrapolate at that rate.
- **XP** `= 10 × 1.15^(threat level − 1)`, doubled for gatekeepers. XP grows more slowly than danger (1.15 vs 1.27 per
  level), so out-of-depth weaklings pay little but tough monsters are not exponential jackpots. A standard monster
  pays 10 XP at depth 1 and 47 at depth 12; the depth-3 gatekeeper pays about 160.

`derive` writes each monster's threat level into its definition, and the game pays XP from it through the level's
`ThreatProfile` (`scenes/levels/encounters/threat_profile.tres`, whose constants `derive` also writes). The `Boss`
archetype marks the gatekeeper bonus. `simulate.py monster` evaluates proposed stats before they are authored.

## Floors and gatekeepers

A floor spends a budget of 12 standard fights at depth 1, rising to 28 at depth 24. It spends it on **packs**, not
single monsters, so roles appear together. Rats lead: rat packs of three from depth 1 and hordes of five from depth 2,
with fodder pairs, lone standards, patrols (standard and fodder), sentries (a shooter) and, from depth 3, warbands (two
standards and a caster who heals them); guard posts (brute and shooter) and vanguards (brute, standard and shooter)
join from depth 4. Each pack has a depth range in `params.PACKS`. Gatekeeper floors spend 20% of the budget, then the
gatekeeper. The simulator fills a floor as the game does: it draws by weight among the eligible packs the remaining
budget affords, until none is affordable or the floor holds 32 monsters. A floor therefore ends less than its cheapest
pack below its budget, 1–5% on average.

A pack costs `(N + 1) / 2` times its members fought alone (`model.group_factor`): the hero kills them one by one while
the rest keep attacking. The simulator's group fights match that within 10% for swarms, fodder and mixed melee; packs
with a healer or a shooter come out a little lower. The floor budget is sized for these group costs.

The game spends the same budget through the realm's encounter table, whose entries are packs. A monster costs
`cost growth ^ (threat level − depth)` there: one standard fight on the depth of its threat level, 22% less for each
depth deeper. That closed form stays within about 20% of the model's cost at the depths a monster spawns. A pack
spawns together in one room.

Fodder and swarmers hit softly, so a floor holds many of them: about 21 monsters on depth 1 and 24 on depth 2, and
13–22 on deeper ordinary floors, with the budget fitted to the survival targets. At regular skill and without potions,
a floor's packs cost 9% of max health at depth 3 and about 37% at depth 24, and a gatekeeper about 100% and 220%: the
gatekeepers are the walls, and supplies are meant to carry the hero through them.

## Experience

XP to the next level is `a × g^(level − 1)`, with `a` and `g` fitted to the expected XP of cleared floors (90% of
monsters killed). Clearing depth 1 reaches level 2 and clearing depth 24 reaches level 25. The current fit asks for
85 XP from level 1, about 16% more each level, and about 20,000 XP in total. Gatekeeper floors pay 1.7–1.9× an
ordinary floor, the intended bump after each realm.

## Class parity

A class passes when each of these holds at regular skill:

- **Realm cost:** the health lost over a realm, three floors plus its gatekeeper, is within ±10% of the class mean.
- **Matchups:** floors alone or gatekeepers alone stay within ±25%. Classes are allowed favorable and unfavorable
  fights.
- **Survival:** odds by depth track the other classes at every skill. Deaths come from variance, so a frailer class
  needs a lower average cost than a sturdier one to survive as often. Average parity alone is not enough.

| Class | Start STR/DEX/VIT/INT | Growth per level | Kit | Strong against | Weak against |
| --- | --- | --- | --- | --- | --- |
| Knight | 9 / 8 / 10 / 6 | STR .3, DEX .2, VIT .3, INT .2 | sword, shield (blocks 5%), heavy armor | packs, swarms | gatekeepers |
| Barbarian | 12 / 8 / 10 / 5 | STR .45, DEX .15, VIT .25, INT .15 | one-handed axe, then two-handed, medium armor | gatekeepers | — |
| Rogue | 9 / 15 / 11 / 8 | STR .2, DEX .5, VIT .25, INT .05 | dagger, crossbow opener, light armor, 2% crit per DEX | gatekeepers | packs |
| Mage | 6 / 10 / 10 / 15 | STR .1, DEX .1, VIT .3, INT .5 | wand (kites), Fireball, light armor | packs | gatekeepers |

All four currently pass: each realm cost is within about 10% of the mean. On ordinary floors the Knight's heavy armor
shrugs off soft bites and the Rogue's light armor does not (0.67–0.76 and 1.24–1.35 of the mean), outside the matchup
band but as their roles intend. Survival tracks within a few points at regular skill and above. Novices split by armor:
the Knight reaches depth 3 in 68% of runs, the others in 15–23%. Homing missiles recover only 10% of missed attacks
(`HOMING_ASSIST`): with measured hit rates this low, a larger share made the Mage far safer than the other classes.

## Using the simulator

```bash
uv run scripts/balance/simulate.py                  # hero and standard monster per depth
uv run scripts/balance/simulate.py survival         # arrival odds per skill and class, against the targets
uv run scripts/balance/simulate.py parity           # realm, floor and gatekeeper cost per class
uv run scripts/balance/simulate.py gear | monsters | classes | xp
uv run scripts/balance/simulate.py weapon --tier 2 --damage 6-12 --cycle 0.5
uv run scripts/balance/simulate.py armor --tier 3 --slot body --armor 9 --weight heavy
uv run scripts/balance/simulate.py monster --depth 3 --hp 140 --armor 3 --damage 10-18 --guard 0.25 --boss
```

- **Checking new content.** `weapon` compares a weapon with its tier's reference sword. `armor` compares a piece
  with its slot target. `monster` reports threat level, XP and cost units.
- **Checking new mechanics.** A new kind of weapon, spell, status or class rule needs a matching entry in `params.py`,
  or a small formula in `model.py`. Then run `parity` and `survival`. A change that moves one class more than the
  others, or moves the odds off target, is a balance change rather than content.
- **Retuning.** Change the dial, supplies or targets in `params.py`, then rerun `survival` until no target is
  flagged. `survival` and `parity` run each class and skill as its own job on every core, with its own random stream:
  a job's numbers depend only on `--seed`, not on which `--classes` run. `survival --runs 3000` takes about 8 s, and
  the default 300 runs can stray about 5 points, enough to flag a target near the edge of its tolerance.
- **Supplies in the game.** `supply` runs a headless probe (`scenes/debug/balance/loot_supply_probe.tscn`) that
  generates seeded floors and samples every loot source on them, then compares healing potions, healing, food and
  gear per floor with `SUPPLY`. Healing potions are the most common potion; realm 1 gives about one per floor.

### Game content

`audit` compares the game's `.tres` files with the model's targets; `derive` writes the targets into them. Both take
`--only` with any of `monsters,encounters,weapons,armor,jewelry,curses,classes,supplies,statuses,loot` and `--match <path text>`
(repeatable). `derive` overwrites every value it owns; review the result with `git diff`.

```bash
uv run scripts/balance/simulate.py audit                       # everything off target by more than 10%
uv run scripts/balance/simulate.py derive --match skeleton     # write the targets of matching resources
```

- **Monsters** carry an `Archetype` and a `BalanceDepth` in their definition: the role and the depth its stats are
  sized for. `derive` writes health, armor, speed and the read-only `ThreatLevel`. The first damaging action of the
  combat profile takes the archetype's hit and attack rhythm. Further damaging actions keep their damage ratio to it
  and their authored cooldowns, so signature attacks such as the Warden's overhead and tremor come as often as
  designed; the model counts only the first attack, so it underestimates such monsters a little. Every damaging
  attack gets the model's `Commit`, and the poise definition the monster's scene points at gets its archetype's meter,
  break growth and memory. A definition without an archetype is reported and left alone. The `Boss` archetype is the gatekeeper.
- **Encounters** are each realm's packs (`RealmEncounterFactory` entries), while the model's floor costs and XP
  ladder come from `params.PACKS`. Each entry's member scenes name their definitions, and their archetypes match the
  entry to the model pack with the same members. The entry takes that pack's `Weight` and its depth window, clamped to
  the realm's floors, as `MinimumDepth` and `MaximumDepth`; these must match exactly. A realm pack the model lacks, and
  a model pack the realm's depths should hold but the realm lacks, are reported, and so is a member or gatekeeper whose
  `BalanceDepth` is not the realm's middle or last floor.
- **Weapons** get their kind from their attack style (`MagicStaff` decides wand or staff by hands) and their target
  from `Tier`. A combo's average damage and duration scales count toward the hit and the seconds per attack, so
  `derive` writes the base damage range and `PerformDuration` that reproduce the kind's hit and rhythm, and the kind's
  `PoiseDamage` and `Knockback`. Wand and staff missiles get the kind's dart share and poise, and no knockback.
- **Armor** gets its slot from `ValidSlots`, its weight from `Weight` and its target from `Tier` (Normal quality); a
  body piece also gets its weight's `SpeedModifier`.
- **Jewelry** gets its stat from its type (`ring.protection`) and its values from `Tier`. Each type with points has
  one definition per tier (`ring_protection_1.tres` to `_5`); percentage types get tier 0.
- **Supplies:** healing potions (`TypeId` `potion.healing`) get the points of their size (`AppearanceForm`) and food
  those of its `Portion`, spread over their buff's ticks. Every potion pool entry gets its size's depth window, and healing potions its weight.
- **Statuses:** poison potions (`potion.poison`) get their size's poison points and spoiled food (each food's
  `PoisonEffect`, one per portion: `food_poison_small.tres` to `_large`) its portion's, as `DamagePointsPerTick`,
  `Duration` and `TicksPerSecond`. Blindness and confusion potions get their size's `Duration`.
- **Loot** writes the tier depth bands (`tier_intro`, `tier_band`) into `tier_depth_bands.tres`. The weapon, armor
  and jewelry pools drop each entry in its item tier's band, so their entries carry no window to write.
- **Classes** match the model's classes by `Id`. `derive` writes base attributes, and the hero sheet and stat
  profiles that reproduce the model's level-1 health, mana and crit chance, its attribute growth and the percentage
  each attribute point adds.

All items of one kind and tier get the same numbers, so variety within a tier has to come from affixes, guards,
weight or attack patterns.

## Calibration

The model can be no better than its skill tiers. Measure them before tuning numbers further:

- Record combat-arena sessions with a trace: run the arena windowed and fight its monsters (spawn them from the debug
  menu), several players of known experience, a few minutes each.

  ```bash
  /Applications/Godot_mono.app/Contents/MacOS/Godot --path . res://scenes/debug/combat/combat_harness.tscn -- --combat-log=/tmp/me.jsonl
  uv run scripts/balance/simulate.py calibrate /tmp/me.jsonl
  ```

  `calibrate` reports the share of monster attacks the player avoided per kind (melee, ranged, homing, area) and the
  share of the player's own attacks that landed, each with the closest `SKILL` tier. An attack's kind is how its
  contacts arrive, logged when it starts (a projectile that homes in is homing, a cast takes its spell's delivery, and
  a spell without damage has none), and a projectile's or spell's hit counts for the attack that released it. It also
  reports the share of the hero's hits and damage that came from behind or as crits, which the model does not count
  yet, and each archetype's uptime: attacks per second while fighting against the model's rhythm, from a monster's
  first decision or attack to its death. Uptime below 100% means the hero kept attacks from starting; fit
  `ENGAGEMENT["uptime"]` to it. Logs recorded before attacks logged their delivery cannot be read; record them again. Move
  the tiers toward the measured values, then retune the targets with `survival`.
- Check time to kill and damage taken against the standard monster and the gatekeeper in the arena. They should
  match the simulator's duel numbers within about 20%. If not, fix the model's timing (attack cycles, engagement
  gap) first.
- Record whole playtests of the first floors. **Debug → Playtest → Record playtest** in the game, or launching with
  `-- --playtest-log[=PATH]`, writes one JSONL file under `user://playtests/` that follows the run across floors: the
  combat trace plus each floor's monsters and loot, pickups, items used, equipped and identified, healing, XP,
  level-ups and deaths, with a snapshot of the hero on every floor.

  ```bash
  uv run scripts/balance/simulate.py playtest ~/Library/Application\ Support/Godot/app_userdata/Rogue\ Gauntlet/playtests/*.jsonl
  ```

  `playtest` reports per floor the time spent, kills, XP, level, damage taken and healing (in max health), the
  healing potions found by size against the model's supply, and gear found. It then lists every hidden item type
  found (potions, scrolls, jewelry) by its real name, how many were found and used, and how each was identified (used,
  worn, or by a scroll of identify), the cursed items the hero put on, and each timed effect on the hero (how often
  it started, how long it lasted, whether it expired or was cured, and the health poison took; poison also counts as
  damage taken), followed by the same skill profile as `calibrate`. A playtest measures the whole loop the model predicts, so compare its floors with `survival` at the
  matching skill tier.

## Implementation plan

The model stays in Python, so it has one implementation. The game holds the derived numbers and only the short
formulas that use them: XP from threat level, floor budgets from depth, and the XP ladder.

Missing content does not block any step. Targets exist per archetype and gear tier, not per item, so a new monster
is authored by choosing its archetype and depth, and a new item by choosing its kind and tier; `derive` fills in
the numbers. Loot needs only averages to match: potions and food per floor, the tier a hero wears at each depth, and
the bonus from quality and affixes. Item tables are free to vary around them.

`audit` and `derive` exist (see [Game content](#game-content)), and the game follows the model so far:

- realm 1 monsters, class sheets, every weapon and armor piece, and the loot tier bands are derived;
- XP and floor budgets follow the monsters' threat levels, and floors spawn rat-led packs;
- heroes level up on the fitted ladder with percentage attributes;
- elemental damage passes armor, potions and food heal fixed points by size, jewelry comes in tiers, and realm 1
  drops about one healing potion per floor (`supply`);
- body armor and helmets exist in three weights, and every class starts in its own.

The remaining steps:

1. **Realm 1.** Calibrate the novice and regular skill tiers from recorded playtests of the first three floors (see
   [Calibration](#calibration)). Most of the difficulty targets fall in these depths.
2. **Realm by realm.** Each realm gets its monsters, packs and gear tier through `derive`, passes `audit`, and is
   playtested before the next one starts.
3. **Unmodeled systems.** Shops, traps and the rest of [Not modeled yet](#not-modeled-yet) enter `params.py` before
   they are built. A shop, for example, turns gold into supplies and gear upgrades.

## Not modeled yet

Shops and gold, traps, stealth and skipped fights, partial floor clears, the cost of harmful potions, spoiled food, blindness and confusion, and the
gatekeepers' individual attacks. Each gatekeeper is the archetype above. Add them to the model before relying on it
for those systems.
