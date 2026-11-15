# Balance rework

Proposed. Reworks how the [balance model](balance-model.md) sizes monsters, gear and floors, and changes armor, movement
speed and monster roles in the game. The current rules are in the balance model, the
[combat system](../combat-system.md) and [monster authoring](../monster-authoring-design.md); this plan describes only
what changes.

## Why

A barbarian playtest on 2026-11-14 (depths 1–3, the Warden beaten) shows the shape of the problem:

| Monster | Killed | Hero hits to kill | Damage dealt to the hero, all of them |
| --- | --- | --- | --- |
| Plague Rat | 23 | 1.7 (10 died to one hit) | 7.3 |
| Skeleton Minion | 21 | 2.1 | 9.6 |
| Skeleton Warrior | 2 | 4.5 | 17.8 |
| Skeleton Crossbowman | 1 | 3 | 7.7 |
| Forsaken Warden | 1 | 13 | 87.5 |
| Floor traps | — | — | 23.7 |

The hero found a tier 1 axe (4–9) 43 seconds in, replacing the worn 3–6 starter. The 44 rats and minions dealt less than
two warriors, and traps dealt more than all of them. A rat cost about 1/30 of a warrior in play while the model prices
it at 1/5 to 1/3. The causes are structural:

1. **One fitting target.** The model is fitted only to arrival odds by depth. Those care where the hero dies, not how a
   floor plays, so the fit moved all danger into gatekeepers: an ordinary floor costs a regular hero 9% of their health.
2. **The modeled hero is not the played hero.** Monster health is sized in hits of an on-curve reference sword at the
   realm's middle depth. The played hero starts below that curve and jumps above it with the first drop, because tier 1
   weapons drop at full strength from depth 1, and adds crits and levels on top.
3. **Flat armor erases weak hits.** Armor absorbs a uniform roll of 0 to armor from every physical hit
   (`HurtBoxComponent`, `model.net_hit`). A 2–4 bite against armor 4 does almost nothing, so swarms hurt only through
   venom and heavy armor is close to immunity.
4. **The hero outruns everything.** The hero moves at 7.5 m/s; monsters at 2.4–4.2. The hero decides every
   engagement and backs off at will; rats started 21% of their attack cycles.
5. **Roles are multipliers.** Archetypes differ by health, damage and cycle multiples of one standard monster. Only the
   warrior (guard and counter) and the mage (healing) ask for a different response.
6. **Traps and loot pacing sit outside the model**, yet both shaped the run more than the monsters did.

## Goals

- Every monster role threatens on its own and asks for its own response.
- Ordinary floors drain health and supplies; gatekeepers are peaks, not the only danger.
- Early drops are upgrades of 10–20%, not 50%.
- The model predicts measured playtests: hits to kill, damage taken per monster and per floor.

Not in scope: new monsters, realms or items. Save compatibility stays out of scope; each slice that changes authored
content bumps `SaveGame.CurrentVersion`.

## Design

### 1. Targets per role and per floor

The model is fitted to three kinds of target, in this order:

1. **Role targets** set each archetype's health and damage at its authored depth, against the expected hero at that
   depth (section 2), at regular skill:

   | Archetype (monster) | Hero landed hits to kill | Its landed hits to kill the hero | Wind-up |
   | --- | --- | --- | --- |
   | Swarmer (rat) | 2–3 | about 12 | 0.25 s |
   | Fodder (minion) | 3 | about 10 | 0.35 s |
   | Standard (warrior) | 5–6 | about 5 | 0.35 s |
   | Shooter (crossbowman) | 3 | about 6 | 0.63 s |
   | Caster (mage) | 3 | about 8, plus heals | 0.6 s |
   | Brute | 7–8 | about 4 | 0.45 s |
   | Gatekeeper | 25–30 | 3–4 | 0.51 s |

   These replace the archetype `hp` and `damage` multipliers, `STANDARD["hits_to_kill"]`/`["hits_to_die"]` and the
   `min_hits` health floor. The difficulty dial becomes a growth of these counts with depth.
2. **Floor attrition** sizes the floor budget: a regular hero without potions loses about 40–50% of max health on an
   ordinary floor and 70–90% on a gatekeeper floor (open decision). Healing supplies are sized to refill that.
3. **Arrival odds** (`TARGETS`) become a check of the whole, tuned last through supplies.

`simulate.py fit` replaces the ad hoc fitting scripts used so far: it searches the floor budget's first and last values
and the gatekeeper floor share against attrition, then reports arrival odds.

### 2. Expected gear follows loot pacing

The expected hero's weapon at depth `d` mixes the worn starter and found weapons by the chance a weapon of each tier and
quality has dropped by then, from the loot the floors place (chests, loose items, monster drops) and the weapon pool's
weights. `report_supply` already samples that loot; the model reuses it. This replaces the linear `starter_share` ramp,
`tier_lag` and `found_bonus` for weapons.

