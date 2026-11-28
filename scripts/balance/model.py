"""
Formulas of the balance model (docs/balance-model.md). Pure functions over `params`; no game data.

One currency runs through everything: the hero's health. A monster's cost is the share of the hero's
maximum health it is expected to take before it dies; its threat level is the depth at which that cost
equals a standard monster's; its XP follows from its threat level.
"""
from __future__ import annotations

import functools
import math
from dataclasses import dataclass, field

import params as P


# --- Combat primitives (mirror the implemented rules) ---------------------------------------------

def lerp(pair, depth: float) -> float:
    """A `(depth 1, depth 24)` parameter at `depth`, held flat outside the run."""
    a, b = pair
    t = min(1.0, max(0.0, (depth - 1) / (P.DEPTHS - 1)))
    return a + (b - a) * t


def mitigate(hit: float, armor: float) -> float:
    """
    Mean damage of a physical hit through `armor`, as the game's `ArmorRules.Mitigate`: armor blocks a uniform roll in
    [0, armor], but the hit deals at least the minimum (a smaller hit deals itself).
    """
    floor = P.ARMOR["minimum"]
    if hit <= floor or armor <= 0:
        return max(0.0, hit)
    if hit >= floor + armor:
        return hit - armor / 2
    return floor + (hit - floor) ** 2 / (2 * armor)


def roll_armor(hit: float, armor: float, rng) -> float:
    """One physical hit through `armor`, rolled as the game does (`mitigate` is its mean)."""
    if hit <= 0:
        return 0.0
    return max(min(hit, P.ARMOR["minimum"]), hit - rng.uniform(0, armor)) if armor > 0 else hit


def net_hit(lo: float, hi: float, armor: float) -> float:
    """Mean damage of a hit rolled uniformly in [lo, hi] through `armor` (`mitigate`), in closed form."""
    lo, hi = max(0.0, lo), max(0.0, hi)
    if armor <= 0:
        return (lo + hi) / 2
    if hi - lo < 1e-9:
        return mitigate(lo, armor)
    floor = P.ARMOR["minimum"]
    knee = floor + armor  # above it the whole roll comes off; between floor and knee the minimum may hold
    pieces = (
        (0.0, floor, lambda h: h * h / 2),
        (floor, knee, lambda h: floor * h + (h - floor) ** 3 / (6 * armor)),
        (knee, float("inf"), lambda h: h * h / 2 - armor * h / 2),
    )
    total = sum(f(min(hi, b)) - f(max(lo, a)) for a, b, f in pieces if lo < b and hi > a)
    return total / (hi - lo)


def spread_range(mean: float, spread: float):
    return mean * (1 - spread), mean * (1 + spread)


def weapon_spread(kind: str) -> float:
    """How far a weapon kind's damage range reaches either side of its mean, as a share of it."""
    return P.WEAPONS[kind]["spread"]


# --- Hero ---------------------------------------------------------------------------------------

@dataclass
class Hero:
    name: str
    depth: float
    level: float
    hp: float
    armor: float
    weapon: str
    hit: float                 # mean hit of the main weapon before armor and crit
    crit: float
    cycle: float
    mana: float = 0.0
    mana_regen: float = 0.0
    spell: dict | None = None  # the class spell, which scales off the focus's hit
    avoid_bonus: dict = field(default_factory=dict)
    speed: float = P.ENGAGEMENT["hero_speed"]   # movement speed in the class's armor
    opener: str | None = None  # the weapon kind of a ranged opener before closing in
    opener_hit: float = 0.0
    opener_cycle: float = 0.0

    @property
    def ranged(self) -> bool:
        return P.WEAPONS[self.weapon].get("ranged", False)


def level_at(depth: float) -> float:
    """Target level while playing `depth`: arrival level, one per cleared floor."""
    return min(P.LEVEL_CAP, max(1.0, depth))


def found_bonus(depth: float) -> float:
    return lerp(P.GEAR["found_bonus"], depth)


def reference_weapon_hit(depth: float) -> float:
    """Mean hit of a Normal reference sword on curve at `depth`."""
    base, growth = P.GEAR["weapon_hit"]
    return base * growth ** (depth - 1)


def _profile(cls: dict, key: str) -> float:
    return cls.get("profile", {}).get(key, P.ATTRIBUTES[key])


def attributes(cls: dict, level: float) -> dict:
    return {k: v + cls["growth"][k] * (level - 1) for k, v in cls["attrs"].items()}


