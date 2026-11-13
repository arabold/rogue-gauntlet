"""
Formulas of the balance model (docs/specs/balance-model.md). Pure functions over `params`; no game data.

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


def net_hit(lo: float, hi: float, armor: float) -> float:
    """Mean damage of a hit rolled in [lo, hi] after armor absorbs a uniform roll in [0, armor]."""
    lo, hi = max(0.0, lo), max(0.0, hi)
    if armor <= 0:
        return (lo + hi) / 2
    if hi - lo < 1e-9:
        h = lo
        return h - armor / 2 if h >= armor else h * h / (2 * armor)

    def above(a, b):  # integral of (h - armor / 2)
        return (b * b - a * a) / 2 - armor * (b - a) / 2

    def below(a, b):  # integral of h^2 / (2 armor)
        return (b ** 3 - a ** 3) / (6 * armor)

    total = 0.0
    if lo < armor:
        total += below(lo, min(hi, armor))
    if hi > armor:
        total += above(max(lo, armor), hi)
    return total / (hi - lo)


def spread_range(mean: float, spread: float):
    return mean * (1 - spread), mean * (1 + spread)


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
    armor_share = (P.GEAR["slot_armor"]["body"] + P.GEAR["slot_armor"]["helmet"]) * (1 + found_bonus(depth))
    return hp / (lerp(P.STANDARD["hits_to_die"], depth) * (1 - armor_share / 2))


def slot_armor(slot: str, weight: str, depth: float, found: bool = True) -> float:
    """Armor of one piece on curve at `depth`; `found` adds the expected quality and affix bonus."""
    mult = P.GEAR["weight"][weight][0] if slot != "shield" else 1.0
    value = P.GEAR["slot_armor"][slot] * mult * standard_hit(depth)
    return value * (1 + found_bonus(depth)) if found else value


def weapon_mean(kind: str, depth: float, cls: dict, found: bool = True) -> float:
    w = P.WEAPONS[kind]
    a = attributes(cls, level_at(depth))
    base = reference_weapon_hit(depth) * w["hit"] * (1 + found_bonus(depth) if found else 1)
    if "darts" in w:  # magic foci scale with Intelligence
        return base * (1 + _profile(cls, "spell_per_int") * (a["INT"] - 10))
    return base * (1 + _profile(cls, "damage_per_str") * (a["STR"] - 10))


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


def jewelry_values(stat: str, slot: str, tier: int) -> list[float]:
    """
    The modifier values of a jewelry piece of `tier` raising `stat` (`armor`, `health`, `damage` as low and high,
    `frailty` or `attribute`): its share of that stat on curve at the tier's depth, more for an amulet than a ring.
    """
    j = P.JEWELRY
    scale = j["slots"][slot]
    if stat == "attribute":
        first, per_tier = j["attribute"]
        return [round((first + per_tier * (tier - 1)) * scale)]
    d = tier_depth(tier)
    if stat == "armor":
        return [max(1, round(j["armor"] * scale * standard_hit(d)))]
    if stat in ("health", "frailty"):
        value = round(j[stat] * scale * reference_hp(d))
        return [value or (1 if stat == "health" else -1)]
    if stat == "damage":
        low, high = spread_range(reference_weapon_hit(d), P.GEAR["weapon_spread"])
        return [max(1, round(j["damage"] * scale * low)), max(1, round(j["damage"] * scale * high))]
    raise ValueError(f"unknown jewelry stat {stat}")


def hero(name: str, depth: float) -> Hero:
    cls = P.CLASSES[name]
    level = level_at(depth)
    a = attributes(cls, level)
    armor = slot_armor("body", cls["armor"], depth) + slot_armor("helmet", cls["armor"], depth)
    if cls.get("shield"):
        armor += slot_armor("shield", "medium", depth)
    weight_avoid = P.GEAR["weight"][cls["armor"]][1]
    guard = cls.get("guard", 0.0)
    bonus = {k: weight_avoid + (guard if k in ("melee", "ranged") else 0.0) for k in ("melee", "ranged", "homing", "area")}
    weapon = cls["weapon"]
    h = Hero(name=name, depth=depth, level=level, hp=hero_hp(cls, level), armor=armor, weapon=weapon,
             hit=weapon_mean(weapon, depth, cls), crit=_profile(cls, "crit_per_dex") * a["DEX"],
             cycle=P.WEAPONS[weapon]["cycle"], avoid_bonus=bonus)
    if "spell" in cls:
        h.mana = P.ATTRIBUTES["mana_base"] + _profile(cls, "mana_per_int") * a["INT"]
        h.mana_regen = h.mana * P.ATTRIBUTES["mana_regen"]
        h.spell = P.SPELLS[cls["spell"]]
    if "opener" in cls:
        h.opener_hit = weapon_mean(cls["opener"], depth, cls)
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


def monster(archetype: str, depth: float) -> Monster:
    """Target stats of an archetype authored for `depth`."""
    a = P.ARCHETYPES[archetype]
    ref_hit = weapon_mean("sword_1h", depth, P.CLASSES["reference"])
    armor = a["armor"] * ref_hit
    per_swing = net_hit(*spread_range(ref_hit, P.GEAR["weapon_spread"]), armor)
    return Monster(archetype=archetype, depth=depth,
                   hp=a["hp"] * lerp(P.STANDARD["hits_to_kill"], depth) * per_swing,
                   armor=armor, hit=a["damage"] * standard_hit(depth), cycle=a["cycle"], kind=a["kind"],
                   speed=a["speed"], guard=a.get("guard", 0.0), darts=a.get("darts", 1),
                   status=a.get("status", (0.0, 0.0)), support=a.get("support"),
                   phases=a.get("phases", []), area=a.get("area", 0.0), boss=archetype == "boss")


# --- Expected duel cost -----------------------------------------------------------------------------

def avoid(hero_: Hero, kind: str, skill: dict) -> float:
    """Share of attempts avoided. Class and armor bonuses remove that share of the hits skill lets through,
    so a bonus is worth the same proportion at every skill level."""
    landed = (1 - skill["avoid"][kind]) * (1 - hero_.avoid_bonus.get(kind, 0.0))
    return min(0.95, max(0.0, 1 - landed))


def homing_efficiency(skill: dict) -> float:
    """Homing projectiles recover part of the attacks a hero's aim would miss."""
    eff = skill["efficiency"]
    return eff + (1 - eff) * P.HOMING_ASSIST


