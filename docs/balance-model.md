# Balance model

**Status: implemented; calibration and the [remaining work](#remaining-work) are open.** This defines where the game's
balance is going and how every number is derived. The game's monsters, classes, gear, supplies, statuses, traps and
loot bands come from it. The simulator in [`scripts/balance/`](../scripts/balance/) implements these formulas, and
its [`params.py`](../scripts/balance/params.py) holds every number. This document explains them; when a number here
and the file disagree, the file wins.

## Goals

- **Hard, like Rogue, NetHack and Shattered Pixel Dungeon.** Most new players die in the first realm, beating the first
  gatekeeper takes several runs, and even experts lose most late runs.
- **One currency.** Every danger and every defense is measured in the hero's health, so armor, weapons, spells,
  monsters, traps and supplies trade against each other.
- **Ordinary floors drain; gatekeepers are peaks.** Every monster role threatens on its own and asks for its own
  response, so floors wear the hero down and gatekeepers are not the only danger.
- **Classes differ in style, not strength.** At equal skill every class survives as deep as the others, while each
  has fights it handles better or worse.
- **Content derives from the model, and the model predicts play.** Each tier, slot and monster archetype gets target
  values, so new items, spells and monsters can be checked before anyone plays them; measured playtests (hits to kill,
  damage taken per monster and per floor) check the model in turn.

## Difficulty targets

Skill is the one input the game cannot know, so the model plays four players (`SKILL`). *Avoid* is the share of
monster attacks they avoid by dodging, guarding or spacing. *Efficiency* is the share of their own attacks that land.
They differ in when they drink potions and how many found potions they can identify. `TARGETS` sets the odds of
arriving at a depth for each, and `survival` simulates them:

| Skill | Avoid melee / ranged / homing | Efficiency | Arrival odds target |
| --- | --- | --- | --- |
| Novice | 51% / 35% / 27% | 42% | depth 2: 75%, 3: 35%, 4: 3% |
| Regular | 67% / 55% / 43% | 53% | depth 4: 40%, 7: 10% |
| Skilled | 79% / 69% / 55% | 60% | depth 4: 85%, 13: 35%, 25: 3% |
| Expert | 88% / 80% / 67% | 64% | depth 13: 85%, 25: 35% |

The target depths date from the 24-depth run of three-floor realms, where depth 4 was the first arrival past a
gatekeeper and 25 the win; they still need re-keying to the gatekeepers on depths 5, 10, 15, 20 and 25 (see
[remaining work](#remaining-work)). The skill values are preliminary (see [calibration](#calibration)): *skilled* is
one developer's profile, avoidance measured in the combat arena against slow monsters and efficiency over four floor
playtests, and the other tiers keep their earlier spacing. The melee column holds against monsters slow enough for full
[footwork](#combat-rules-the-model-assumes); faster ones are avoided less. Since footwork caps everyone's melee
avoidance against fast monsters, skill spreads the outcomes less than the targets ask, and fast heroes (light armor)
gain the most from skill, heavy ones the least.

## Combat rules the model assumes

- A hit rolls uniformly in its damage range; a critical hit doubles it.
- **Armor** blocks a uniform roll in `[0, armor]` from every physical hit, but a hit always deals at least 1 (a smaller
  hit deals itself; `ArmorRules`, `HurtBoxComponent.MinimumDamage`, `params.ARMOR`), as in Shattered Pixel Dungeon and
  Dungeon Crawl. A hit `h` against armor `A` deals `h − A/2` on average when `h ≥ 1 + A`, and `1 + (h − 1)²/(2A)`
  between 1 and `1 + A`. The minimum keeps weak hits from vanishing, so soft swarms still hurt and heavy armor is not
  immunity. A flat roll is the easiest rule to read ("blocks up to 4 per hit"); a share that shrinks as the hit grows
  (Path of Exile) or one set by the attacker's level (Diablo III) averaged about the same against the first realm.
  Hit-chance armor (Diablo II's defense, NetHack's armor class) is out: confirmed contact has no hidden miss roll.
- **Magic ignores armor.** Elemental damage (missiles, spells, burning) skips the armor roll, as wand zaps do in
  Shattered Pixel Dungeon, for heroes and monsters alike. Otherwise a wand's three small darts lose most of their
  damage to an armored gatekeeper, and no Mage tuning reaches parity there. Monster casters' missiles and gatekeepers'
  area attacks therefore ignore the hero's armor too, so the caster's damage is set low.
- **Attributes scale; levels and gear grow.** Each attribute changes a shared curve by a
  percentage, so a class's advantage keeps its size at every depth. Flat per-point bonuses fade as levels add health
  and gear adds damage, so a class balanced at depth 1 drifts out of balance by depth 25.
- **Footwork needs a slower monster.** Stepping out of reach is most of melee avoidance, so skill avoids a melee
  monster in full only when it moves at most 40% of the hero's speed, less the closer it comes to the hero's speed,
  and not at all once it is as fast (`model.footwork`, `ENGAGEMENT["footwork"]`). Fitted to playtests in which rats at
  0.56 of the hero's speed were avoided six times as often as at 0.87, with the same bite. The hero cannot step away
  from rats, which outrun it, and heavy armor's slowdown costs footwork against everything.
- **Class and armor bonuses remove a share of the hits that skill lets through.** If they added to the avoid chance
  instead, the same +5% dodge would be worth far more to an expert than to a novice.

## The hero

The hero arrives on each depth at about that level: the [XP ladder](#experience) is fitted to make this true. Every
class follows the same curves:

- **Health** `= (50 + 7 × (level − 1)) × (1 + 4% × (Vitality − 10))`.
- **Strength:** +3% physical damage per point above 10. **Dexterity:** 1% crit chance per point (2% for the Rogue).
  **Intelligence:** +3% missile and spell damage per point above 10; mana `= 20 + 5 × Intelligence`, regenerating
  1% of the maximum per second.
- Each level adds one attribute point, split by class (`growth` in `params.py`).
- **Supplies:** healing potions heal fixed points, so a size is worth less as the hero grows and the next size takes
  over: each size heals a set share of the expected hero's health where it arrives (`SUPPLY["potion_sizes"]`), and
  small ones stop once large ones are near. The hero finds about half a healing potion per floor early, rising to one
  late, as measured with `supply`. Healing potions are only about a quarter of the potions found, so their bottle
  stays a guess until one is drunk or identified. Food heals a little by its portion (small: fruit and drinks; medium:
  bread and cheese; large: meat), about one per floor; health fountains give a few full heals per run. Health never
  regenerates on its own. The model starts every class without potions (`starting_potions`); in the game only the Mage
  packs one.
- **Harmful supplies:** poison takes fixed points as healing gives them, in five ticks over 10 s: a poison potion
  takes half of what the healing potion of its size gives, spoiled food twice what its portion heals, so a bad bite
  costs more than a good one gives. Any healing potion cures poison and blindness. Blindness and confusion last long
  enough by potion size that a pack finds the hero before they pass (Rogue blinds for hundreds of turns and confuses for
  about twenty). The survival simulation does not charge them yet.

### Gear

The expected hero wears armor that is on curve for its depth, plus a quality and affix bonus that grows from 0% at
depth 1 to 25% at depth 24. Its weapon is what the floors actually drop (`model.expected_weapon`): every class starts
with worn (Poor) weapons at 70% of a tier 1 weapon's damage and wields the expected best of that and the weapons of
the kinds its class picks up (`uses`) found on the floors before, half of the current floor's included. Each found
weapon turns up a Poisson number of times at its measured rate (`WEAPON_SUPPLY`, from `supply --save`), so the best
one beats a damage `x` with chance `1 − exp(−count above x)`. Most floor weapons come from chests and reward rooms, so
the expected hero soon holds most of the on-curve weapon. The Barbarian keeps its one-handed axe until it can expect a
stronger two-handed weapon than its best one-handed one.

- **Weapons:** the reference one-handed sword's mean hit is `3.7 × 1.05^(depth − 1)`, so a T1 sword rolls 2–5: about
  3× from tier 1 to tier 5, like Shattered Pixel Dungeon's base weapons, with quality and affixes on top as Diablo II's
  magic modifiers are. Weapon, armor and monster numbers stay in single digits at T1. Ranges are wide, as in both
  games, and set by kind: daggers ±35%, one-handed weapons and crossbows ±40%, two-handed melee ±60%, magic foci ±25%.
  Other kinds are multiples of the sword in hit and seconds per attack (`WEAPONS`): one-handed kinds share the sword's
  damage per second and differ in hit size (bigger, slower hits cut through armor), two-handed melee kinds add cleave,
  spears trade the off-hand for a larger hit, and wands and staves fire homing darts. Ranged kinds hit less because the
  hero kites: a hero whose main weapon is ranged backs off between shots from a melee monster slower than itself and
  avoids another `kite` share of the melee hits its skill lets through, scaled by `1 − monster speed / hero speed`
  (unmeasured). Rats outrun the hero and cannot be kited. The wand is set so the Mage survives as often as the other
  classes; the crossbow so a Rogue who never draws the dagger loses more health than one who opens with it. Wood is a
  terrible weapon: wooden melee weapons (practice swords, clubs, mallets: the wooden roll table) deal and stagger 25% of
  their kind and tier; crossbows, wands and staves are wooden by nature and keep full strength.
- **Hit reactions** (`POISE`): monster attacks commit halfway through their wind-up, after which only a poise break
  stops them. A hit removes poise by its weapon kind's hit share, blunt weapons more; monsters hold a few such hits by
  archetype (gatekeepers the most, rats none), and each break grows the next meter for a while. Only two-handed blunt
  weapons push on a stagger. The model does not count staggers or lost attacks yet; playtests measure them as uptime.
- **Armor slots** follow the weapon curve, so armor stays in the weapons' range: a Normal medium body piece holds 0.75 of
  the reference sword's mean hit at its tier's depth, a helmet 0.25, a shield 0.3: as in Diablo II, body armor holds
  about three helmets and a shield about one, and a shield's block does the rest. Light pieces carry 0.6 of the armor,
  heavy ones 1.3×. Body armor sets the hero's movement: light moves 5% faster with the full dodge; medium shortens the
  dodge by a third; heavy halves it and moves 15% slower (`SpeedModifier`, `DodgeDistanceModifier`). The model counts
  the speed through footwork and the dodge as a share of landed hits (unmeasured).
- **Curses:** a quarter of dropped equipment is cursed. A curse's hidden penalty takes points, like the bonuses beside
  it (damage, armor, health, Strength, Dexterity), in tiers 1–5 like the gear, each up to twice what a ring of its tier
  gives (Dread, on damage, three times); the speed penalty is a percentage, since speed is a percentage everywhere on
  gear. The model does not count curses, since a careful player avoids most of them.
- **Jewelry** comes in tiers 1–5 that drop in the same depth bands. At its tier's on-curve depth a ring adds a quarter of
  the reference sword's mean hit as armor (Protection), 10% of the hero's health (Health), 20% of the reference
  weapon's range to both ends of the hero's damage (Savagery), or attribute points. Amulets give 1.5 times as much; a
  Ring of Frailty takes 20% of health. Swiftness, Lethargy and Dread change speed or damage by a percentage and have no
  tiers. Jewelry counts toward the found bonus, like quality and affixes.
- **Tiers T1–T5** are introduced at depths 1, 6, 11, 16 and 21, drop for 8 depths and are on curve one depth after
  introduction. `simulate.py gear` prints each tier's authoring targets.
- **Spells** scale off the focus's hit like missiles. The baseline Fireball hits about two monsters of a pack.

## Monsters

Monsters never scale at runtime. An authored monster keeps its stats wherever it spawns; deeper floors spawn monsters
authored for deeper depths. A realm authors its ordinary monsters once, for its middle floor, and its gatekeeper for its
last. The model fights and prices every floor's monsters at their realm's middle depth, so a realm's first floor holds
fewer, relatively stronger monsters and its last more, weaker ones, for the same budget. Authoring targets come from
the **standard monster** at depth `D`, built against the reference hero:

- **Health** `= hits_to_kill(D) ×` the expected reference hero's mean hit after the monster's armor, but for every
  archetype except the gatekeeper at least 1.25 average blows of the heaviest class weapon or opener (`min_hits`), so no
  class kills ordinary monsters with one routine blow. The archetype multiples follow role targets: on the first floors,
  with the worn starter, about two hits for a rat, a few for a minion, five or six for a warrior and nearly thirty for the
  gatekeeper.
- **Mean hit** `= hero health / hits_to_die(D) + half the reference hero's armor`, so the reference hero, whose armor
  blocks half its value per hit, dies after `hits_to_die` landed hits.
- **The difficulty dial:** `hits_to_kill` and `hits_to_die` rise across the run, fitted to the arrival targets with
  the floor budget and the gatekeeper's damage: monsters take several hits and hit softly, since the hero avoids few of
  their blows. Monsters therefore outgrow the hero slowly, and later floors are deadlier at the same skill.

Archetypes are multiples of the standard monster at the same depth (`ARCHETYPES`):

| Archetype | Role | Response it asks for |
| --- | --- | --- |
| Fodder | Soft bites armor mostly stops; comes in numbers | Cleave, or fall back to a doorway |
| Swarmer | Frail and faster than the hero; venom passes armor | Keep moving and face the pack |
| Standard | The yardstick | Read its swing |
| Brute | Sturdy, armored, hard-hitting; guards | Dodge its counter and attack its flank |
| Shooter | Ranged, long tell | Close in or break line of sight |
| Caster | Homing darts that ignore armor; heals allies | Kill it first |
| Gatekeeper (`Boss`) | Many hits, guards, harder below its phase thresholds, area attacks that ignore armor | Positioning and defense |

Behavior gives each role its response and numbers only scale it: rats flank to the rear, bite and dart out; minions
close in numbers and surround; warriors guard and counter; shooters keep their distance; casters heal and keep back
(see [monster authoring](monster-authoring-design.md)). A swarmer's archetype includes its venom, priced with it.

Wind-ups are the time from an attack's start to its contact. `derive` sets the primary attack's `Duration` so its clip's
contact lands there. Quick monster tells are a fraction of a Diablo II hero's swing; shooters, casters and the
gatekeeper keep their longer, authored tells.

Speeds come from archetypes too (`derive` writes `MoveSpeed`), against a hero who moves at 6.0 m/s
(`ENGAGEMENT["hero_speed"]`, written to the stat sheet's `BaseSpeed`): swarmers outrun the hero, fodder and standard
monsters come close to it, and brutes, shooters, casters and gatekeepers are slower. The hero can back off from most
monsters, but not freely. A definition's `SpeedScale` sets a monster's share of its archetype's pace (a slow blob, a
snake slower than a rat), and `derive` counts the scaled speed in its footwork and threat.

## Cost, threat level and XP

Cost turns any monster into one number in the hero's currency. The **duel cost** of monster `m` is the share of the
hero's maximum health it takes before it dies:

`cost = monster attacks that land before it dies × damage per attack after armor and avoidance / hero max health`

Blows land one at a time, as in the game: the hero's first a swing's contact after the two meet, then one per swing
that lands; the monster's first its archetype's wind-up after, then one per attack cycle. The chance the monster is
still alive after each of the hero's blows follows the weapon's roll and crits, so a monster felled by the first blow
costs nothing, and a slow, heavy weapon is worth what its blows achieve, not its damage per second. The survival
simulation plays fights with the same timing. An ordinary melee monster starts only a share of those attacks, the first
included (`ENGAGEMENT["uptime"]`, fitted to playtests): the hero's footwork, hits and guard keep the rest from starting.
Shooters, casters and gatekeepers attack on every cycle. Ranged heroes get free shots while melee monsters close the
gap; melee heroes take extra shots while walking up to shooters. Healers add what they heal, burned through again,
while an ally keeps attacking.

- **Cost units** `u(m, D)` = the duel cost against the reference hero at depth `D` ÷ the standard monster's. Floors
  spend their encounter budget in these units.
- **Threat level** = the depth where `u = 1`: the depth at which `m` is exactly a standard fight. As the hero grows, a
  fixed monster gets cheaper by a steady factor per depth, so threat levels outside the run's depths extrapolate at
  that rate.
- **XP** `= 10 × 1.15^(threat level − 1)`, doubled for gatekeepers. XP grows more slowly than danger, so
  out-of-depth weaklings pay little but tough monsters are not exponential jackpots.

`derive` writes each monster's threat level into its definition, and the game pays XP from it through the level's
`ThreatProfile` (`scenes/levels/encounters/threat_profile.tres`, whose constants `derive` also writes). The `Boss`
archetype marks the gatekeeper bonus. `simulate.py monster` evaluates proposed stats before they are authored.

## Floors and gatekeepers

A floor spends a budget of standard fights (`FLOOR["budget"]`, rising with depth) on **packs**, not single monsters, so
roles appear together. Rats lead: rat packs from depth 1 and hordes from depth 2, with fodder pairs, lone standards,
patrols (standard and fodder), sentries (a shooter) and, from depth 3, warbands (two standards and a caster who heals
them); guard posts (brute and shooter) and vanguards (brute, standard and shooter) join from depth 4, and deeper realms
add lone fodder, standard pairs and trios, lone swarmers, swarmer pairs and fodder trios. Each pack has a depth range in
`params.PACKS`. Gatekeeper floors spend a share of the budget (`boss_budget_share`), then the gatekeeper. The simulator
fills a floor as the game does: it draws by weight among the eligible packs the remaining budget affords, until none is
affordable or the floor holds 32 monsters, so a floor ends less than its cheapest pack below its budget. The game also
skips packs that would take a monster past its `MaximumPerFloor` (the Nymph: one a floor); the model's packs are
archetypes and do not see that, so a Prison floor may leave up to a lone Nymph's cost unspent that the model spends.

A pack costs `(N + 1) / 2` times its members fought alone (`model.group_factor`): the hero kills them one by one while
the rest keep attacking. The simulator's group fights match that within 10% for swarms, fodder and mixed melee; packs
with a healer or a shooter come out a little lower. The floor budget is sized for these group costs.

The game spends the same budget through the realm's encounter table, whose entries are packs. A monster costs
`cost growth ^ (threat level − depth)` there: one standard fight on the depth of its threat level, cheaper by the cost
growth for each depth deeper. That closed form stays within about 20% of the model's cost at the depths a monster
spawns. A pack spawns together in one room.

The budget is sized to **attrition** (`params.ATTRITION`, `simulate.py fit`): a regular hero without potions, averaged
over the classes, loses a set share of max health on an ordinary floor and more on a gatekeeper floor (its packs, traps
and gatekeeper). Packs cost in proportion to the budget they spend; traps, gauntlets and gatekeepers do not, so `fit`
solves for the budget line and the gatekeeper floor's share from the class-mean costs at every depth, then reports the
arrival odds. Fodder and swarmers hit softly, so a floor holds many of them. Healing is not sized to refill the
attrition: potions stay scarce, so the hero runs down over a realm and the fountains and gatekeepers set the rhythm.

**Traps** (`params.TRAPS`) count toward attrition. A floor places a few hidden traps (`MapGenerator.MinTraps..MaxTraps`,
audited); playtests measured how often a trap springs on the hero, before the spike plate's trigger shrank to its holes,
so re-measure. A spring is a physical hit of a share of the expected hero's health, rolled against armor like a blow,
which `derive` writes as the trap's `Damage` and `DamagePerDepth`. From depth 2 a share of the slots holds plate traps
instead (`params.LAUNCHERS`: `MapGenerator.TriggeredTrapChance`, audited, times the share of rolls a lane fits); most of
their springs hit, for a dart's share of the expected hero's health, and a fireball deals less because armor never
absorbs it. `derive` writes each launcher's `Damage` and `DamagePerDepth`. The trap count stays authored rather than
spent from the budget: the budget fit leaves room for them.

**Trap gauntlets** (`params.HAZARDS`) count toward attrition too. A run deals two or three
(`GauntletProfile.MinPerRun..MaxPerRun` and `FirstDepth`, audited), one in each band of the ordinary floors from depth 2,
each guarding its floor's descent, and never the same room twice; `simulate` deals them the same way. The model spreads
them over shop and labyrinth floors too, which never hold one, so a gauntlet floor comes a little rarer there and
a little likelier elsewhere. The gauntlet rooms hold darts, fireballs and blades one each (audited from
[the gauntlet rooms](../scenes/levels/rooms/room_types/gauntlets/)). Every row has a safe window and the hero may
wait between rows, so a crossing costs an estimated one hit: a dart or a blade's cut is a physical hit of a share of the
expected hero's health, a fireball the launchers' fraction of a dart, which `derive` writes as each hazard's `Damage`
and `DamagePerDepth`. `fit` shows gauntlet attrition in its own column; re-measure the hits per crossing with
`playtest`, which reports gauntlet hits apart from floor traps.

### Shops

Shops are bounded by their stock instead of simulated. From the Prison on, the first floor of each realm stands a
merchant who sells one finite shelf: the Quartermaster's holds two small potions of different types (healing favored),
two loaves, a scroll, a lockpick and three pieces of local-tier gear. At most that adds one healing potion per realm,
less than a fountain's full heal. The model therefore leaves gold out of the run. `supply` reports the gold a floor
holds, so prices can be checked against it by hand: a thorough hero who clears the Garrison gathers about what the
Quartermaster's shelf costs, plus what selling spare loot (a fifth of its retail value) brings. Re-measure after
changing gold drops, and model shops only if merchants ever restock or sell more than a shelf.

## Experience

XP to the next level is `a × g^(level − 1)`, with `a` and `g` fitted to the expected XP of cleared floors (90% of
monsters killed): clearing depth 1 reaches level 2 and clearing depth 24 reaches level 25 (`XP["level_by_depth"]`).
Gatekeeper floors pay noticeably more than an ordinary floor, the intended bump after each realm. `derive` writes the
ladder into the game's `XpToNextLevel`.

## Class parity

A class passes when each of these holds at regular skill:

- **Realm cost:** the health lost over a realm, its ordinary floors plus the gatekeeper floor, is within ±10% of the
  class mean.
- **Matchups:** floors alone or gatekeepers alone stay within ±25%. Classes are allowed favorable and unfavorable
  fights.
- **Survival:** odds by depth track the other classes at every skill. Deaths come from variance, so a frailer class
  needs a lower average cost than a sturdier one to survive as often. Average parity alone is not enough.

| Class | Start STR/DEX/VIT/INT | Growth per level | Kit | Strong against | Weak against |
| --- | --- | --- | --- | --- | --- |
| Knight | 9 / 8 / 10 / 6 | STR .3, DEX .2, VIT .3, INT .2 | sword, shield (blocks 15%, unmeasured), heavy armor | packs, swarms | gatekeepers |
| Barbarian | 12 / 8 / 10 / 5 | STR .45, DEX .15, VIT .25, INT .15 | one-handed axe, then two-handed, medium armor | gatekeepers | — |
| Rogue | 9 / 15 / 11 / 8 | STR .2, DEX .5, VIT .25, INT .05 | dagger, crossbow opener, light armor, 2% crit per DEX | gatekeepers | packs |
| Mage | 6 / 10 / 10 / 15 | STR .1, DEX .1, VIT .3, INT .5 | wand (kites), Fireball, light armor | packs | gatekeepers |

`parity` reports each class against these bands. Survival splits by speed, since footwork multiplies skill: the light
Rogue gains most from skill and the heavy Knight least. Homing missiles recover only a small share of missed attacks
(`HOMING_ASSIST`): with measured hit rates this low, a larger share made the Mage far safer than the other classes.

## Using the simulator

```bash
uv run scripts/balance/simulate.py                  # hero and standard monster per depth
uv run scripts/balance/simulate.py survival         # arrival odds per skill and class, against the targets
uv run scripts/balance/simulate.py parity           # realm, floor and gatekeeper cost per class
uv run scripts/balance/simulate.py fit              # floor budget sized to ATTRITION, then arrival odds
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
  a job's numbers depend only on `--seed`, not on which `--classes` run. The default 300 runs can stray about 5 points,
  enough to flag a target near the edge of its tolerance; use `--runs 3000` before trusting a close call.
- **Supplies in the game.** `supply` runs a headless probe (`scenes/debug/balance/loot_supply_probe.tscn`) that
  generates seeded floors and samples every loot source on them, then compares healing potions, healing, food and
  gear per floor with `SUPPLY`, and lists the gold a floor holds. `--save` also writes each depth's weapons, as rolled,
  to `weapon_supply.json` for the expected hero's weapon; run `supply --depths 1-24 --save` after changing loot,
  encounters or weapons.

### Game content

`audit` compares the game's `.tres` files with the model's targets; `derive` writes the targets into them. Both take
`--only` with any of `monsters,encounters,weapons,armor,jewelry,curses,classes,supplies,statuses,traps,loot` and `--match <path text>`
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
- **Traps:** the spike plate (`SpikeBurst`), each wall launcher (`scenes/props/traps/launchers/`) and each gauntlet
  hazard (a `WallSpout` or `PendulumBlade` scene in `scenes/props/traps/`) get their damage line as `Damage` and
  `DamagePerDepth`; an elemental shot takes the fireball's. The audit notes trap counts, plate trap odds, the gauntlets
  a run deals and the gauntlet rooms by hazard that differ from `TRAPS`, `LAUNCHERS` and `HAZARDS`.
- **Loot** writes the tier depth bands (`tier_intro`, `tier_band`) into `tier_depth_bands.tres`. The weapon, armor
  and jewelry pools drop each entry in its item tier's band, so their entries carry no window to write.
- **Classes** match the model's classes by `Id`. `derive` writes base attributes, and the hero sheet and stat
  profiles that reproduce the model's level-1 health, mana and crit chance, its attribute growth and the percentage
  each attribute point adds.

All items of one kind and tier get the same numbers, so variety within a tier has to come from affixes, guards,
weight or attack patterns.

**The monsters are eased on purpose.** They were derived against an earlier, sparser loot supply and deliberately not
re-derived after floors grew denser, so the game plays easier than the targets; `audit` lists the gap. The threat
profile's `CostGrowth` and the XP ladder stay with the eased monsters, since their targets assume re-derived ones, while
the floor budget follows `fit`.

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
  `ENGAGEMENT["uptime"]` to it. Move the tiers toward the measured values, then retune the targets with `survival`.
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
  healing potions found by size against the model's supply, and gear found. Per monster type it reports the hero's
  contacts to kill one (blocked ones count; mean and range), one-hit kills, seconds from the first contact to the
  kill and the damage the type dealt, then floor trap and gauntlet damage and hits, and the first weapon picked up. A
  monster is named by its death, else by its node name. It then lists every hidden item type found (potions, scrolls,
  jewelry) by its real name, how many were found and used, and how each was identified, the cursed items the hero put
  on, and each timed effect on the hero (how often it started, how long it lasted, whether it expired or was cured, and
  the health poison took), followed by the same skill profile as `calibrate`. A playtest measures the whole loop the
  model predicts, so compare its floors with `survival` at the matching skill tier, and its fight report with the role
  targets and floor attrition.

## Remaining work

The model stays in Python, so it has one implementation. The game holds the derived numbers and only the short
formulas that use them: XP from threat level, floor budgets from depth, and the XP ladder. Missing content blocks no
step: targets exist per archetype and gear tier, not per item, so a new monster is authored by choosing its archetype
and depth, and a new item by choosing its kind and tier; `derive` fills in the numbers.

1. **Calibrate the first realm.** Measure the novice and regular skill tiers from recorded playtests of the Garrison,
   where most difficulty targets fall, and check a playtest's fight report against the role targets and floor attrition.
2. **Re-key the arrival targets** (`TARGETS`, and `XP["level_by_depth"]` if needed) to five-floor realms: the first
   gatekeeper on depth 5 and the win after depth 25.
3. **The Garrison's depths.** Its ordinary monsters are authored for depth 2 and the Warden for depth 3, from the
   three-floor realm, against the model's depths 3 and 5; `audit` notes them. They move when the monsters are next
   re-derived, which ends the deliberate easing above.
4. **First-drop pacing.** Pick the loot change that keeps the first weapon found a 10–20% upgrade over the worn starter:
   floor 1–2 weapons rolling mostly Poor or Normal, or tier 1 dropping from depth 2. A stronger starter is ruled out;
   gatekeepers already drop their own tier.
5. **Role targets per class.** Decide whether the role targets must hold for every class or only on average; parity
   keeps realm costs within 10% today.
6. **Realm by realm.** Each realm gets its monsters, packs and gear tier through `derive`, passes `audit`, and is
   playtested before the next one starts.
7. **Unmodeled systems.** The rest of [Not modeled yet](#not-modeled-yet) enter `params.py` before they are built.
   Shops are the exception: their finite stock bounds them (see [Shops](#shops)).

## Not modeled yet

Gold (shops are bounded by stock instead; see [Shops](#shops)), stealth and skipped fights, partial floor clears, the
cost of harmful potions, spoiled food, blindness and confusion, and the gatekeepers' individual attacks: each
gatekeeper is the archetype above. Nor are the special monster capabilities: nests, lobs that ignore cover, ground
pools, ambushes, thefts, revives, flight's evasion and per-monster floor caps; each such monster starts on its nearest
archetype until harness traces and playtests calibrate it. Add them to the model before relying on it for those
systems.