Loot changes so the first drop is a 10–20% upgrade (open decision, pick one):

- **Quality by depth:** floor 1–2 weapons roll mostly Poor or Normal, Superior from depth 3; or
- **Late first tier:** the weapon pool drops tier 1 from depth 2; floor 1 places none; or
- **Stronger starter:** `StartingWeaponScale` 0.85 with the quality spread unchanged.

### 3. Armor: a flat roll that never zeroes a hit

Armor keeps its flat roll, the easiest rule to read and explain ("blocks up to 4 per hit"), and gains a minimum: a hit
always deals at least 1 point (a smaller hit deals itself), as in Shattered Pixel Dungeon and Dungeon Crawl. The old roll
let small hits drop to zero, so soft swarms did nothing and heavy armor was close to immunity. Armor numbers stay as they
are: the starting barbarian's armor 4 lets a 3-point bite through for 1 to 3 (1.5 on average), takes 2 off an 8-point
blow on average and 2 off a 20-point swing.

Alternatives considered: a share that shrinks as the hit grows (Path of Exile's `armour / (armour + 10 × hit)`), and a
share set by the attacker's level (Diablo III's `armor / (armor + 50 × level)`, our monsters' level being their
`BalanceDepth`). Their averages against realm 1's monsters are close to the roll's; the roll was chosen for being easy
to read. Hit-chance armor (Diablo II's defense, NetHack's armor class) is out: once a hit connects there is no hidden
miss roll.

- `common/combat/ArmorRules.cs`: `ArmorRules.Mitigate(float hit, float armor, float minimum, RandomNumberGenerator
  random)`, used by `HurtBoxComponent` (whose `MinimumDamage` export sets the minimum) in place of its own roll;
  mirrored in `params.py` (`ARMOR`) and checked by `audit`. `model.mitigate`/`net_hit` give its mean in closed form; the survival simulation rolls it as the game does.
- The trace keeps `armor` and replaces `armor_roll` with `armor_reduction`, the amount actually blocked.

### 4. Speeds

The hero moves at 6.0 m/s (was 7.5); armor weight factors stay (light 1.05, heavy 0.92) and so does the dodge. Monster
speeds come from archetypes:

| Archetype | Speed (m/s) | Was |
| --- | --- | --- |
| Swarmer | 6.5, faster than the hero | 4.2 |
| Fodder | 5.0 | 3.2 |
| Standard | 4.5 | 2.8 |
| Shooter | 4.0 | 2.8 |
| Caster | 3.5 | 2.4 |
| Brute | 4.0 | 2.6 |
| Gatekeeper | 3.5, with its slam's reach | 2.6 |

The model's kiting share applies only to monsters slower than the hero, scaled by how much slower; `uptime` is
re-measured after this slice, since retreating stops being free.

### 5. Roles that ask for a response

Each role gets a behavior built from existing strategies and actions, then numbers only scale it:

| Monster | Behavior | What it asks of the hero | Building blocks |
| --- | --- | --- | --- |
| Rat | flanks to the rear, bites, darts out | keep moving, face the pack | `bite_and_dart` movement with the `flank_rear` flank (60° arc) |
| Minion | closes in numbers, surrounds | cleave or fall back to a doorway | `engagement_ring` movement with the `surround` flank (360° arc), attack slots |
| Warrior | guards, counters blocked hits | dodge the counter, attack its flank | done |
| Crossbowman | keeps its distance, aims | close in or break line of sight | `MaintainDistanceStrategy`, aim lock |
| Mage | heals allies, keeps back | kill it first | done: heal spells, `MaintainDistanceStrategy` |

Only rat and minion authoring changes; the warrior, crossbowman and mage are checked against the table once their
speeds change.

### 6. Traps in the budget