def hero_hp(cls: dict, level: float) -> float:
    a = attributes(cls, level)
    base = P.ATTRIBUTES["hp_base"] + P.ATTRIBUTES["hp_per_level"] * (level - 1)
    return base * (1 + _profile(cls, "hp_per_vit") * (a["VIT"] - 10))


def standard_hit(depth: float) -> float:
    """Mean hit of the standard monster: the reference hero dies in `hits_to_die` of them after armor."""
    ref = P.CLASSES["reference"]
    hp = hero_hp(ref, level_at(depth))
    armor = slot_armor("body", ref["armor"], depth) + slot_armor("helmet", ref["armor"], depth)
    return hp / lerp(P.STANDARD["hits_to_die"], depth) + armor / 2  # the hero's armor blocks half its value per hit


def slot_armor(slot: str, weight: str, depth: float, found: bool = True) -> float:
    """Armor of one piece on curve at `depth`; `found` adds the expected quality and affix bonus."""
    mult = P.GEAR["weight"][weight]["armor"] if slot != "shield" else 1.0
    value = P.GEAR["slot_armor"][slot] * mult * reference_weapon_hit(depth)
    return value * (1 + found_bonus(depth)) if found else value


def weapon_hit(kind: str, power: float, cls: dict, depth: float) -> float:
    """Mean hit of a `kind` weapon of sword-equivalent `power` in the hands of `cls` at its level for `depth`."""
    w = P.WEAPONS[kind]
    a = attributes(cls, level_at(depth))
    base = power * w["hit"]
    if "darts" in w:  # magic foci scale with Intelligence
        return base * (1 + _profile(cls, "spell_per_int") * (a["INT"] - 10))
    return base * (1 + _profile(cls, "damage_per_str") * (a["STR"] - 10))


def _found(kinds: frozenset, depth: int) -> list[tuple[float, float]]:
    """
    (sword-equivalent power, expected count) of the `kinds` weapons found before fighting on `depth`: every earlier
    floor's drops (WEAPON_SUPPLY) and half of this floor's. Power is a weapon's mean hit over its kind's `hit`.
    """
    found = {}
    for floor in range(1, depth + 1):
        share = 1.0 if floor < depth else 0.5
        for kind, mean, count in P.WEAPON_SUPPLY.get(floor, []):
            if kind in kinds:
                power = mean / P.WEAPONS[kind]["hit"]
                found[power] = found.get(power, 0.0) + count * share
    return list(found.items())


def _expected_best(floor_value: float, found: list[tuple[float, float]]) -> float:
    """
    Expected best of `floor_value` and the found items: each power turns up a Poisson number of times with its
    expected count, so the best exceeds `x` with chance `1 - exp(-count above x)`.
    """
    best, count = floor_value, 0.0
    above = sorted(((p, c) for p, c in found if p > floor_value), reverse=True)
    for i, (power, c) in enumerate(above):
        count += c
        below = above[i + 1][0] if i + 1 < len(above) else floor_value
        best += (1 - math.exp(-count)) * (power - below)
    return best


@functools.cache
def expected_weapon(name: str, depth: int, opener: bool = False) -> tuple[str, float]:
    """
    The weapon kind the expected hero of class `name` wields on `depth` (its opener with `opener`) and that weapon's
    expected sword-equivalent power: the better of the worn starter and the best weapon of the class's `uses` found
    so far. A class that starts with a kind of other hands than its main one (the barbarian's one-handed axe) wields
    whichever hands' weapons it can expect to be stronger.
    """
    cls = P.CLASSES[name]
    main = cls["opener"] if opener else cls["weapon"]
    start = main if opener else cls.get("starter", main)
    usable = [main] if opener else cls["uses"]
    worn = P.GEAR["starter"] * reference_weapon_hit(tier_depth(1))
    hands = frozenset(k for k in usable if P.WEAPONS[k]["hands"] == P.WEAPONS[main]["hands"])
    if P.WEAPONS[start]["hands"] == P.WEAPONS[main]["hands"]:
        return main, _expected_best(worn, _found(frozenset(usable), depth))
    main_power = _expected_best(worn, _found(hands, depth))
    other_power = _expected_best(worn, _found(frozenset(usable) - hands, depth))
    return (main, main_power) if main_power >= other_power else (start, other_power)


def weapon_poise(kind: str, scale: float = 1.0) -> float:
    """Poise one hit of a weapon `kind` removes (one dart of a volley), times `scale`, to half a point."""
    w = P.WEAPONS[kind]
    share = w["hit"] * w.get("dart_scale", 1.0) * (P.POISE["blunt"] if kind.startswith("blunt") else 1.0)
    return round(P.POISE["hit"] * share * scale * 2) / 2


