# Balance model

**Status: plan.** This defines where the game's balance is going and how every number is derived. Leveling, body
armor, helmets and threat-based XP do not exist yet, and today's authored values were made in isolation; the
[implementation plan](#implementation-plan) replaces them with values derived here. The simulator in
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

| Skill | Avoid melee / ranged / homing | Efficiency | Arrival odds: target | Simulated (class range) |
| --- | --- | --- | --- | --- |
| Novice | 51% / 35% / 27% | 29% | depth 2: 75%, 3: 35%, 4: 3% | 76% (71–82%), 30% (25–36%), 1% |
| Regular | 67% / 55% / 43% | 36% | depth 4: 40%, 7: 10% | 39% (36–41%), 14% (13–14%) |
| Skilled | 79% / 69% / 55% | 41% | depth 4: 85%, 13: 35%, win: 3% | 85% (81–88%), 44% (41–47%), 0–1% |
| Expert | 88% / 80% / 67% | 44% | depth 13: 85%, win: 35% | 90% (88–91%), 34% (29–39%) |

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
- **Supplies:** about 0.9 healing potions per floor, restoring a share of max health (small 35%, from depth 3 medium
  50%, from depth 7 large 70%; the runs assume 38% early, rising to 60%), as measured with `supply`; about one food per
  floor at 10%; three health fountains per run. Health never regenerates on its own. Classes start without potions (today only the Mage starts with one).

### Gear

The expected hero wears gear that is on curve for its depth, plus a quality and affix bonus that grows from 0% at
depth 1 to 25% at depth 24.

- **Weapons:** the reference one-handed sword's mean hit is `6 × 1.08^(depth − 1)` with a ±40% range. Other kinds are
  multiples of it: dagger 0.72× every 0.36 s; sword 1.0× every 0.5 s; two-handed axe 1.5× every 0.72 s, clipping a
  second engaged monster 15% of the time; crossbow 1.3× every 0.95 s at range; wand 3 homing darts of 0.25× every
  0.6 s. The kinds no class starts with share the sword's damage per second when one-handed (axe 1.15×, blunt 1.3×:
  bigger, slower hits that cut through armor) and add cleave when two-handed (sword, axe, blunt); spears trade the off-hand
  for a larger hit, and a staff fires four missiles for a volley 1.5× a wand's. Their exact multiples are proposals in `params.py`.
- **Armor slots** are sized against the standard monster's mean hit `m` at that depth. A Normal medium body piece
  holds `0.30 m` armor, a helmet `0.15 m`, a shield `0.22 m`. Light pieces carry half the armor and remove 5% of
  landed hits; heavy pieces carry 1.3× and let 10% more land. The game has no passive evasion, so it expresses that footwork as movement speed: a light body piece moves its wearer 5% faster, a heavy one 8% slower.
- **Tiers T1–T5** are introduced at depths 1, 6, 11, 16 and 21, drop for 8 depths and are on curve one depth after
  introduction. `simulate.py gear` prints the authoring targets: T1 sword 4–9, T3 sword 8–20, T5 sword 18–42; T3
  heavy body armor 11, and so on.
- **Spells** scale off the focus's hit like missiles. The baseline Fireball deals 1.3× to about 2.2 monsters for 24
  mana.

## Monsters

Monsters never scale at runtime. An authored monster keeps its stats wherever it spawns; deeper floors spawn monsters
authored for deeper depths. Authoring targets come from the **standard monster** at depth `D`, built against the
reference hero:

- **Health** `= hits_to_kill(D) ×` the reference hero's mean hit after the monster's armor (10% of that hit).
- **Mean hit** `= hero health / (hits_to_die(D) × (1 − hero armor share / 2))`, so the reference hero dies after
  `hits_to_die` landed hits. One attack every 2.2 s, ±35% range.
- **The difficulty dial:** `hits_to_kill` rises from 2.2 to 3.2 landed hits (misses come on top) and `hits_to_die`
  falls from 9.6 to 4.4 across the run.
  Monsters therefore outgrow the hero slowly, and later floors are deadlier at the same skill.

Archetypes are multiples of the standard monster at the same depth:

