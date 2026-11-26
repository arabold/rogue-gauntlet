"""
Design parameters of the Rogue Gauntlet balance model.

Every number the game's balance derives from lives here. `docs/specs/balance-model.md` explains the
model; `simulate.py` turns these parameters into target tables and difficulty estimates, and `derive` writes the
game's values from them.

Conventions: `(a, b)` pairs are values at depth 1 and depth 24, interpolated linearly. Multipliers are
relative to the reference hero or the standard monster at the same depth. Times are seconds.
"""
import json
from pathlib import Path

DEPTHS = 25
REALM_SIZE = 5
LEVEL_CAP = 25

# --- Difficulty dial: the standard monster against the reference hero ---------------------------
STANDARD = dict(
    hits_to_kill=(5.5, 7.0),   # landed reference sword hits (no crit) to kill a standard monster; misses come on top
    hits_to_die=(19.0, 30.0),   # standard monster hits (after the hero's armor) to kill the reference hero
    spread=0.35,               # damage range is mean * (1 -/+ spread)
    # Ordinary monsters (not gatekeepers) hold at least this many average hits of the heaviest class weapon at their
    # authored depth, so a one-blow kill takes a high roll or a crit instead of being routine.
    min_hits=1.25,
)

# How a fight opens: monsters notice the hero `gap` metres away; the hero moves at `hero_speed` (the stat sheet's `BaseSpeed`,
# which `derive` writes) and walks up to shooters at `close_speed`, zig-zagging at two thirds of it; at most `melee_cap` melee
# monsters reach the hero at once.
# `rest` = seconds of exploring between packs, during which mana regenerates (health does not).
# Blows land one at a time: the hero's first `strike` seconds after a monster comes within reach (a swing's contact),
# a monster's first its archetype's `windup` seconds after it does, then each every attack cycle.
# `uptime`: the share of its attack cycles, the first included, an ordinary melee monster actually starts while
# fighting; the hero's footwork, hits and guard keep the rest from starting. Shooters, casters and the gatekeeper attack
# on every cycle. Playtests measured 10-43% before monster attacks committed and hits stopped pushing them back, and
# 46-76% since (61% over three logs, 76% in the one log with the current monster speeds). `playtest` reports it per
# archetype.
# `footwork`: stepping out of reach is most of melee avoidance, so it needs a monster slower than the hero. SKILL's melee
# avoidance holds in full against monsters at most this share of the hero's speed and fades linearly to nothing
# against one as fast as the hero. Fitted to one player's four playtests: rats avoided 64% at 0.56 of the hero's speed
# and 10% at 0.87, with the same bite.
ENGAGEMENT = dict(gap=4.0, hero_speed=6.0, close_speed=4.0, melee_cap=3, rest=20.0, strike=0.15, uptime=0.75,
                  footwork=0.4)

# --- Attributes: what one point buys ----------------------------------------------------------
ATTRIBUTES = dict(
    hp_base=50.0,              # max health at level 1 with 10 Vitality
    hp_per_level=7.0,          # max health per level, before Vitality
    hp_per_vit=0.04,           # max health +4% per Vitality point above 10
    damage_per_str=0.03,       # physical damage +3% per Strength point above 10
    crit_per_dex=0.01,         # critical chance per Dexterity point (crits deal x2)
    spell_per_int=0.03,        # missile and spell damage +3% per Intelligence point above 10
    mana_base=20.0,
    mana_per_int=5.0,
    mana_regen=0.01,           # share of max mana regenerated per second
)
CRIT_MULTIPLIER = 2.0
HOMING_ASSIST = 0.1           # share of missed attacks a homing projectile still lands
MAGIC_ARMOR_SHARE = 0.0       # share of armor that applies to missiles and spells (0 = magic ignores armor)
# Armor blocks a uniform roll in [0, armor] from a physical hit, but a hit always deals at least `minimum` (a smaller hit
# deals itself), as in Shattered Pixel Dungeon and Dungeon Crawl. The game's ArmorRules holds the same minimum; `audit`
# checks it.
ARMOR = dict(minimum=1.0)