def monster_poise(archetype: str) -> float:
    """A monster's poise meter: its archetype's reference hits; zero means no poise."""
    return P.POISE["hits"][archetype] * P.POISE["hit"]


def tier_depth(tier: int) -> int:
    """The depth a tier is on curve: its introduction plus the lag before the expected hero wears it."""
    return P.GEAR["tier_intro"][tier - 1] + P.GEAR["tier_lag"]


def reference_hp(depth: float) -> float:
    """Max health of the reference hero arriving at `depth` at the target level."""
    return hero_hp(P.CLASSES["reference"], level_at(depth))


@functools.cache
def potion_points(size: str) -> int:
    """Health a healing potion of `size` restores: its share of the expected hero's health where the size arrives."""
    s = P.SUPPLY["potion_sizes"][size]
    return round(s["heal"] * reference_hp(s["intro"]) / 5) * 5


@functools.cache
def potion_sizes_at(depth: float) -> dict[str, float]:
    """The odds of each healing potion size among the potions found at `depth`."""
    sizes = {name: s["weight"] for name, s in P.SUPPLY["potion_sizes"].items()
             if s["intro"] <= depth and (not s["last"] or depth <= s["last"])}
    total = sum(sizes.values())
    return {name: w / total for name, w in sizes.items()}


def healing_potions(depth: float) -> float:
    """Expected healing potions found on a floor at `depth`; more deeper down, where each heals a smaller share."""
    return lerp(P.SUPPLY["healing_potions"], depth)


def potion_heal(depth: float) -> float:
    """Expected health one healing potion found at `depth` restores."""
    return sum(odds * potion_points(size) for size, odds in potion_sizes_at(depth).items())


def healing_share(depth: int) -> float:
    """Expected healing from the potions found on a floor at `depth`, as a share of the expected hero's health."""
    return healing_potions(depth) * potion_heal(depth) / reference_hp(depth)


def food_points(portion: str) -> int:
    """Health one food of `portion` restores: its share of a level 1 hero's health."""
    return max(1, round(P.SUPPLY["food_portions"][portion] * reference_hp(1)))


@functools.cache
def food_heal() -> float:
    """Expected health one food restores, with every portion equally likely."""
    portions = P.SUPPLY["food_portions"]
    return sum(food_points(portion) for portion in portions) / len(portions)


def poison_tick(points: float) -> int:
    """Whole points each poison tick takes, so the whole poison takes about `points`."""
    return max(1, round(points / P.STATUS["poison_ticks"]))


def poison_per_tick(size: str) -> int:
    """Points each tick of a poison potion of `size` takes: in all, a share of what the healing potion of its size gives."""
    return poison_tick(P.STATUS["poison"] * potion_points(size))


def food_poison_per_tick(portion: str) -> int:
    """Points each tick of spoiled food of `portion` takes: in all, a multiple of what it heals."""
    return poison_tick(P.STATUS["food_poison"] * food_points(portion))


def _ring_values(stat: str, tier: int) -> list[float]:
    """
    What a ring of `tier` gives of `stat` (`armor`, `health`, `damage` as low and high, `frailty` or `attribute`),
    before rounding: its share of that stat on curve at the tier's depth.
    """
    j = P.JEWELRY
    if stat == "attribute":
        first, per_tier = j["attribute"]
        return [first + per_tier * (tier - 1)]
    d = tier_depth(tier)
    if stat == "armor":
        return [j["armor"] * reference_weapon_hit(d)]
    if stat in ("health", "frailty"):
        return [j[stat] * reference_hp(d)]
    if stat == "damage":
        return [j["damage"] * end for end in spread_range(reference_weapon_hit(d), weapon_spread("sword_1h"))]
    raise ValueError(f"unknown jewelry stat {stat}")


def jewelry_values(stat: str, slot: str, tier: int) -> list[float]:
    """
    The modifier values of a jewelry piece of `tier` raising `stat`: a ring's, more for an amulet. Each tier gives at
    least a point more than the tier below, where small values would otherwise round to the same.
    """
    values = [value * P.JEWELRY["slots"][slot] for value in _ring_values(stat, tier)]
    if stat == "attribute":
        values = [round(value) for value in values]
    elif stat in ("health", "frailty"):
        values = [round(value) or (1 if stat == "health" else -1) for value in values]
    else:
        values = [max(1, round(value)) for value in values]
    if tier > 1:
        below = jewelry_values(stat, slot, tier - 1)
        values = [max(v, b) if b >= 0 else min(v, b) for v, b in zip(values, below)]
        if sum(map(abs, values)) <= sum(map(abs, below)):
            values[-1] += 1 if values[-1] >= 0 else -1
    return values


