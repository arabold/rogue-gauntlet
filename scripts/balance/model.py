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
    spell_hit: float = 0.0     # mean hit a spell scales off (the focus's hit)
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
        h.spell_hit = h.hit
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
    per_swing = net_hit(*spread_range(ref_hit, P.GEAR.get("weapon_spread", 0.4)), armor)
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
    spread = P.GEAR.get("weapon_spread", 0.4)
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
    if h.mana_regen > 0:
        spell = P.SPELLS[P.CLASSES[h.name]["spell"]]
        cast_hit = net_hit(*spread_range(h.spell_hit * spell["scale"], spread), m.armor * P.MAGIC_ARMOR_SHARE)
        dps += h.mana_regen / spell["mana"] * cast_hit * eff
    return dps


def monster_dps(m: Monster, h: Hero, skill: dict) -> float:
    spread = P.STANDARD["spread"]
    per_dart = m.hit / m.darts
    landed = m.darts * net_hit(*spread_range(per_dart, spread), h.armor)
    chance, extra = m.status
    landed += chance * extra * per_dart * m.darts
    kind_avoid = avoid(h, m.kind, skill) * (1 - m.area) + avoid(h, "area", skill) * m.area
    return landed / m.cycle * (1 - kind_avoid)


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
        opener = P.WEAPONS[P.CLASSES[h.name]["opener"]]
        free = gap / m.speed
        shots = free / opener["cycle"] * skill["efficiency"]
        dealt = shots * net_hit(*spread_range(h.opener_hit, P.GEAR.get("weapon_spread", 0.4)), m.armor)
        ttk = max(0.0, (m.hp - dealt) / kill)
    return monster_dps(m, h, skill) * ttk / h.hp


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
    return (duel_cost(m, hero("reference", 1), skill) / duel_cost(m, hero("reference", 24), skill)) ** (1 / 23)


def xp_reward(m: Monster) -> float:
    growth = P.XP["growth"] or cost_growth()
    xp = P.XP["standard_xp"] * growth ** (threat_level(m) - 1)
    return xp * (P.XP["boss_multiplier"] if m.boss else 1.0)


# --- Floors and the XP ladder ---------------------------------------------------------------------

def eligible_packs(depth: int):
    return [p for p in P.PACKS if p[1] <= depth]


def pack_members(pack, depth: float):
    return [monster(a, depth) for a in pack[3]]


def expected_floor_xp(depth: int) -> float:
    packs = eligible_packs(depth)
    weight = sum(p[2] for p in packs)
    xp_per_unit = sum(p[2] * sum(xp_reward(m) for m in pack_members(p, depth)) for p in packs)
    units = sum(p[2] * sum(cost_units(m, depth) for m in pack_members(p, depth)) for p in packs)
    budget = lerp(P.FLOOR["budget"], depth)
    boss = depth % P.REALM_SIZE == 0
    if boss:
        budget *= P.FLOOR["boss_budget_share"]
    xp = budget * xp_per_unit / units * P.FLOOR["clear_share"]
    if boss:
        xp += xp_reward(monster("boss", depth))
    return xp


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
    """Level on arrival at each depth when every floor above was cleared as expected."""
    out, total_need = {}, [0]
    for n in need:
        total_need.append(total_need[-1] + n)
    for d in range(1, P.DEPTHS + 2):
        xp = cumulative.get(d - 1, 0.0)
        out[d] = 1 + sum(1 for t in total_need[1:] if t <= xp * 1.001)
    return out