# --- Gear -------------------------------------------------------------------------------------
GEAR = dict(
    # Reference one-handed sword mean hit at depth 1 (a T1 sword rolls 2-5) and its growth per depth: about 3x from tier 1
    # to tier 5, like Shattered Pixel Dungeon's base weapons; quality and affixes add the rest, as Diablo II's magic
    # modifiers do. Armor follows the same curve.
    weapon_hit=(3.7, 1.05),
    found_bonus=(0.0, 0.25),   # quality and affixes on the armor a hero is expected to wear (weapons: WEAPON_SUPPLY)
    tier_intro=[1, 6, 11, 16, 21],
    tier_band=8,               # a tier drops from its introduction depth for this many depths
    tier_lag=1,                # depths before the expected hero wears a newly introduced tier
    # A class's starting weapons are worn (Poor): this share of a tier 1 weapon's damage. The expected hero keeps them
    # until the floors' loot (WEAPON_SUPPLY) likely gave it something better (model.expected_weapon).
    starter=0.7,
    # Wood is a terrible weapon: wooden melee weapons (practice swords, clubs, mallets: the `wooden_weapon` roll table)
    # deal and stagger this share of their kind and tier, 1-2 damage at tier 1. Crossbows, wands and staves are wooden
    # by nature and keep full strength.
    wooden=0.25,
    # Armor of a Normal medium piece, as a share of the reference sword's mean hit at its tier's depth, so armor numbers
    # stay in the weapons' range: a T1 medium body piece holds 3, a helmet and a shield 1. As in Diablo II, body armor
    # holds about three helmets and a shield about one helmet: a shield's block does the rest of its work.
    slot_armor=dict(body=0.75, helmet=0.25, shield=0.30),
    # Armor weight: `armor` multiplier, `avoid` = the share of landed hits its dodge distance adds or removes, and the
    # body piece's movement `speed` and `dodge` distance factors (helmets keep both). Medium body armor shortens the dodge
    # by a third, heavy halves it and slows its wearer 15%, which costs footwork (ENGAGEMENT) on top. The avoid shares
    # are unmeasured guesses for those rules.
    weight=dict(light=dict(armor=0.6, avoid=+0.05, speed=1.05, dodge=1.0),
                medium=dict(armor=1.0, avoid=-0.05, speed=1.0, dodge=0.67),
                heavy=dict(armor=1.3, avoid=-0.15, speed=0.85, dodge=0.5)),
)

# Weapon kinds relative to the reference sword: hit multiplier, seconds per attack, hands, and `spread`: the damage range
# is mean * (1 -/+ spread). Ranges are wide, as in Diablo II and Shattered Pixel Dungeon: daggers +-35%, one-handed
# weapons and crossbows +-40%, two-handed melee +-60%, so a big hit is possible but not routine (Diablo II's Great Axe
# rolls 9-30, Shattered's Greataxe 5-45). Magic foci keep +-25%, a missile's steady damage.
# `cleave` = average monsters hit per swing while two or more are engaged; `darts` split a volley, each dealing
# `dart_scale` of the hit. One-handed melee kinds share the sword's damage per second and differ in hit size (bigger
# hits cut through armor); two-handed melee kinds add cleave. Ranged kinds hit less, paid for by `kite` (SKILL):
# the crossbow is set so a rogue who never draws the dagger loses more health than the class's crossbow-then-dagger
# plan, the wand so the mage sits at parity. Kinds no class starts with are not in the parity runs.
WEAPONS = {
    "sword_1h": dict(hit=1.00, cycle=0.50, hands=1, spread=0.40),
    "dagger":   dict(hit=0.72, cycle=0.36, hands=1, spread=0.35),
    "axe_1h":   dict(hit=1.15, cycle=0.575, hands=1, spread=0.40),
    "blunt_1h": dict(hit=1.30, cycle=0.65, hands=1, spread=0.40),
    "sword_2h": dict(hit=1.35, cycle=0.65, hands=2, spread=0.60, cleave=1.20),
    "axe_2h":   dict(hit=1.50, cycle=0.72, hands=2, spread=0.60, cleave=1.15),
    "blunt_2h": dict(hit=1.75, cycle=0.84, hands=2, spread=0.60, cleave=1.10),
    "spear_2h": dict(hit=1.55, cycle=0.70, hands=2, spread=0.60),  # thrusts one target; reach is not modeled
    "crossbow": dict(hit=1.00, cycle=0.95, hands=2, spread=0.40, ranged=True),
    "wand":     dict(hit=1.00, cycle=0.60, hands=1, spread=0.25, ranged=True, homing=True, darts=3, dart_scale=0.24),
    "staff":    dict(hit=1.00, cycle=0.60, hands=2, spread=0.25, ranged=True, homing=True, darts=4, dart_scale=0.27),  # volley 1.5x a wand's
}