def hero(name: str, depth: float) -> Hero:
    cls = P.CLASSES[name]
    level = level_at(depth)
    a = attributes(cls, level)
    armor = slot_armor("body", cls["armor"], depth) + slot_armor("helmet", cls["armor"], depth)
    if cls.get("shield"):
        armor += slot_armor("shield", "medium", depth)
    weight_avoid = P.GEAR["weight"][cls["armor"]]["avoid"]
    guard = cls.get("guard", 0.0)
    bonus = {k: weight_avoid + (guard if k in ("melee", "ranged") else 0.0) for k in ("melee", "ranged", "homing", "area")}
    weapon, power = expected_weapon(name, round(depth))
    h = Hero(name=name, depth=depth, level=level, hp=hero_hp(cls, level), armor=armor, weapon=weapon,
             hit=weapon_hit(weapon, power, cls, depth), crit=_profile(cls, "crit_per_dex") * a["DEX"],
             cycle=P.WEAPONS[weapon]["cycle"], avoid_bonus=bonus,
             speed=P.ENGAGEMENT["hero_speed"] * P.GEAR["weight"][cls["armor"]]["speed"])
    if "spell" in cls:
        h.mana = P.ATTRIBUTES["mana_base"] + _profile(cls, "mana_per_int") * a["INT"]
        h.mana_regen = h.mana * P.ATTRIBUTES["mana_regen"]
        h.spell = P.SPELLS[cls["spell"]]
    if "opener" in cls:
        h.opener = cls["opener"]
        h.opener_hit = weapon_hit(cls["opener"], expected_weapon(name, round(depth), opener=True)[1], cls, depth)
        h.opener_cycle = P.WEAPONS[cls["opener"]]["cycle"]
    return h


# --- Monsters -----------------------------------------------------------------------------------

@dataclass
class Monster:
    archetype: str
    depth: float
    hp: float
    armor: float
    hit: float                 # mean hit per attack (all darts together)
    cycle: float
    kind: str
    speed: float
    guard: float = 0.0
    darts: int = 1
    status: tuple = (0.0, 0.0)
    support: dict | None = None
    phases: list = field(default_factory=list)
    area: float = 0.0
    boss: bool = False
    windup: float = 0.5        # seconds from an attack's start to its contact


@functools.cache
def heaviest_hit(depth: float) -> float:
    """The largest average blow among the playable classes' weapons and openers at `depth` (a volley counts whole)."""
    def blow(h: Hero) -> float:
        w = P.WEAPONS[h.weapon]
        return max(h.hit * w.get("darts", 1) * w.get("dart_scale", 1.0), h.opener_hit)
    return max(blow(hero(name, depth)) for name in P.CLASSES if name != "reference")


def monster(archetype: str, depth: float) -> Monster:
    """
    Target stats of an archetype authored for `depth`. Health is sized in the expected reference hero's hits (its
    found weapon, `expected_weapon`), at least `min_hits` of the heaviest class blow for every archetype but the
    gatekeeper.
    """
    a = P.ARCHETYPES[archetype]
    ref_hit = hero("reference", depth).hit
    armor = a["armor"] * ref_hit
    per_swing = net_hit(*spread_range(ref_hit, weapon_spread("sword_1h")), armor)
    hp = a["hp"] * lerp(P.STANDARD["hits_to_kill"], depth) * per_swing
    if archetype != "boss":
        hp = max(hp, P.STANDARD["min_hits"] * heaviest_hit(depth))
    return Monster(archetype=archetype, depth=depth,
                   hp=hp,
                   armor=armor, hit=a["damage"] * standard_hit(depth), cycle=a["cycle"], kind=a["kind"],
                   speed=a["speed"], guard=a.get("guard", 0.0), darts=a.get("darts", 1),
                   status=a.get("status", (0.0, 0.0)), support=a.get("support"),
                   phases=a.get("phases", []), area=a.get("area", 0.0), boss=archetype == "boss", windup=a["windup"])


# --- Expected duel cost -----------------------------------------------------------------------------