| Archetype | Health | Armor (share of hero hit) | Damage | Attack every | Notes | Threat level vs depth |
| --- | --- | --- | --- | --- | --- | --- |
| Fodder | 0.55 | 0 | 0.65 | 2.0 s | fast | −4 |
| Swarmer | 0.45 | 0 | 0.5 | 1.9 s | 35% venom | −5 |
| Standard | 1 | 0.10 | 1 | 2.2 s | | 0 |
| Brute | 1.7 | 0.35 | 1.3 | 2.5 s | guards 25% | +3 to +4 |
| Shooter | 0.8 | 0.05 | 0.95 | 3.2 s | ranged | −1 |
| Caster | 0.9 | 0 | 0.35 in 3 darts | 3.3 s | heals an ally 30% every 9 s; darts ignore armor | −1 to −2 |
| Gatekeeper | 6 | 0.35 | 1.6 | 2.4 s | guards 25%, ×1.25 below 65% and ×1.55 below 30% health, 35% area attacks, which ignore armor | +10 to +12 |

## Cost, threat level and XP

Cost turns any monster into one number in the hero's currency. The **duel cost** of monster `m` is the share of the
hero's maximum health it takes before it dies:

`cost = monster damage per second after armor and avoidance × time to kill it / hero max health`

Ranged heroes get free shots while melee monsters close the 4 m gap; melee heroes take extra shots while walking up
to shooters. Healers add what they heal, burned through again, while an ally keeps attacking.

- **Cost units** `u(m, D)` = the duel cost against the reference hero at depth `D` ÷ the standard monster's. Floors
  spend their encounter budget in these units.
- **Threat level** = the depth where `u = 1`: the depth at which `m` is exactly a standard fight. As the hero grows, a
  fixed monster gets about 23% cheaper per depth, so threat levels outside depths 1–24 extrapolate at that rate.
- **XP** `= 10 × 1.15^(threat level − 1)`, doubled for gatekeepers. XP grows more slowly than danger (1.15 vs 1.23 per
  level), so out-of-depth weaklings pay little but tough monsters are not exponential jackpots. A standard monster
  pays 10 XP at depth 1 and 47 at depth 12; the depth-3 gatekeeper pays about 100.

`derive` writes each monster's threat level into its definition, and the game pays XP from it through the level's
`ThreatProfile` (`scenes/levels/encounters/threat_profile.tres`, whose constants `derive` also writes). The `Boss`
archetype marks the gatekeeper bonus. `simulate.py monster` evaluates proposed stats before they are authored.

## Floors and gatekeepers

A floor spends a budget of 9 standard fights at depth 1, rising to 23 at depth 24. It spends it on **packs**, not
single monsters, so roles appear together. Rats lead: rat packs of three from depth 1 and hordes of five from depth 2,
with fodder pairs, lone standards, patrols (standard and fodder), sentries (a shooter) and, from depth 3, warbands (two
standards and a caster who heals them); guard posts (brute and shooter) and vanguards (brute, standard and shooter)
join from depth 4. Each pack has a depth range in `params.PACKS`. Gatekeeper floors spend 60% of the budget, then the
gatekeeper.

A pack costs `(N + 1) / 2` times its members fought alone (`model.group_factor`): the hero kills them one by one while
the rest keep attacking. The simulator's group fights match that within 10% for swarms, fodder and mixed melee; packs
with a healer or a shooter come out a little lower. The floor budget is sized for these group costs.

The game spends the same budget through the realm's encounter table, whose entries are packs. A monster costs
`cost growth ^ (threat level − depth)` there: one standard fight on the depth of its threat level, 23% less for each
depth deeper. That closed form stays within about 20% of the model's cost at the depths a monster spawns. A pack
spawns together in one room.