# Spells scale off the focus's hit like missiles. `targets` = average monsters hit in a pack.
SPELLS = {
    "fireball": dict(scale=1.3, targets=2.2, mana=24.0, cast=0.9),
}

# --- Classes ------------------------------------------------------------------------------------
# Classes differ by attributes, kit and defense bonuses only; attributes scale shared curves, so a class's
# strengths keep their proportion at every depth. `growth` = attribute points gained per level.
# `guard` (optional) removes that share of the melee and ranged hits skill lets through (a shield's block). `uses` =
# the weapon kinds the class picks up in place of its own: the expected hero wields the best of them it has found.
CLASSES = {
    # The yardstick monsters are derived from; tuned to sit at the class mean.
    "reference": dict(attrs=dict(STR=12, DEX=11, VIT=12, INT=10),
                      growth=dict(STR=0.25, DEX=0.25, VIT=0.25, INT=0.25),
                      weapon="sword_1h", uses=["sword_1h", "axe_1h", "blunt_1h"], armor="medium", shield=False),
    # Heavy armor and a shield; pays with low Strength and the heavy armor's lost footwork. Its shield holds about a
    # helmet's armor, as in Diablo II, and blocks 15% of the hits skill lets through (unmeasured).
    "knight":    dict(attrs=dict(STR=9, DEX=8, VIT=10, INT=6),
                      growth=dict(STR=0.3, DEX=0.2, VIT=0.3, INT=0.2),
                      weapon="sword_1h", uses=["sword_1h", "axe_1h", "blunt_1h"], armor="heavy", shield=True,
                      guard=0.15),
    # Two-handed cleaving damage once found; starts with a one-handed axe. No shield, medium armor.
    "barbarian": dict(attrs=dict(STR=12, DEX=8, VIT=10, INT=5),
                      growth=dict(STR=0.45, DEX=0.15, VIT=0.25, INT=0.15),
                      weapon="axe_2h", starter="axe_1h", uses=["axe_1h", "sword_1h", "blunt_1h", "axe_2h", "sword_2h", "blunt_2h",
                                                                  "spear_2h"],
                      armor="medium", shield=False),
    # Fast dagger crits after crossbow openers; light armor, quick feet.
    "rogue":     dict(attrs=dict(STR=9, DEX=15, VIT=11, INT=8),
                      growth=dict(STR=0.2, DEX=0.5, VIT=0.25, INT=0.05),
                      weapon="dagger", opener="crossbow", uses=["dagger", "sword_1h", "axe_1h", "blunt_1h"],
                      armor="light", shield=False,
                      profile=dict(crit_per_dex=0.02)),
    # Free homing missiles at range plus mana-limited area spells; frailest body.
    "mage":      dict(attrs=dict(STR=6, DEX=10, VIT=10, INT=15),
                      growth=dict(STR=0.1, DEX=0.1, VIT=0.3, INT=0.5),
                      weapon="wand", spell="fireball", uses=["wand", "staff"], armor="light", shield=False),
}
PARITY_TOLERANCE = 0.10        # every class's realm cost (three floors and the gatekeeper) within +-10% of the class mean
MATCHUP_TOLERANCE = 0.25       # floors alone or gatekeepers alone may differ more: classes have favorable matchups