def avoid(hero_: Hero, kind: str, skill: dict, speed: float = 0.0) -> float:
    """
    Share of attempts avoided. Skill avoids a melee monster of `speed` only by its `footwork` share; class and armor
    bonuses remove that share of the hits skill lets through, so a bonus is worth the same proportion at every skill
    level; a ranged hero kites a melee monster on top, the more the slower it is than the hero.
    """
    share = skill["avoid"][kind] * (footwork(hero_, speed) if kind == "melee" else 1.0)
    landed = (1 - share) * (1 - hero_.avoid_bonus.get(kind, 0.0))
    if kind == "melee" and hero_.ranged:
        landed *= 1 - skill["kite"] * max(0.0, 1 - speed / hero_.speed)
    return min(0.95, max(0.0, 1 - landed))


def footwork(hero_: Hero, speed: float) -> float:
    """
    How much of the skill's melee avoidance works against a monster of `speed`: all of it against monsters at most
    ENGAGEMENT `footwork` of the hero's speed, fading to none against one as fast as the hero, which the hero cannot
    step away from.
    """
    full = P.ENGAGEMENT["footwork"]
    return min(1.0, max(0.0, (1 - speed / hero_.speed) / (1 - full)))


def landing_efficiency(h: Hero, skill: dict) -> float:
    """Share of the hero's attacks that land: skill's efficiency, homing projectiles recovering part of the misses."""
    eff = skill["efficiency"]
    return eff + (1 - eff) * P.HOMING_ASSIST if P.WEAPONS[h.weapon].get("homing") else eff


def hero_dps(h: Hero, m: Monster, skill: dict) -> float:
    """Sustained damage per second against `m`, including crits, efficiency and the monster's guard."""
    w = P.WEAPONS[h.weapon]
    spread = weapon_spread(h.weapon)
    eff = landing_efficiency(h, skill)
    if "darts" in w:
        dart = h.hit * w["dart_scale"]
        per_attack = w["darts"] * net_hit(*spread_range(dart, spread), m.armor * P.MAGIC_ARMOR_SHARE)
    else:
        per_attack = net_hit(*spread_range(h.hit, spread), m.armor)
    per_attack *= 1 + h.crit * (P.CRIT_MULTIPLIER - 1)
    dps = per_attack / h.cycle * eff * (1 - m.guard)
    if h.spell:
        cast_hit = net_hit(*spread_range(h.hit * h.spell["scale"], spread), m.armor * P.MAGIC_ARMOR_SHARE)
        dps += h.mana_regen / h.spell["mana"] * cast_hit * eff
    return dps


def uptime(m: Monster) -> float:
    """Share of its attack cycles a monster actually starts while fighting (ENGAGEMENT `uptime`)."""
    return P.ENGAGEMENT["uptime"] if m.kind == "melee" and not m.boss else 1.0


def monster_dps(m: Monster, h: Hero, skill: dict) -> float:
    """
    Damage per second the hero takes, counting only the attacks that start (`uptime`). Elemental damage (a caster's missiles, a gatekeeper's area attacks) meets only
    `MAGIC_ARMOR_SHARE` of the hero's armor, like the hero's own magic.
    """
    spread = P.STANDARD["spread"]
    per_dart = m.hit / m.darts
    magic_armor = h.armor * P.MAGIC_ARMOR_SHARE
    landed = m.darts * net_hit(*spread_range(per_dart, spread), magic_armor if m.kind == "homing" else h.armor)
    chance, extra = m.status
    landed += chance * extra * per_dart * m.darts
    direct = landed * (1 - m.area) * (1 - avoid(h, m.kind, skill, m.speed))
    area = net_hit(*spread_range(m.hit, spread), magic_armor) * m.area * (1 - avoid(h, "area", skill))
    return (direct + area) * uptime(m) / m.cycle


def _blow_odds(h: Hero, m: Monster, skill: dict):
    """
    How the hero's landed blows against `m` add up: the chance a swing lands, and the chance the monster is still
    alive after `k` landed blows. One blow follows its roll (weapon range, crit); several add up as a normal sum.
    """
    lands = landing_efficiency(h, skill) * (1 - m.guard)
    mean = hero_dps(h, m, skill) * h.cycle / lands          # a landed blow, with crits and the class spell
    crit, mult, spread = h.crit, P.CRIT_MULTIPLIER, weapon_spread(h.weapon)
    base = mean / (1 + crit * (mult - 1))
    sd = base * math.sqrt((1 + spread ** 2 / 3) * (1 - crit + crit * mult ** 2) - (1 + crit * (mult - 1)) ** 2)

    def roll_below(x: float, scale: float) -> float:
        return min(1.0, max(0.0, (x / scale - (1 - spread)) / (2 * spread)))

    def alive_after(blows: int, hp: float) -> float:
        if hp <= 0:
            return 0.0
        if blows == 0:
            return 1.0
        if blows == 1:
            return (1 - crit) * roll_below(hp, base) + crit * roll_below(hp, base * mult)
        z = (hp - blows * mean) / (sd * math.sqrt(blows))
        return 0.5 * (1 + math.erf(z / math.sqrt(2)))

    return lands, alive_after


