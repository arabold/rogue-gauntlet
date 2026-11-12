"""
Design parameters of the Rogue Gauntlet balance model.

Every number the game's balance derives from lives here. `docs/specs/balance-model.md` explains the
model; `simulate.py` turns these parameters into target tables and difficulty estimates. Values are
targets for content still to be authored, not a mirror of the current `.tres` data.

Conventions: `(a, b)` pairs are values at depth 1 and depth 24, interpolated linearly. Multipliers are
relative to the reference hero or the standard monster at the same depth. Times are seconds.
"""

DEPTHS = 24
REALM_SIZE = 3
LEVEL_CAP = 25

# --- Difficulty dial: the standard monster against the reference hero ---------------------------
STANDARD = dict(
    hits_to_kill=(3.5, 5.0),   # reference hero hits (no crit) to kill a standard monster
    hits_to_die=(9.0, 5.0),    # standard monster hits (after the hero's armor) to kill the reference hero
    cycle=2.2,                 # seconds between a monster's attacks (wind-up plus cooldown)
    spread=0.35,               # damage range is mean * (1 -/+ spread)
    speed=2.8,                 # m/s, for approach time against ranged heroes
)

# How a fight opens: monsters notice the hero `gap` metres away; heroes walk up to shooters at
# `close_speed`; at most `melee_cap` melee monsters reach the hero at once.
# `rest` = seconds of exploring between packs, during which mana regenerates (health does not).
ENGAGEMENT = dict(gap=4.0, close_speed=5.0, melee_cap=3, rest=20.0)

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
HOMING_ASSIST = 0.3           # share of missed attacks a homing projectile still lands
MAGIC_ARMOR_SHARE = 0.0       # share of armor that applies to missiles and spells (0 = magic ignores armor)

# --- Gear -------------------------------------------------------------------------------------
GEAR = dict(
    weapon_hit=(6.0, 1.08),    # reference one-handed sword mean hit at depth 1, growth per depth
    weapon_spread=0.4,         # weapon damage range is mean * (1 -/+ spread)
    found_bonus=(0.0, 0.25),   # quality and affixes on the gear a hero is expected to wear
    tier_intro=[1, 6, 11, 16, 21],
    tier_band=8,               # a tier drops from its introduction depth for this many depths
    tier_lag=1,                # depths before the expected hero wears a newly introduced tier
    # Armor of a Normal medium piece, as a share of the standard monster's mean hit at that depth.
    slot_armor=dict(body=0.30, helmet=0.15, shield=0.22),
    # Armor weight: armor multiplier, and the share of landed hits its footwork removes (negative adds hits).
    weight=dict(light=(0.5, +0.05), medium=(1.0, 0.0), heavy=(1.3, -0.10)),
)

# Weapon kinds relative to the reference sword: hit multiplier, seconds per attack, hands.
# `cleave` = average monsters hit per swing while two or more are engaged; `darts` split a volley, each dealing
# `dart_scale` of the hit. One-handed melee kinds share the sword's damage per second and differ in hit size (bigger
# hits cut through armor); two-handed melee kinds add cleave. Kinds no class starts with are not in the parity runs.
WEAPONS = {
    "sword_1h": dict(hit=1.00, cycle=0.50, hands=1),
    "dagger":   dict(hit=0.72, cycle=0.36, hands=1),
    "axe_1h":   dict(hit=1.15, cycle=0.575, hands=1),
    "blunt_1h": dict(hit=1.30, cycle=0.65, hands=1),
    "sword_2h": dict(hit=1.35, cycle=0.65, hands=2, cleave=1.20),
    "axe_2h":   dict(hit=1.50, cycle=0.72, hands=2, cleave=1.15),
    "blunt_2h": dict(hit=1.75, cycle=0.84, hands=2, cleave=1.10),
    "spear_2h": dict(hit=1.55, cycle=0.70, hands=2),            # thrusts one target; reach is not modeled
    "crossbow": dict(hit=1.30, cycle=0.95, hands=2, ranged=True),
    "wand":     dict(hit=1.00, cycle=0.60, hands=1, ranged=True, homing=True, darts=3, dart_scale=0.25),
    "staff":    dict(hit=1.20, cycle=0.60, hands=2, ranged=True, homing=True, darts=4, dart_scale=0.19),
}

# Spells scale off the focus's hit like missiles. `targets` = average monsters hit in a pack.
SPELLS = {
    "fireball": dict(scale=1.3, targets=2.2, mana=24.0, cast=0.9),
}