# --- Monster archetypes, relative to the standard monster at the same depth ------------------------
# armor = share of the reference hero's mean hit (it blocks half that on average); kind decides avoidance; support heals
# allies.
# Fodder and swarmers hit softly so floors hold many of them: armor shrugs off most of a bite, a swarmer's venom does not.
# `windup` = seconds from an attack's start to its contact, which `derive` sets through the primary attack's Duration.
# Rats bite in 0.25 s and skeletons strike in 0.35 s (the Normal tier's floor is 0.25 s); shooters, casters and the
# gatekeeper keep their longer, authored tells.
ARCHETYPES = {
    "fodder":   dict(hp=0.55, armor=0.00, damage=0.325, cycle=2.0, windup=0.35, kind="melee", speed=5.0),
    "swarmer":  dict(hp=0.36, armor=0.00, damage=0.35, cycle=1.9, windup=0.25, kind="melee", speed=6.5,
                     status=(0.35, 1.2)),   # venom passes armor, so heavy armor shrugs off less of a swarm
    "standard": dict(hp=1.00, armor=0.10, damage=1.00, cycle=2.2, windup=0.35, kind="melee", speed=4.5),
    "brute":    dict(hp=1.36, armor=0.35, damage=1.30, cycle=2.5, windup=0.45, kind="melee", speed=4.0, guard=0.25),
    "shooter":  dict(hp=0.55, armor=0.05, damage=0.95, cycle=3.2, windup=0.63, kind="ranged", speed=4.0),
    "caster":   dict(hp=0.55, armor=0.00, damage=0.35, cycle=3.3, windup=0.60, kind="homing", speed=3.5,
                     darts=3, support=dict(heal=0.30, cycle=9.0)),
    "boss":     dict(hp=5.00, armor=0.35, damage=1.00, cycle=2.4, windup=0.51, kind="melee", speed=3.5, guard=0.25,
                     phases=[(0.65, 1.25), (0.30, 1.55)], area=0.35),
}

# Hit reactions. A monster attack commits at `commit` of its wind-up: from there until its contact ends, hits no longer
# stop it, only a poise break does, so the hero must dodge, parry or block a committed swing. Poise ignores damage and
# armor: each hit removes `hit` times its weapon kind's hit share (WEAPONS' `hit`; a missile its dart's share), `blunt`
# times more for blunt weapons, and a monster's meter holds `hits` such reference hits by archetype (0: no poise; the
# hit recovery of the hurtbox instead). Each break leaves the next meter `growth` larger until `memory` seconds pass
# without one, so staggers cannot be chained. Only the weapon kinds in `knockback` push on a stagger, by that many
# metres; the hero's melee secondary and signature monster attacks author their own.
POISE = dict(
    commit=0.5,
    hit=10.0,
    blunt=1.3,
    hits=dict(fodder=2, swarmer=0, standard=3, brute=4, shooter=2, caster=2, boss=8),
    growth=0.5,
    memory=10.0,
    knockback=dict(blunt_2h=1.1),
)