def duel_cost(m: Monster, h: Hero, skill: dict) -> float:
    """
    Share of the hero's max health lost killing `m` alone. Blows land one at a time: the hero's first `strike` seconds
    after the two meet, then one per swing that lands; the monster's first its `windup` after, then one per attack
    cycle. The hero takes only the attacks that land before the monster dies, so a monster felled by the first blow
    costs nothing. A ranged hero gets the shots while a melee monster closes in; a melee hero walks up to a shooter.
    """
    e = P.ENGAGEMENT
    lands, alive_after = _blow_odds(h, m, skill)
    hp, first = m.hp, e["strike"]
    if h.ranged and m.kind == "melee":
        first -= e["gap"] / m.speed
    elif not h.ranged and m.kind != "melee":
        first += e["gap"] / e["close_speed"]
    elif h.opener_hit and m.kind == "melee":
        shots = e["gap"] / m.speed / h.opener_cycle * skill["efficiency"]
        hp -= shots * net_hit(*spread_range(h.opener_hit, weapon_spread(h.opener)), m.armor)

    def alive_at(t: float) -> float:
        if t < first:
            return 1.0 if hp > 0 else 0.0
        blows = lands * (1 + (t - first) / h.cycle)
        k = int(blows)
        lo = alive_after(k, hp)
        return lo + (blows - k) * (alive_after(k + 1, hp) - lo)

    attacks = 0.0
    for k in range(1000):
        alive = alive_at(m.windup + k * m.cycle)
        attacks += alive
        if alive < 1e-5:
            break
    return attacks * monster_dps(m, h, skill) * m.cycle / h.hp


@functools.cache
def standard_cost(depth: float, hero_name: str = "reference", skill_name: str = P.REFERENCE_SKILL) -> float:
    return duel_cost(monster("standard", depth), hero(hero_name, depth), P.SKILL[skill_name])


def support_cost(m: Monster, h: Hero, skill: dict) -> float:
    """
    Health share a healer adds by keeping one standard ally alive longer: what it heals while it lives,
    burned through again at the hero's damage, while that ally keeps attacking.
    """
    if not m.support:
        return 0.0
    ally = monster("standard", h.depth)
    alive = m.hp / hero_dps(h, m, skill)
    healed = m.support["heal"] * ally.hp * max(1.0, alive / m.support["cycle"])
    return monster_dps(ally, h, skill) * healed / hero_dps(h, ally, skill) / h.hp


def cost_units(m: Monster, depth: float) -> float:
    """Budget cost of `m` at `depth`, in standard monsters (reference hero and skill)."""
    skill = P.SKILL[P.REFERENCE_SKILL]
    h = hero("reference", depth)
    return (duel_cost(m, h, skill) + support_cost(m, h, skill)) / standard_cost(depth)


def threat_level(m: Monster) -> float:
    """
    The depth at which `m` costs the reference hero as much as a standard monster does. Outside the run's
    depths the hero stops growing, so levels beyond them extrapolate with the run's average cost growth.
    """
    first, last = cost_units(m, 1), cost_units(m, P.DEPTHS)
    growth = math.log(cost_growth())
    if first <= 1:
        return 1 + math.log(first) / growth
    if last >= 1:
        return P.DEPTHS + math.log(last) / growth
    lo, hi = 1.0, float(P.DEPTHS)
    while hi - lo > 1e-4:  # threat levels are authored to 0.01
        mid = (lo + hi) / 2
        if cost_units(m, mid) > 1:
            lo = mid
        else:
            hi = mid
    return (lo + hi) / 2


@functools.cache
def cost_growth() -> float:
    """Per-depth factor by which a fixed monster gets cheaper as the expected hero grows."""
    skill = P.SKILL[P.REFERENCE_SKILL]
    m = monster("standard", 1)
    first, last = hero("reference", 1), hero("reference", P.DEPTHS)
    return (duel_cost(m, first, skill) / duel_cost(m, last, skill)) ** (1 / (P.DEPTHS - 1))


def xp_reward(m: Monster) -> float:
    xp = P.XP["standard_xp"] * P.XP["growth"] ** (threat_level(m) - 1)
    return xp * (P.XP["boss_multiplier"] if m.boss else 1.0)