def hero_dps(h: Hero, m: Monster, skill: dict) -> float:
    """Sustained damage per second against `m`, including crits, efficiency and the monster's guard."""
    w = P.WEAPONS[h.weapon]
    spread = P.GEAR["weapon_spread"]
    eff = skill["efficiency"]
    if w.get("homing"):
        eff = homing_efficiency(skill)
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


def monster_dps(m: Monster, h: Hero, skill: dict) -> float:
    """
    Damage per second the hero takes. Elemental damage (a caster's missiles, a gatekeeper's area attacks) meets only
    `MAGIC_ARMOR_SHARE` of the hero's armor, like the hero's own magic.
    """
    spread = P.STANDARD["spread"]
    per_dart = m.hit / m.darts
    magic_armor = h.armor * P.MAGIC_ARMOR_SHARE
    landed = m.darts * net_hit(*spread_range(per_dart, spread), magic_armor if m.kind == "homing" else h.armor)
    chance, extra = m.status
    landed += chance * extra * per_dart * m.darts
    direct = landed * (1 - m.area) * (1 - avoid(h, m.kind, skill))
    area = net_hit(*spread_range(m.hit, spread), magic_armor) * m.area * (1 - avoid(h, "area", skill))
    return (direct + area) / m.cycle


def duel_cost(m: Monster, h: Hero, skill: dict) -> float:
    """Share of the hero's max health lost killing `m` alone."""
    kill = hero_dps(h, m, skill)
    ttk = m.hp / kill
    gap = P.ENGAGEMENT["gap"]
    if h.ranged and m.kind == "melee":
        ttk = max(0.0, ttk - gap / m.speed)          # free shots while it closes in
    elif not h.ranged and m.kind != "melee":
        ttk += gap / P.ENGAGEMENT["close_speed"]                              # walking up to a shooter
    elif h.opener_hit and m.kind == "melee":
        free = gap / m.speed
        shots = free / h.opener_cycle * skill["efficiency"]
        dealt = shots * net_hit(*spread_range(h.opener_hit, P.GEAR["weapon_spread"]), m.armor)
        ttk = max(0.0, (m.hp - dealt) / kill)
    return monster_dps(m, h, skill) * ttk / h.hp


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
    for _ in range(50):
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


@functools.cache
def archetype_units(archetype: str, depth: float) -> float:
    """Budget cost of one ordinary monster of `archetype` on a floor at `depth`, in standard monsters."""
    return cost_units(monster(archetype, authored_depth(depth)), depth)


@functools.cache
def archetype_xp(archetype: str, depth: float) -> float:
    """XP of one ordinary monster of `archetype` on a floor at `depth`."""
    return xp_reward(monster(archetype, authored_depth(depth)))


def pack_units(pack, depth: float) -> float:
    """Budget cost of a pack at `depth`, in standard monsters."""
    return group_factor(len(pack["members"])) * sum(archetype_units(a, depth) for a in pack["members"])


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