# Packs a floor spends its budget on. A pack spawns together in one room; it may appear from `first` to `last`
# depth (0 = to the end). Rats lead: depth 1 opens with small rat groups, depth 2 brings hordes that can overwhelm a
# careless hero with only occasional skeletons, and mixed groups take over from depth 3. From the Prison (depth 6) on,
# swarms are bats, a lone fodder is a thieving nymph, and orc grunts come in pairs and threes. In the Sewer (depth 11)
# rats return in packs and hordes, acid blobs ooze in alone or in twos and threes, snakes lie alone or in pairs, and
# a spitting slime keeps watch alone.
PACKS = [
    dict(name="rat pack",      first=1, last=0, weight=6, members=["swarmer"] * 3),
    dict(name="rat horde",     first=2, last=0, weight=3, members=["swarmer"] * 5),
    dict(name="fodder pair",   first=1, last=0, weight=2, members=["fodder", "fodder"]),
    dict(name="lone standard", first=1, last=0, weight=2, members=["standard"]),
    dict(name="patrol",        first=2, last=0, weight=2, members=["standard", "fodder"]),
    dict(name="sentry",        first=2, last=0, weight=1, members=["shooter"]),
    dict(name="warband",       first=3, last=0, weight=2, members=["standard", "standard", "caster"]),
    dict(name="guard post",    first=4, last=0, weight=2, members=["brute", "shooter"]),
    dict(name="vanguard",      first=4, last=0, weight=2, members=["brute", "standard", "shooter"]),
    dict(name="lone fodder",   first=6, last=0, weight=2, members=["fodder"]),
    dict(name="standard pair", first=6, last=0, weight=3, members=["standard"] * 2),
    dict(name="standard trio", first=8, last=0, weight=2, members=["standard"] * 3),
    dict(name="lone swarmer",  first=11, last=0, weight=2, members=["swarmer"]),
    dict(name="swarmer pair",  first=12, last=0, weight=2, members=["swarmer"] * 2),
    dict(name="fodder trio",   first=12, last=0, weight=2, members=["fodder"] * 3),
]