# --- Floors and the XP ladder ---------------------------------------------------------------------

def eligible_packs(depth: int):
    return [p for p in P.PACKS if p["first"] <= depth and (p["last"] == 0 or depth <= p["last"])]


def group_factor(size: int) -> float:
    """
    How much more a pack costs than its members fought alone. The hero kills members one by one while the rest keep
    attacking, so the k-th member to fall attacks through k kills: (N + 1) / 2 for N similar members. Simulated
    packs of swarmers, fodder and mixed melee follow it within 10%; packs with a healer or shooter come out lower.
    """
    return (size + 1) / 2


def authored_depth(depth: int) -> float:
    """
    The depth the ordinary monsters of a floor at `depth` are authored for. Monsters don't level: a realm authors its
    monsters once, for its middle floor, and they keep those stats on every floor of the realm. Its first floor
    therefore holds fewer, relatively stronger monsters, and its last more, weaker ones. A gatekeeper is authored for
    its own floor.
    """
    realm = (depth - 1) // P.REALM_SIZE
    return realm * P.REALM_SIZE + (P.REALM_SIZE + 1) / 2


def curse_penalty(stat: str, tier: int) -> list[tuple[int, int]]:
    """
    The strongest and weakest roll of each modifier of a curse penalty on `stat` (`armor`, `health`, `attribute`, or
    `damage` as its low and high end) at `tier`, as positive points: up to `CURSES["penalty"]` (for damage
    `CURSES["damage_penalty"]`) times what a ring of that tier gives, down to `CURSES["floor"]` of it.
    """
    c = P.CURSES
    penalty = c["damage_penalty"] if stat == "damage" else c["penalty"]
    strongest = [max(1, round(penalty * ring)) for ring in _ring_values(stat, tier)]
    return [(s, max(1, round(c["floor"] * s))) for s in strongest]


@functools.cache
def archetype_units(archetype: str, depth: float) -> float:
    """Budget cost of one ordinary monster of `archetype` on a floor at `depth`, in standard monsters."""
    return cost_units(monster(archetype, authored_depth(depth)), depth)


def archetype_xp(archetype: str, depth: float) -> float:
    """XP of one ordinary monster of `archetype` on a floor at `depth`: the one authored for its realm."""
    return _authored_xp(archetype, authored_depth(depth))


@functools.cache
def _authored_xp(archetype: str, depth: float) -> float:
    return xp_reward(monster(archetype, depth))


def pack_units(pack, depth: float) -> float:
    """Budget cost of a pack at `depth`, in standard monsters."""
    return group_factor(len(pack["members"])) * sum(archetype_units(a, depth) for a in pack["members"])


def health_line(share: float) -> tuple[int, float]:
    """A trap's authored damage on depth 1 and per depth (`Trap.Damage`, `DamagePerDepth`): a line through `share` of the
    reference hero's health on depths 1 and 24."""
    first, last = (share * reference_hp(d) for d in (1, P.DEPTHS))
    return round(first), round((last - first) / (P.DEPTHS - 1), 2)


def line_damage(line: tuple[float, float], depth: float) -> float:
    """A hit on `depth` before armor from an authored damage line, as the game's `TrapDamage.At` computes it."""
    first, per_depth = line
    return max(1, round(first + per_depth * (round(depth) - 1)))


def trap_line() -> tuple[int, float]:
    """A spike plate's damage line (`SpikeBurst`): TRAPS `damage` of the reference hero's health."""
    return health_line(P.TRAPS["damage"])


def trap_damage(depth: float) -> float:
    """A spike plate's spring on `depth` before armor."""
    return line_damage(trap_line(), depth)


def launcher_line(scale: float = 1.0) -> tuple[int, float]:
    """A wall launcher's damage line (`WallLauncher`): LAUNCHERS `damage` of the reference hero's health, times `scale`
    (a fireball's `fire_scale`)."""
    return health_line(P.LAUNCHERS["damage"] * scale)


def launcher_damage(depth: float) -> float:
    """A dart's hit on `depth` before armor."""
    return line_damage(launcher_line(), depth)


def launcher_share(depth: float) -> float:
    """Share of a floor's trap slots that hold plate traps on `depth`."""
    l = P.LAUNCHERS
    return l["chance"] * l["lane_fit"] if depth >= l["first_depth"] else 0.0