# --- Classes ------------------------------------------------------------------------------------
# Classes differ by attributes, kit and defense bonuses only; attributes scale shared curves, so a class's
# strengths keep their proportion at every depth. `growth` = attribute points gained per level.
# `guard` (optional) removes that share of the melee and ranged hits skill lets through (a shield's block).
CLASSES = {
    # The yardstick monsters are derived from; tuned to sit at the class mean.
    "reference": dict(attrs=dict(STR=12, DEX=11, VIT=12, INT=10),
                      growth=dict(STR=0.25, DEX=0.25, VIT=0.25, INT=0.25),
                      weapon="sword_1h", armor="medium", shield=False),
    # Heavy armor and a shield; pays with low Strength and the heavy armor's lost footwork.
    "knight":    dict(attrs=dict(STR=9, DEX=8, VIT=10, INT=6),
                      growth=dict(STR=0.3, DEX=0.2, VIT=0.3, INT=0.2),
                      weapon="sword_1h", armor="heavy", shield=True, guard=0.05),
    # Two-handed cleaving damage; no shield, medium armor.
    "barbarian": dict(attrs=dict(STR=12, DEX=8, VIT=10, INT=5),
                      growth=dict(STR=0.45, DEX=0.15, VIT=0.25, INT=0.15),
                      weapon="axe_2h", armor="medium", shield=False),
    # Fast dagger crits after crossbow openers; light armor, quick feet.
    "rogue":     dict(attrs=dict(STR=9, DEX=15, VIT=11, INT=8),
                      growth=dict(STR=0.2, DEX=0.5, VIT=0.25, INT=0.05),
                      weapon="dagger", opener="crossbow", armor="light", shield=False,
                      profile=dict(crit_per_dex=0.02)),
    # Free homing missiles at range plus mana-limited area spells; frailest body.
    "mage":      dict(attrs=dict(STR=6, DEX=10, VIT=10, INT=15),
                      growth=dict(STR=0.1, DEX=0.1, VIT=0.3, INT=0.5),
                      weapon="wand", spell="fireball", armor="light", shield=False),
}
PARITY_TOLERANCE = 0.10        # every class's realm cost (three floors and the gatekeeper) within +-10% of the class mean
MATCHUP_TOLERANCE = 0.25       # floors alone or gatekeepers alone may differ more: classes have favorable matchups

# --- Monster archetypes, relative to the standard monster at the same depth ------------------------
# armor = share of the reference hero's mean hit; kind decides avoidance; support heals allies.
ARCHETYPES = {
    "fodder":   dict(hp=0.55, armor=0.00, damage=0.65, cycle=2.0, kind="melee", speed=3.2),
    "swarmer":  dict(hp=0.45, armor=0.00, damage=0.50, cycle=1.9, kind="melee", speed=4.2,
                     status=(0.35, 0.6)),
    "standard": dict(hp=1.00, armor=0.10, damage=1.00, cycle=2.2, kind="melee", speed=2.8),
    "brute":    dict(hp=1.70, armor=0.35, damage=1.30, cycle=2.5, kind="melee", speed=2.6, guard=0.25),
    "shooter":  dict(hp=0.80, armor=0.05, damage=0.95, cycle=3.2, kind="ranged", speed=2.8),
    "caster":   dict(hp=0.90, armor=0.00, damage=0.80, cycle=3.3, kind="homing", speed=2.4,
                     darts=3, support=dict(heal=0.30, cycle=9.0)),
    "boss":     dict(hp=6.00, armor=0.35, damage=1.60, cycle=2.4, kind="melee", speed=2.6, guard=0.25,
                     phases=[(0.65, 1.25), (0.30, 1.55)], area=0.35),
}

# Packs a floor spends its budget on: (name, first depth, weight, members).
PACKS = [
    ("lone standard",      1, 4, ["standard"]),
    ("fodder pair",        1, 3, ["fodder", "fodder"]),
    ("patrol",             1, 3, ["standard", "fodder"]),
    ("sentry",             1, 2, ["shooter"]),
    ("guard post",         2, 2, ["brute", "shooter"]),
    ("rat swarm",          2, 2, ["swarmer", "swarmer", "swarmer"]),
    ("warband",            2, 2, ["standard", "standard", "caster"]),
    ("vanguard",           4, 2, ["brute", "standard", "shooter"]),
]

# --- Floors, supplies and experience ------------------------------------------------------------
FLOOR = dict(
    budget=(8.0, 11.0),        # standard-monster equivalents per floor
    boss_budget_share=0.6,     # a boss floor spends this share of the budget before the boss
    clear_share=0.9,           # share of each floor's monsters an average hero kills
)
SUPPLY = dict(
    healing_potions=1.0,       # expected healing potions found per floor (Poisson)
    potion_heal=0.5,           # share of max health one healing potion restores
    food=1.0,                  # expected food per floor
    food_heal=0.10,
    fountain_depths=[5, 12, 19],  # one full heal on these floors (2-3 per run, seeded in game)
    starting_potions=0,        # healing potions every class starts with (equal for parity)
)
XP = dict(
    standard_xp=10.0,          # XP of a threat-level-1 standard monster
    growth=1.15,               # XP growth per threat level; below the cost growth, so XP rises slower than danger
    boss_multiplier=2.0,       # gatekeepers pay this much more than their threat suggests
    level_by_depth={1: 2, 24: 25},  # target level after clearing these depths; fits the XP ladder
)

# --- Player skill: what the model cannot know until playtests measure it -------------------------
# avoid = share of a monster's landed-contact attempts the hero avoids (dodge, guard, spacing);
# efficiency = share of the hero's attack cycles that land; potion_at = health share that triggers a
# potion; potion_known = share of found healing potions the hero can identify and use.
SKILL = {
    "novice":  dict(avoid=dict(melee=0.25, ranged=0.20, homing=0.10, area=0.20),
                    efficiency=0.60, potion_at=0.20, potion_known=0.5),
    "regular": dict(avoid=dict(melee=0.50, ranged=0.45, homing=0.30, area=0.45),
                    efficiency=0.75, potion_at=0.30, potion_known=0.8),
    "skilled": dict(avoid=dict(melee=0.68, ranged=0.62, homing=0.45, area=0.62),
                    efficiency=0.85, potion_at=0.35, potion_known=0.95),
    "expert":  dict(avoid=dict(melee=0.82, ranged=0.76, homing=0.60, area=0.76),
                    efficiency=0.92, potion_at=0.40, potion_known=1.0),
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