# --- Floors, supplies and experience ------------------------------------------------------------
# Floor attrition: the share of max health a hero of REFERENCE_SKILL loses to a floor without potions, averaged over
# the classes: an ordinary floor and a gatekeeper floor (its packs, traps and gatekeeper). `simulate.py fit` sizes
# FLOOR's budget and gatekeeper share to it; supplies and the arrival odds follow. Chosen 2026-11-14 to keep the
# difficulty playtests liked (ordinary floors cost about 60%) with gatekeeper floors the walls.
ATTRITION = dict(floor=0.60, gatekeeper_floor=0.75)
# Floor traps (spike plates: SpikeBurst): `per_floor` = traps placed per floor (MapGenerator.MinTraps..MaxTraps, audited), `triggers`
# = springs per trap on the hero per floor (measured: 26 over 30 playtest floors of about 1.5 traps), `damage` = a
# spring's physical hit as a share of the expected hero's health, rolled against armor like a blow. `derive` writes a
# trap's damage on depth 1 and per depth. Traps count toward a floor's attrition, so the budget leaves room for them.
TRAPS = dict(per_floor=1.5, triggers=0.58, damage=0.09)
# Plate traps (a concealed plate and the wall launcher across from it: MapGenerator.TriggeredTraps) fill a share of the
# trap slots from `first_depth` (the shallowest profile's FirstDepth): each slot rolls `chance`
# (MapGenerator.TriggeredTrapChance, audited) and a lane fits about `lane_fit` of those (the generated-floor sweep). Of
# their springs `hits` land (estimated: a hero crossing at full speed can clear the lane). A dart is a physical hit of
# `damage` of the expected hero's health; a fireball deals `fire_scale` of it, since armor never absorbs it. `derive`
# writes each launcher's Damage and DamagePerDepth.
LAUNCHERS = dict(first_depth=2, chance=0.35, lane_fit=0.9, hits=0.75, damage=0.055, fire_scale=0.75)
FLOOR = dict(
    budget=(7.3, 11.4),        # standard-monster equivalents per floor, packs costed as groups (`fit` sizes it)
    boss_budget_share=0.45,    # a boss floor spends this share of the budget before the boss (`fit` sizes it)
    max_monsters=32,           # a floor stops adding packs once it holds this many (RealmEncounterFactory.MaximumMonsters)
    clear_share=0.9,           # share of each floor's monsters an average hero kills
)
SUPPLY = dict(
    healing_potions=(0.45, 1.0),  # expected healing potions found per floor (Poisson), depths 1 to 24; measured with `supply`
    # Potions heal fixed points, so a size is worth less as the hero's health grows and the next size takes over. A size
    # drops from `intro` to `last` (0 = to the end), healing `heal` of the expected hero's health where it arrives;
    # `weight` is its share among the sizes eligible at a depth. Small potions stop once large ones are near, or late
    # floors fill with potions that heal a tenth of the hero. `derive` writes every potion pool entry's window and the
    # healing potions' weights.
    potion_sizes=dict(small=dict(intro=1, last=12, heal=0.6, weight=4),
                      medium=dict(intro=9, last=0, heal=0.7, weight=4),
                      large=dict(intro=17, last=0, heal=1.0, weight=4)),
    food=1.0,                  # expected food per floor
    # Food heals fixed points by its `Portion`, as a share of a level 1 hero's health: minor healing that fades deeper
    # down. Fruit and drinks are small, bread and cheese medium, meat large.
    food_portions=dict(small=0.06, medium=0.10, large=0.15),
    fountain_depths=[5, 12, 19],  # one full heal on these floors (2-3 per run, seeded in game)
    starting_potions=0,        # healing potions every class starts with (equal for parity)
)
# Harmful potions and spoiled food. Poison takes fixed points, as healing gives them, in `poison_ticks` whole ticks over
# `poison_seconds`: a poison potion takes `poison` of what the healing potion of its size gives, spoiled food
# `food_poison` times what its portion heals. Any healing potion cures poison and blindness. Blindness and confusion
# last, by potion size, long enough that a pack finds the hero before they pass (Rogue blinds for hundreds of turns and
# confuses for about twenty). Rat venom is priced with the rat (ARCHETYPES' `status`).
STATUS = dict(
    poison=0.5,
    food_poison=2.0,
    poison_seconds=10.0,
    poison_ticks=5,
    blindness=dict(small=30, medium=45, large=60),
    confusion=dict(small=15, medium=20, large=30),
)
# Jewelry comes in tiers 1-5 that drop in the gear tier bands. A piece is sized at its tier's on-curve depth as a share of
# the stat it raises: armor against the standard monster's mean hit, health against the hero's, damage against the
# reference weapon's range (added to both ends). Attribute pieces add points that grow by tier. Amulets give more than
# rings. Jewelry counts toward the expected hero's `found_bonus`, like quality and affixes.
JEWELRY = dict(
    slots=dict(ring=1.0, amulet=1.5),
    armor=0.25,                # Protection, a share of the reference sword's mean hit like slot armor
    health=0.10,               # Health
    damage=0.20,               # Savagery
    frailty=-0.20,             # Frailty (always cursed): health lost
    attribute=(2, 1),          # Strength, Dexterity, Vitality, Mind: points at tier 1 and per tier above
)
# Curses: this share of dropped equipment is cursed, carrying a hidden penalty while worn. Penalties take points, like
# the bonuses beside them (damage, armor, health, Strength, Dexterity), in tiers 1-5 that roll in the gear tier bands,
# each up to `penalty` times what a ring of that tier gives (see JEWELRY) and at least `floor` of that, so a curse
# outweighs an item of its tier. Dread, which lowers damage, goes to `damage_penalty` instead, so its strongest roll
# takes over half of an average hit. Speed is a percentage everywhere on gear, so its penalty is too and needs no tiers.
CURSES = dict(
    chance=0.25,
    penalty=2.0,
    damage_penalty=3.0,
    floor=0.67,
    speed_percent=(0.10, 0.15),    # Lethargy
)
XP = dict(
    standard_xp=10.0,          # XP of a threat-level-1 standard monster
    growth=1.15,               # XP growth per threat level; below the cost growth, so XP rises slower than danger
    boss_multiplier=2.0,       # gatekeepers pay this much more than their threat suggests
    level_by_depth={1: 2, 24: 25},  # target level after clearing these depths; fits the XP ladder
)

