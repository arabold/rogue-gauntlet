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
| Novice | 25% / 20% / 10% | 60% | depth 2: 75%, 3: 35%, 4: 3% | 77%, 22% (12–36%), 0% |
| Regular | 50% / 45% / 30% | 75% | depth 4: 40%, 7: 10% | 37% (34–41%), 10% (8–13%) |
| Skilled | 68% / 62% / 45% | 85% | depth 4: 85%, 13: 35%, win: 3% | 85%, 39% (34–43%), 0–1% |
| Expert | 82% / 76% / 60% | 92% | depth 13: 85%, win: 35% | 86% (84–88%), 40% (36–42%) |

"Arriving at depth 4" means beating the depth-3 gatekeeper; "win" means beating the depth-24 one. The skill values are
guesses until playtests measure them (see [calibration](#calibration)).

## Combat rules the model assumes

- A hit rolls uniformly in its damage range; a critical hit doubles it.
- **Armor** absorbs a uniform roll in `[0, armor]` from every hit, as implemented. A hit `h` against armor `A` deals
  `h − A/2` on average when `h ≥ A`, and `h²/(2A)` below that. Flat armor is strong against many small hits and weak
  against a few big ones: it trivializes swarms, matters less against gatekeepers, and would crush multi-dart
  missiles.
- **Magic ignores armor** (proposed rule change). Missiles and spells skip the armor roll, as wand zaps do in
  Shattered Pixel Dungeon. Otherwise a wand's three small darts lose most of their damage to an armored gatekeeper,
  and no Mage tuning reaches parity there.
- **Attributes scale; levels and gear grow** (proposed rule change). Each attribute changes a shared curve by a
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
- **Supplies:** about one healing potion per floor, healing 50% of max health (proposed: a share of max health, not a
  fixed amount); about one food per floor at 10%; three health fountains per run. Health never regenerates on its
  own. Classes start without potions (today only the Mage starts with one).

### Gear

The expected hero wears gear that is on curve for its depth, plus a quality and affix bonus that grows from 0% at
depth 1 to 25% at depth 24.

- **Weapons:** the reference one-handed sword's mean hit is `6 × 1.08^(depth − 1)` with a ±40% range. Other kinds are
  multiples of it: dagger 0.72× every 0.36 s; sword 1.0× every 0.5 s; two-handed axe 1.5× every 0.72 s, clipping a
  second engaged monster 15% of the time; crossbow 1.3× every 0.95 s at range; wand 3 homing darts of 0.25× every
  0.6 s.
- **Armor slots** are sized against the standard monster's mean hit `m` at that depth. A Normal medium body piece
  holds `0.30 m` armor, a helmet `0.15 m`, a shield `0.22 m`. Light pieces carry half the armor and remove 5% of
  landed hits; heavy pieces carry 1.3× and let 10% more land. Body armor and helmets are new slots to build.
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
- **The difficulty dial:** `hits_to_kill` rises from 3.5 to 5 and `hits_to_die` falls from 9 to 5 across the run.
  Monsters therefore outgrow the hero slowly, and later floors are deadlier at the same skill.

Archetypes are multiples of the standard monster at the same depth:

| Archetype | Health | Armor (share of hero hit) | Damage | Attack every | Notes | Threat level vs depth |
| --- | --- | --- | --- | --- | --- | --- |
| Fodder | 0.55 | 0 | 0.65 | 2.0 s | fast | −4 |
| Swarmer | 0.45 | 0 | 0.5 | 1.9 s | 35% venom | −5 |
| Standard | 1 | 0.10 | 1 | 2.2 s | | 0 |
| Brute | 1.7 | 0.35 | 1.3 | 2.5 s | guards 25% | +3 to +4 |
| Shooter | 0.8 | 0.05 | 0.95 | 3.2 s | ranged | −1 |
| Caster | 0.9 | 0 | 0.8 in 3 darts | 3.3 s | heals an ally 30% every 9 s | −1 to −3 |
| Gatekeeper | 6 | 0.35 | 1.6 | 2.4 s | guards 25%, ×1.25 below 65% and ×1.55 below 30% health, 35% area attacks | +9 to +11 |

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

The game will compute threat level and XP from a monster's definition with these formulas, replacing `XpReward` and
hand-authored `Threat`. `simulate.py monster` evaluates proposed stats today.

## Floors and gatekeepers

A floor spends a budget of 8 standard monsters at depth 1, rising to 11 at depth 24. It spends it on **packs**, not
single monsters, so roles appear together: a patrol (standard and fodder), a guard post (brute and shooter), a rat
swarm, a warband (two standards and a caster who heals them) and, from depth 4, a vanguard (brute, standard and
shooter). Fighting together costs more than the sum of the members, because each monster keeps attacking until the
hero reaches it. Gatekeeper floors spend 60% of the budget, then the gatekeeper.

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

All four currently pass. The realm cost of each is within 6% of the mean, and survival differs by a few points at
regular, skilled and expert skill. One spread remains: novices survive depths 1–2 more often as a Mage (36% reach
depth 3, against 12–26% for the others). Range and homing missiles forgive poor aim. Decide whether that is a welcome
on-ramp or needs tuning.

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

## Calibration

The model can be no better than its skill tiers. Measure them before tuning numbers further:

- From combat-arena traces (`--combat-log`), take the share of monster attacks the player avoided per attack kind,
  and the share of the player's attacks that landed. Use several players of known experience.
- Check time to kill and damage taken against the standard monster and the gatekeeper in the arena. They should
  match the simulator's duel numbers within about 20%. If not, fix the model's timing (attack cycles, engagement
  gap) first.

## Implementation plan

1. **Model in the game.** Port the reference curves, duel cost, threat level and XP to C#, with their parameters in
   one Godot resource. Monsters compute threat level and XP from their definitions, which removes `XpReward` and
   `MobSpawnEntry.Threat`. Floor budgets come from the depth instead of authored `FloorBudgets`. Bump
   `SaveGame.CurrentVersion`.
2. **Rule changes.** Make attributes proportional (Vitality as % health; Strength and Intelligence as % damage).
   Let missiles and spells skip armor. Make healing potions heal a share of max health.
3. **Leveling.** The fitted XP ladder, level-ups adding health and the class's attribute growth, and the level-up
   presentation the game already has an effect for.
4. **Armor slots.** Body armor and helmets in light, medium and heavy weights, sized from the slot shares and tiers.
5. **Derived content.** Rewrite weapons, shields, monsters, spells, potions, class sheets, packs and loot supply
   from the simulator's target tables.
6. **Audit mode.** Once the `.tres` files hold derived values, `simulate.py audit` reads them and flags anything
   off its target. From then on the simulator checks the real game instead of the plan.
7. **Calibrate** the skill tiers from playtests, then retune the targets.

## Not modeled yet

Shops and gold, traps, stealth and skipped fights, partial floor clears, status effects beyond venom, and the
gatekeepers' individual attacks. Each gatekeeper is the archetype above. Add them to the model before relying on it
for those systems.