At regular skill and without potions, an ordinary floor costs about 40% of max health at depth 3 and over 110% at
depth 24, and a gatekeeper about 110% and 230%. Supplies cover part of that: the late game is meant to drain
potions.

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
| Barbarian | 12 / 8 / 10 / 5 | STR .45, DEX .15, VIT .25, INT .15 | two-handed axe, medium armor | gatekeepers | — |
| Rogue | 9 / 15 / 11 / 8 | STR .2, DEX .5, VIT .25, INT .05 | dagger, crossbow opener, light armor, 2% crit per DEX | gatekeepers | packs |
| Mage | 6 / 10 / 10 / 15 | STR .1, DEX .1, VIT .3, INT .5 | wand, Fireball, light armor | packs | gatekeepers |

All four currently pass. The realm cost of each is within 8% of the mean, and survival differs by a few points at
every skill. Homing missiles recover only 10% of missed attacks (`HOMING_ASSIST`): with measured hit rates this low, a
larger share made the Mage far safer than the other classes.

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
  flagged. `survival --runs 1000` takes about 20 s.
- **Supplies in the game.** `supply` runs a headless probe (`scenes/debug/balance/loot_supply_probe.tscn`) that
  generates seeded floors and samples every loot source on them, then compares healing potions, healing, food and
  gear per floor with `SUPPLY`. Healing potions are the most common potion; realm 1 gives about one per floor.

### Game content

`audit` compares the game's `.tres` files with the model's targets; `derive` writes the targets into them. Both take
`--only monsters,weapons,armor,classes` and `--match <path text>` (repeatable). `derive` overwrites every value it
owns; review the result with `git diff`.

```bash
uv run scripts/balance/simulate.py audit                       # everything off target by more than 10%
uv run scripts/balance/simulate.py derive --match skeleton     # write the targets of matching resources
```

- **Monsters** carry an `Archetype` and a `BalanceDepth` in their definition: the role and the depth its stats are
  sized for. `derive` writes health, armor, speed and the read-only `ThreatLevel`. The first damaging action of the
  combat profile takes the archetype's hit and attack rhythm. Further damaging actions keep their damage ratio to it
  and their authored cooldowns, so signature attacks such as the Warden's overhead and tremor come as often as
  designed; the model counts only the first attack, so it underestimates such monsters a little. A definition without an archetype is reported and left alone. The `Boss` archetype is the gatekeeper.
- **Weapons** get their kind from their attack style (`MagicStaff` decides wand or staff by hands) and their target
  from `Tier`. A combo's average damage and duration scales count toward the hit and the seconds per attack, so
  `derive` writes the base damage range and `PerformDuration` that reproduce the kind's hit and rhythm. Wand and
  staff missiles get the kind's dart share.
- **Armor** gets its slot from `ValidSlots` and its target from `Tier` (Normal quality, medium weight).
- **Classes** match the model's classes by `Id`. `derive` writes base attributes, and the hero sheet and stat
  profiles that reproduce the model's level-1 health, mana and crit chance with the game's current formulas. Damage
  from attributes waits for the rule change.

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

  `calibrate` reports the share of monster attacks the player avoided per kind (melee, ranged, homing) and the share
  of the player's own attacks that landed, each with the closest `SKILL` tier. Move the tiers toward the measured
  values, then retune the targets with `survival`.
- Check time to kill and damage taken against the standard monster and the gatekeeper in the arena. They should
  match the simulator's duel numbers within about 20%. If not, fix the model's timing (attack cycles, engagement
  gap) first.

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
- elemental damage passes armor, potions and food heal a share of max health, and realm 1 drops about one healing
  potion per floor (`supply`);
- body armor and helmets exist in three weights, and every class starts in its own.

The remaining steps:

1. **Realm 1.** Calibrate the novice and regular skill tiers from playtests (see
   [Calibration](#calibration)). Most of the difficulty targets fall in these depths.
2. **Realm by realm.** Each realm gets its monsters, packs and gear tier through `derive`, passes `audit`, and is
   playtested before the next one starts.
3. **Unmodeled systems.** Shops, traps and the rest of [Not modeled yet](#not-modeled-yet) enter `params.py` before
   they are built. A shop, for example, turns gold into supplies and gear upgrades.

## Not modeled yet

Shops and gold, traps, stealth and skipped fights, partial floor clears, status effects beyond venom, and the
gatekeepers' individual attacks. Each gatekeeper is the archetype above. Add them to the model before relying on it
for those systems.