# --- Player skill, measured in combat-arena playtests (`simulate.py calibrate`) ------------------------
# avoid = share of a monster's attacks the hero avoids (moving away, dodging, guarding, interrupting), melee against a
# monster slow enough for full footwork (ENGAGEMENT); efficiency = share of the hero's attacks that land, swings at
# nothing included; potion_at = health share that triggers a potion; potion_known = share of found healing potions
# the hero can identify and use.
# Preliminary: "skilled" is one developer's profile. Avoidance comes from the combat arena (2026-11-12: 79% of melee
# and 69% of ranged attacks over ~110 monster attacks, against slow monsters); efficiency from four floor playtests
# (2026-11-14: 60% of 452 swings, where packs crowd the hero's swings). The other tiers keep their earlier spacing,
# with the share of hits let through and the hit rate scaled to match. Homing uses the ranged scale: the measured 92%
# mostly reflected weak Skeleton Mage casts, not skill. `kite`: a hero whose main weapon is ranged backs off between shots from a melee
# monster slower than itself, so this share of the melee hits avoidance lets through never land, scaled by how much
# slower the monster is (`1 - its speed / hero_speed`); rats outrun the hero and cannot be kited.
# Unmeasured: record a playtest with a ranged class to fit it.
SKILL = {
    "novice":  dict(avoid=dict(melee=0.51, ranged=0.35, homing=0.27, area=0.35),
                    efficiency=0.42, potion_at=0.20, potion_known=0.5, kite=0.2),
    "regular": dict(avoid=dict(melee=0.67, ranged=0.55, homing=0.43, area=0.55),
                    efficiency=0.53, potion_at=0.30, potion_known=0.8, kite=0.4),
    "skilled": dict(avoid=dict(melee=0.79, ranged=0.69, homing=0.55, area=0.69),
                    efficiency=0.60, potion_at=0.35, potion_known=0.95, kite=0.5),
    "expert":  dict(avoid=dict(melee=0.88, ranged=0.80, homing=0.67, area=0.80),
                    efficiency=0.64, potion_at=0.40, potion_known=1.0, kite=0.6),
}
REFERENCE_SKILL = "regular"

# Difficulty targets: probability of arriving at a depth (25 = beating the depth-24 gatekeeper).
# Proposed reading of "hard like Rogue/NetHack/Shattered Pixel Dungeon"; tune them like any other parameter.
TARGETS = {
    "novice":  {2: 0.75, 3: 0.35, 4: 0.03},   # most die on depths 1-3; the first gatekeeper stops nearly all
    "regular": {4: 0.40, 7: 0.10},            # beating the first gatekeeper is an achievement
    "skilled": {4: 0.85, 13: 0.35, 25: 0.03},
    "expert":  {13: 0.85, 25: 0.35},          # experts win about a third of their runs
}
TARGET_TOLERANCE = 0.10        # absolute probability slack before a target is flagged
AUDIT_TOLERANCE = 0.10         # relative difference before `simulate.py audit` flags an authored value

# Weapons the floors drop per depth, measured from the game's own loot by `simulate.py supply --save`: for each depth,
# [kind, mean hit per swing as rolled (quality, affixes, combo), expected count per floor]. Re-measure after loot,
# encounter or weapon changes.
WEAPON_SUPPLY_PATH = Path(__file__).with_name("weapon_supply.json")
WEAPON_SUPPLY = {int(depth): entries for depth, entries in json.loads(WEAPON_SUPPLY_PATH.read_text())["depths"].items()}