def trap_cost(h: Hero) -> float:
    """Expected share of `h`'s max health a floor's traps take: spike plates, and plate traps whose shot lands."""
    t, share = P.TRAPS, launcher_share(h.depth)
    spring = ((1 - share) * mitigate(trap_damage(h.depth), h.armor)
              + share * P.LAUNCHERS["hits"] * mitigate(launcher_damage(h.depth), h.armor))
    return t["per_floor"] * t["triggers"] * spring / h.hp


def hazard_line(kind: str) -> tuple[int, float]:
    """A gauntlet hazard's damage line (`TrapHazard`) for `kind` (dart, fireball or blade): HAZARDS `damage` of the
    reference hero's health; a fireball deals LAUNCHERS `fire_scale` of a dart's."""
    damage = P.HAZARDS["damage"]
    return health_line(damage["dart"] * P.LAUNCHERS["fire_scale"] if kind == "fireball" else damage[kind])


def hazard_hit(kind: str, h: Hero) -> float:
    """A gauntlet hazard's mean hit on `h` after armor; armor never absorbs a fireball."""
    hit = line_damage(hazard_line(kind), h.depth)
    return hit if kind == "fireball" else mitigate(hit, h.armor)


def gauntlet_depths() -> list[int]:
    """Depths that can hold a gauntlet: the ordinary floors from HAZARDS `first_depth`."""
    return [d for d in range(P.HAZARDS["first_depth"], P.DEPTHS + 1) if not is_gatekeeper_floor(d)]


def gauntlet_cost(h: Hero) -> float:
    """
    Expected share of `h`'s max health the gauntlet of a floor at `h`'s depth takes: the chance the floor holds one (a
    run's `per_run` spread over the floors that can), times a crossing's hits, each a hit of a room's hazard.
    """
    g, floors = P.HAZARDS, gauntlet_depths()
    if h.depth not in floors:
        return 0.0
    hit = sum(n * hazard_hit(kind, h) for kind, n in g["rooms"].items()) / sum(g["rooms"].values())
    return g["per_run"] / len(floors) * g["hits"] * hit / h.hp


def is_gatekeeper_floor(depth: int) -> bool:
    """The last floor of each realm holds its gatekeeper."""
    return depth % P.REALM_SIZE == 0


def floor_budget(depth: int) -> float:
    """Standard fights a floor spends on packs; a gatekeeper floor spends a share and keeps the rest for it."""
    budget = lerp(P.FLOOR["budget"], depth)
    return budget * P.FLOOR["boss_budget_share"] if is_gatekeeper_floor(depth) else budget


@functools.cache
def expected_floor_xp(depth: int) -> float:
    packs = eligible_packs(depth)
    xp_per_unit = sum(p["weight"] * sum(archetype_xp(a, depth) for a in p["members"]) for p in packs)
    units = sum(p["weight"] * pack_units(p, depth) for p in packs)
    xp = floor_budget(depth) * xp_per_unit / units * P.FLOOR["clear_share"]
    if is_gatekeeper_floor(depth):
        xp += xp_reward(monster("boss", depth))
    return xp


@functools.cache
def xp_ladder():
    """XP to advance from each level, `a * g^(L-1)`, fitted so cleared floors land the target levels."""
    cumulative, total = {}, 0.0
    for d in range(1, P.DEPTHS + 1):
        total += expected_floor_xp(d)
        cumulative[d] = total
    (d1, l1), (d2, l2) = sorted(P.XP["level_by_depth"].items())

    def cum(level, a, g):  # XP needed to reach `level` from level 1
        return a * (g ** (level - 1) - 1) / (g - 1)

    target_ratio = cumulative[d2] / cumulative[d1]
    lo, hi = 1.0001, 2.0
    for _ in range(80):
        g = (lo + hi) / 2
        if cum(l2, 1, g) / cum(l1, 1, g) < target_ratio:
            lo = g
        else:
            hi = g
    g = (lo + hi) / 2
    a = cumulative[d2] / cum(l2, 1, g)
    need = [round(a * g ** (lvl - 1)) for lvl in range(1, P.LEVEL_CAP)]
    return need, cumulative


def levels_by_depth(need, cumulative):
    """
    Level on arrival at each depth when every floor above was cleared as expected. The ladder rounds each level's XP to
    whole points, so a level counts as reached when the expected XP falls short of it by no more than that rounding.
    """
    out, total_need = {}, [0]
    for n in need:
        total_need.append(total_need[-1] + n)
    for d in range(1, P.DEPTHS + 2):
        xp = cumulative.get(d - 1, 0.0)
        out[d] = 1 + sum(1 for level, t in enumerate(total_need[1:], 1) if t <= xp + 0.5 * level)
    return out