A trap costs floor budget like a pack. `FloorTrap.Damage` is derived per realm (a share of the expected hero's health)
and the trap count comes from the budget instead of `MinTraps`/`MaxTraps`. The model charges each trap's damage times
the chance a hero of that skill triggers it, measured from playtests.

## Affected files

```text
scripts/balance/
  ~ params.py      ROLES, ATTRITION, ARMOR; archetype speeds; drop STANDARD hits, min_hits, starter ramp
  ~ model.py       monster() from role targets; expected_weapon(); mitigate(); floor_attrition()
  ~ content.py     derive armor, monster speeds, trap damage and counts, loot pacing
  ~ simulate.py    fit command (the playtest fight report landed with slice 1)
common/combat/
  + ArmorRules.cs  the armor roll and its minimum, shared by every hurtbox
scenes/components/
  ~ HurtBoxComponent.cs   ArmorRules.Mitigate instead of its own roll; trace field
scenes/player/
  ~ player.tscn           movement speed 6.0
scenes/props/
  ~ FloorTrap.cs          damage stays authored, written by derive
scenes/levels/generators/
  ~ MapGenerator.Loot.cs  trap count from the floor budget
scenes/enemies/**         derived health, damage, speed; rat and minion tactics
scenes/items/**           derived armor; loot pool and quality pacing
tests/
  + ArmorRulesTest.cs     roll bounds, the minimum, averages
  ~ affected combat, loot and encounter tests
docs/
  ~ specs/balance-model.md, combat-system.md, monster-authoring-design.md, stats-and-itemization.md
```

## Slices

1. **Measure.** Landed: `playtest` reports hero hits to kill and damage dealt per monster type, trap damage, and the
   first weapon picked up, and reproduces the table above from the 2026-11-14 log.
2. **Armor.** Landed: the flat roll with a 1-damage minimum (`ArmorRules`), the hurtbox, `model.mitigate`/`net_hit` and
   the simulator; armor numbers unchanged (the starting barbarian has 4). Realm costs stay within 6% of the class mean.
   Still to playtest: a rat bite always hurts and the knight is no longer immune.
3. **Speeds.** Landed: the hero at 6.0 m/s (`ENGAGEMENT["hero_speed"]`, written to the stat sheet's `BaseSpeed`), archetype speeds as
   in the table, and kiting scaled by the speed gap. With less kiting the wand's darts went back up to 0.22 of the
   weapon's damage (staff 0.25), so the Mage survives as often as the others. The arena smoke scenario and the full suite
   pass. Playtested (2026-11-14, with the hero still at 7.5 m/s): monsters started 76% of their attack cycles and rats,
   now nearly as fast as the hero, were avoided 10% of the time instead of 64%. The model now ties melee avoidance to
   the speed gap (`footwork`), takes `uptime` 0.75, and refits the dial, the floor budget (8 to 12, a third fewer
   monsters) and the gatekeeper's damage (1.15×).
4. **Role targets and gear pacing.** Partly landed: weapons rescaled after Diablo II and Shattered Pixel Dungeon (5% growth
   per depth, a T1 sword rolls 2–5, ranges ±35–60% by kind, the Warden drops its own tier at Magic quality), armor on the
   weapon curve (T1 pieces 1–3), body armor shortening the dodge (medium by a third, heavy by half, heavy 15% slower),
   and monster health from the role targets through the dial and archetype multiples (rat 6, minion 10, warrior 17,
   Warden 82). Monster damage was refitted to the arrival targets rather than the role table's hits to kill the hero
   (`hits_to_die` now 19 rising to 30): with sturdier monsters the table's figures would have emptied the floors. The
   expected hero now wields the floors' measured weapon drops (`expected_weapon`, `supply --save`) and monsters are
   sized against it; the starters stay worn. Verify with a playtest's fight report against the role targets.
5. **Attrition, fitting and traps.** Landed: attrition targets kept at the liked difficulty (ordinary floor 60%,
   gatekeeper floor 75%, `params.ATTRITION`); `simulate.py fit` sizes the budget line (7.3 to 11.4) and the gatekeeper
   floor's share (45%) to them and reports arrival odds; parity now costs real ordinary floors (it used to cost the
   gatekeeper floor's 20% packs as one). Traps count toward attrition and their damage grows with depth (9% of the
   expected hero's health, derived), but their count stays authored: at about 6% a floor the fit leaves room for them.
   Supplies stay scarce rather than refilling attrition, as playtests asked for fewer potions. Verify floor attrition in a
   playtest.
6. **Role behaviors.** Landed: an optional `Flank` on any combat profile (`FlankStrategy`, with a per-monster
   `FlankRuntime`) circles a monster to its ring bearing behind the player before it closes in. Rats flank on a 60° arc
   (`flank_rear`) around their bite-and-dart retreat; minions spread on a 360° arc (`surround`) around the engagement
   ring, whose waiters now circle the player too. `MonsterFlankTest` verifies both in the arena. The warrior, crossbowman and
   mage keep their behaviors (guard and counter, keep distance, heal and keep back). Still to playtest.

Slices 2 and 3 change how fights feel the most and stand alone; slice 1 should land first so each later slice is
measured against the same report.

## Open decisions

- Attrition per ordinary and gatekeeper floor: decided, 60% and 75% (today's difficulty).
- Armor: decided, a flat roll with a 1-damage minimum.
- Hero speed (proposed 6.0 m/s) and whether rats outrun the hero.
- Which loot change sets the first-drop upgrade: not a stronger starter; gatekeepers drop their own tier.
- Whether role targets hold for every class or only on average (class parity keeps realm costs within 10% today).
