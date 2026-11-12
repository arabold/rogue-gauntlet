#!/usr/bin/env -S uv run --script
# /// script
# requires-python = ">=3.12"
# ///
"""
Rogue Gauntlet balance simulator: target tables, class parity and survival odds from the balance model.

    uv run scripts/balance/simulate.py                 # progression overview
    uv run scripts/balance/simulate.py monsters|gear|classes|xp
    uv run scripts/balance/simulate.py survival [--runs 300]  # arrival odds per skill and class vs targets
    uv run scripts/balance/simulate.py parity [--floors 200]  # realm, floor and gatekeeper cost per class
    uv run scripts/balance/simulate.py weapon --tier 2 --damage 6-12 --cycle 0.5 [--hands 2]
    uv run scripts/balance/simulate.py armor --tier 3 --slot body --armor 9 [--weight heavy]
    uv run scripts/balance/simulate.py monster --depth 4 --hp 40 --armor 1 --damage 6-10 --cycle 2.2 [--kind ranged]
    uv run scripts/balance/simulate.py audit [--only monsters,weapons,armor,classes] [--match skeleton]
    uv run scripts/balance/simulate.py derive [--only ...] [--match ...]   # writes the targets into the .tres files
    uv run scripts/balance/simulate.py supply [--depths 1-3] [--seeds 12]  # healing, food and gear per floor in the game
    uv run scripts/balance/simulate.py calibrate TRACE.jsonl [...]          # skill measured from combat-arena traces

See docs/specs/balance-model.md for the model; edit params.py to tune it.
"""
from __future__ import annotations

import argparse
import math
import os
import random
import sys
from functools import cache
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import content as C  # noqa: E402
import model as M  # noqa: E402
import params as P  # noqa: E402
import resources as R  # noqa: E402

KEY_DEPTHS = [1, 3, 6, 9, 12, 15, 18, 21, 24]
CLASS_NAMES = [c for c in P.CLASSES if c != "reference"]


def table(headers, rows):
    widths = [max(len(str(h)), *(len(str(r[i])) for r in rows)) for i, h in enumerate(headers)]
    print("  ".join(str(h).rjust(w) for h, w in zip(headers, widths)))
    print("  ".join("-" * w for w in widths))
    for r in rows:
        print("  ".join(str(c).rjust(w) for c, w in zip(r, widths)))
    print()


def rng_range(mean, spread):
    lo, hi = M.spread_range(mean, spread)
    return f"{lo:.0f}-{hi:.0f}"


# --- Analytic reports --------------------------------------------------------------------------------

def report_progression():
    need, cumulative = M.xp_ladder()
    levels = M.levels_by_depth(need, cumulative)
    skill = P.SKILL[P.REFERENCE_SKILL]
    print(f"Progression of the reference hero ({P.REFERENCE_SKILL} skill) against the standard monster\n")
    rows = []
    for d in range(1, P.DEPTHS + 1):
        h, m = M.hero("reference", d), M.monster("standard", d)
        per_hit = M.net_hit(*M.spread_range(m.hit, P.STANDARD["spread"]), h.armor)
        rows.append([d, "boss" if d % P.REALM_SIZE == 0 else "", levels[d], f"{h.hp:.0f}", f"{h.armor:.1f}",
                     rng_range(h.hit, P.GEAR["weapon_spread"]), f"{m.hp:.0f}", f"{m.armor:.1f}",
                     rng_range(m.hit, P.STANDARD["spread"]), f"{h.hp / per_hit:.1f}",
                     f"{M.duel_cost(m, h, skill):.1%}", f"{M.lerp(P.FLOOR['budget'], d):.1f}",
                     f"{M.expected_floor_xp(d):.0f}"])
    table(["depth", "", "level", "hero hp", "armor", "hit", "mon hp", "armor", "mon hit", "hits to die",
           "duel cost", "budget", "floor xp"], rows)
    print(f"A fixed monster gets {1 - 1 / M.cost_growth():.1%} cheaper per depth as the expected hero grows.")


def report_monsters():
    print("Archetype targets per depth, with threat level and XP\n")
    for d in KEY_DEPTHS:
        rows = []
        for name in P.ARCHETYPES:
            m = M.monster(name, d)
            rows.append([name, f"{m.hp:.0f}", f"{m.armor:.1f}", rng_range(m.hit, P.STANDARD["spread"]),
                         f"{m.cycle:.1f}", m.kind, f"{M.threat_level(m):.1f}", f"{M.xp_reward(m):.0f}",
                         f"{M.cost_units(m, d):.2f}"])
        print(f"Depth {d}")
        table(["archetype", "hp", "armor", "damage", "cycle", "kind", "threat lvl", "xp", "cost"], rows)


def report_gear():
    print("Authoring targets per tier: Normal quality at the depth the tier is on curve\n")
    rows = []
    for t, intro in enumerate(P.GEAR["tier_intro"], 1):
        d = intro + P.GEAR["tier_lag"]
        ref = P.CLASSES["reference"]
        weapons = []
        for kind, w in P.WEAPONS.items():
            mean = M.reference_weapon_hit(d) * w["hit"]
            weapons.append(f"{kind} {rng_range(mean, P.GEAR['weapon_spread'])}")
        armor = [f"{slot}/{weight} {M.slot_armor(slot, weight, d, found=False):.1f}"
                 for slot in ("body", "helmet") for weight in P.GEAR["weight"]]
        armor.append(f"shield {M.slot_armor('shield', 'medium', d, found=False):.1f}")
        rows.append([f"T{t}", f"{intro}-{min(P.DEPTHS, intro + P.GEAR['tier_band'] - 1)}", d, "; ".join(weapons)])
        rows.append(["", "", "", "; ".join(armor)])
    table(["tier", "drops", "on curve", "values"], rows)


def report_classes():
    skill = P.SKILL[P.REFERENCE_SKILL]
    print(f"Class envelopes and duel cost against the standard monster ({P.REFERENCE_SKILL} skill)\n")
    for d in [1, 6, 12, 18, 24]:
        m = M.monster("standard", d)
        ref = M.duel_cost(m, M.hero("reference", d), skill)
        rows = []
        for name in P.CLASSES:
            h = M.hero(name, d)
            cost = M.duel_cost(m, h, skill)
            rows.append([name, f"{h.hp:.0f}", f"{h.armor:.1f}", rng_range(h.hit, P.GEAR["weapon_spread"]),
                         f"{h.crit:.0%}", f"{M.hero_dps(h, m, skill):.1f}", f"{h.mana:.0f}",
                         f"{cost:.1%}", f"{cost / ref:.2f}"])
        print(f"Depth {d}")
        table(["class", "hp", "armor", "hit", "crit", "dps", "mana", "duel cost", "vs ref"], rows)


def report_xp():
    need, cumulative = M.xp_ladder()
    levels = M.levels_by_depth(need, cumulative)
    total = 0
    rows = []
    for lvl, n in enumerate(need, 1):
        total += n
        rows.append([f"{lvl}->{lvl + 1}", n, total])
    print("XP ladder (fitted to the expected XP of cleared floors)\n")
    table(["level", "xp", "total"], rows)
    print("Arrival level per depth: " + ", ".join(f"{d}:{levels[d]}" for d in range(1, P.DEPTHS + 2)))


# --- Monte Carlo ------------------------------------------------------------------------------------

@cache
def hero_at(name, depth):
    return M.hero(name, depth)


@cache
def monster_at(archetype, depth):
    return M.monster(archetype, depth)


def pack_cost(pack, depth):
    return M.group_factor(len(pack["members"])) * sum(member_cost(a, depth) for a in pack["members"])


@cache
def member_cost(archetype, depth):
    return M.cost_units(monster_at(archetype, depth), depth)


def compose_floor(depth, rng):
    budget = M.lerp(P.FLOOR["budget"], depth)
    if depth % P.REALM_SIZE == 0:
        budget *= P.FLOOR["boss_budget_share"]
    eligible = M.eligible_packs(depth)
    weights = [p["weight"] for p in eligible]
    packs, misses = [], 0
    while misses < 4:
        pack = rng.choices(eligible, weights)[0]
        cost = pack_cost(pack, depth)
        if cost <= budget + 0.25:
            packs.append([monster_at(a, depth) for a in pack["members"]])
            budget -= cost
        else:
            misses += 1
    return packs


def poisson(rng, lam):
    k, p, limit = 0, 1.0, math.exp(-lam)
    while True:
        p *= rng.random()
        if p <= limit:
            return k
        k += 1


class Live:
    __slots__ = ("m", "hp", "max", "next", "engage", "support_next", "approached", "lo", "hi", "dps")

    def __init__(self, m, h, skill, rng, gap):
        self.m, self.hp, self.max = m, m.hp, m.hp
        if m.kind == "melee":
            self.engage = gap / m.speed if h.ranged or h.opener_hit else gap / (m.speed + P.ENGAGEMENT["close_speed"])
        else:
            self.engage = 0.3
        self.next = self.engage + rng.uniform(0, m.cycle * 0.5)
        self.support_next = self.engage
        self.approached = h.ranged or m.kind == "melee"
        per_dart = m.hit / m.darts
        self.lo, self.hi = M.spread_range(per_dart, P.STANDARD["spread"])
        self.dps = M.monster_dps(m, h, skill)


def phase_multiplier(u: Live):
    mult = 1.0
    for threshold, factor in u.m.phases:
        if u.hp < u.max * threshold:
            mult = factor
    return mult


def fight(h, state, members, skill, rng):
    """Resolve one pack against the hero; mutates `state` (hp, potions). Returns False when the hero dies."""
    gap = P.ENGAGEMENT["gap"]
    units = [Live(m, h, skill, rng, gap) for m in members]
    w = P.WEAPONS[h.weapon]
    spread = P.GEAR["weapon_spread"]
    eff = M.homing_efficiency(skill) if w.get("homing") else skill["efficiency"]
    spell = P.SPELLS.get(P.CLASSES[h.name].get("spell", ""), None)
    mana = state.setdefault("mana", h.mana)
    t, last, hero_next = 0.0, 0.0, 0.0
    melee_cap = P.ENGAGEMENT["melee_cap"]

    def hit_unit(u, mean, armor_scale=1.0):
        dmg = rng.uniform(*M.spread_range(mean, spread))
        if rng.random() < h.crit:
            dmg *= P.CRIT_MULTIPLIER
        dmg = max(0.0, dmg - rng.uniform(0, u.m.armor * armor_scale))
        u.hp -= dmg

    while True:
        alive = [u for u in units if u.hp > 0]
        if not alive:
            state["mana"] = mana
            return True
        engaged_melee = [u for u in alive if u.m.kind == "melee" and t >= u.engage][:melee_cap]
        attackers = set(engaged_melee) | {u for u in alive if u.m.kind != "melee" and t >= u.engage}
        nxt = min([hero_next] + [u.next for u in attackers] + [u.engage for u in alive if u.engage > t] or [hero_next])
        t = max(t, nxt)
        if h.mana_regen:
            mana = min(h.mana, mana + h.mana_regen * (t - last))
        last = t
        if hero_next <= t:
            if state["hp"] < skill["potion_at"] * h.hp and state["potions"] > 0:
                state["hp"] = min(h.hp, state["hp"] + P.SUPPLY["potion_heal"] * h.hp)
                state["potions"] -= 1
                hero_next = t + 1.0
                continue
            target = max(alive, key=lambda u: u.dps / max(u.hp, 1e-6))
            if not target.approached:
                target.approached = True
                hero_next = t + gap / P.ENGAGEMENT["close_speed"]
                continue
            if spell and mana >= spell["mana"] and (len(alive) >= 2 or target.hp > h.hit):
                mana -= spell["mana"]
                others = [u for u in alive if u is not target]
                extra = min(1.0, (spell["targets"] - 1) / len(others)) if others else 0
                for u in [target] + [u for u in others if rng.random() < extra]:
                    if rng.random() < eff:
                        hit_unit(u, h.spell_hit * spell["scale"], P.MAGIC_ARMOR_SHARE)
                hero_next = t + spell["cast"]
                continue
            opening = h.opener_hit and not any(u.m.kind == "melee" and t >= u.engage for u in alive)
            if opening:
                if rng.random() < skill["efficiency"] * (1 - target.m.guard):
                    hit_unit(target, h.opener_hit)
                hero_next = t + h.opener_cycle
                continue
            if rng.random() < eff * (1 - target.m.guard):
                if "darts" in w:
                    for _ in range(w["darts"]):
                        hit_unit(target, h.hit * w["dart_scale"], P.MAGIC_ARMOR_SHARE)
                else:
                    hit_unit(target, h.hit)
            cleave = w.get("cleave", 1.0) - 1
            neighbours = [u for u in engaged_melee if u is not target and u.hp > 0]
            if cleave > 0 and neighbours and rng.random() < cleave and rng.random() < eff:
                hit_unit(rng.choice(neighbours), h.hit)
            hero_next = t + h.cycle
            continue
        for u in sorted(attackers, key=lambda x: x.next):
            if u.next > t or u.hp <= 0:
                continue
            u.next = t + u.m.cycle
            if u.m.support and t >= u.support_next:
                hurt = [a for a in alive if a is not u and a.hp > 0 and a.hp < 0.75 * a.max]
                if hurt:
                    ally = min(hurt, key=lambda a: a.hp / a.max)
                    ally.hp = min(ally.max, ally.hp + u.m.support["heal"] * ally.max)
                    u.support_next = t + u.m.support["cycle"]
                    continue
            kind = "area" if u.m.area and rng.random() < u.m.area else u.m.kind
            if rng.random() < M.avoid(h, kind, skill):
                continue
            mult = phase_multiplier(u)
            for _ in range(u.m.darts):
                dmg = rng.uniform(u.lo, u.hi) * mult
                dmg = max(0.0, dmg - rng.uniform(0, h.armor))
                chance, extra = u.m.status
                if dmg > 0 and rng.random() < chance:
                    dmg += extra * (u.lo + u.hi) / 2
                state["hp"] -= dmg
            if state["hp"] <= 0:
                state["mana"] = mana
                return False
            break


def run(name, skill_name, rng):
    """Play one run; returns the depth reached (25 = victory)."""
    skill = P.SKILL[skill_name]
    state = {"hp": None, "potions": P.SUPPLY["starting_potions"]}
    food, previous_max = 0, 0.0
    for d in range(1, P.DEPTHS + 1):
        h = hero_at(name, d)
        state["hp"] = h.hp if state["hp"] is None else state["hp"] + (h.hp - previous_max)
        previous_max = h.hp
        state["potions"] += sum(1 for _ in range(poisson(rng, P.SUPPLY["healing_potions"]))
                                if rng.random() < skill["potion_known"])
        food += poisson(rng, P.SUPPLY["food"])
        if d in P.SUPPLY["fountain_depths"]:
            state["hp"] = h.hp
        packs = compose_floor(d, rng)
        if d % P.REALM_SIZE == 0:
            packs.append([monster_at("boss", d)])
        for pack in packs:
            while state["hp"] < 0.6 * h.hp and food > 0:
                state["hp"] = min(h.hp, state["hp"] + P.SUPPLY["food_heal"] * h.hp)
                food -= 1
            rest(h, state)
            if not fight(h, state, pack, skill, rng):
                return d
    return P.DEPTHS + 1


def rest(h, state):
    """Exploring between packs: mana regenerates for the rest time, health does not."""
    state["mana"] = min(h.mana, state.get("mana", h.mana) + h.mana_regen * P.ENGAGEMENT["rest"])


def floor_cost(name, depth, skill, rng, floors):
    """Mean share of max health one floor's packs take from a hero who cannot die and drinks nothing."""
    h = hero_at(name, depth)
    total = 0.0
    state = {"hp": 1e12, "potions": 0, "mana": h.mana}
    for _ in range(floors):
        for pack in compose_floor(depth, rng):
            state["hp"] = 1e12
            rest(h, state)
            fight(h, state, pack, skill, rng)
            total += (1e12 - state["hp"]) / h.hp
    return total / floors


def boss_cost(name, depth, skill, rng, fights):
    """Mean share of max health a gatekeeper takes from a hero who arrives with full mana and cannot die."""
    h = hero_at(name, depth)
    total = 0.0
    for _ in range(fights):
        state = {"hp": 1e12, "potions": 0, "mana": h.mana}
        fight(h, state, [monster_at("boss", depth)], skill, rng)
        total += (1e12 - state["hp"]) / h.hp
    return total / fights


def parity_table(costs, tolerance):
    rows = []
    for d, by_class in costs.items():
        mean = sum(by_class[c] for c in CLASS_NAMES) / len(CLASS_NAMES)
        row = [d, f"{mean:.0%}"]
        for name in ["reference"] + CLASS_NAMES:
            ratio = by_class[name] / mean
            row.append(f"{ratio:.2f}" + ("" if abs(ratio - 1) <= tolerance else "*"))
        rows.append(row)
    table(["depth", "class mean", "reference"] + CLASS_NAMES, rows)


def report_parity(floors, seed):
    rng = random.Random(seed)
    skill = P.SKILL[P.REFERENCE_SKILL]
    names = ["reference"] + CLASS_NAMES
    depths = [d for d in range(1, P.DEPTHS + 1) if d % P.REALM_SIZE == 0]
    floor = {d: {n: floor_cost(n, d, skill, rng, floors) for n in names} for d in depths}
    boss = {d: {n: boss_cost(n, d, skill, rng, floors) for n in names} for d in depths}
    realm = {d: {n: P.REALM_SIZE * floor[d][n] + boss[d][n] for n in names} for d in depths}
    print(f"Health lost relative to the class mean ({P.REFERENCE_SKILL} skill, no potions, {floors} samples). "
          "Below 1 is stronger; * = outside tolerance.\n")
    print(f"Realm: {P.REALM_SIZE} floors and the gatekeeper (parity, +-{P.PARITY_TOLERANCE:.0%})")
    parity_table(realm, P.PARITY_TOLERANCE)
    print(f"Ordinary floor (matchup, +-{P.MATCHUP_TOLERANCE:.0%})")
    parity_table(floor, P.MATCHUP_TOLERANCE)
    print(f"Gatekeeper (matchup, +-{P.MATCHUP_TOLERANCE:.0%})")
    parity_table(boss, P.MATCHUP_TOLERANCE)


def report_survival(runs, seed, classes):
    rng = random.Random(seed)
    print(f"Probability of arriving at a depth ({runs} runs each; 25 = victory)\n")
    shown = [2, 3, 4, 5, 7, 10, 13, 16, 19, 22, 25]
    flagged = []
    for skill_name in P.SKILL:
        rows = []
        for name in classes:
            reached = [run(name, skill_name, rng) for _ in range(runs)]
            row = [name]
            for d in shown:
                row.append(f"{sum(1 for r in reached if r >= d) / runs:.0%}")
            rows.append(row)
            for d, target in P.TARGETS.get(skill_name, {}).items():
                actual = sum(1 for r in reached if r >= d) / runs
                if abs(actual - target) > P.TARGET_TOLERANCE:
                    flagged.append(f"{skill_name} {name}: reach {d} = {actual:.0%}, target {target:.0%}")
        print(f"{skill_name} (targets: " + ", ".join(f"{d}: {p:.0%}" for d, p in P.TARGETS.get(skill_name, {}).items()) + ")")
        table(["class"] + [str(d) for d in shown], rows)
    print("Off target:" if flagged else "All targets met.")
    for f in flagged:
        print("  " + f)


# --- Content evaluation ------------------------------------------------------------------------------

def parse_range(text):
    lo, hi = (float(x) for x in text.split("-"))
    return lo, hi


def evaluate_weapon(args):
    d = P.GEAR["tier_intro"][args.tier - 1] + P.GEAR["tier_lag"]
    skill = P.SKILL[P.REFERENCE_SKILL]
    m = M.monster("standard", d)
    lo, hi = parse_range(args.damage)
    ref = M.hero("reference", d)
    budget = M.reference_weapon_hit(d)
    item = net_dps(lo, hi, args.cycle, m)
    reference = net_dps(*M.spread_range(budget, P.GEAR["weapon_spread"]), P.WEAPONS["sword_1h"]["cycle"], m)
    ratio = item / reference
    print(f"T{args.tier} on curve at depth {d}; reference sword {rng_range(budget, P.GEAR['weapon_spread'])} "
          f"every {P.WEAPONS['sword_1h']['cycle']}s")
    print(f"Damage per second vs the standard monster: {item:.1f} ({ratio:.0%} of the reference sword)")
    if args.hands == 2:
        shielded = M.duel_cost(m, M.hero("knight", d), skill)
        print("Two-handed: compare its DPS share against the off-hand it gives up (shield or second weapon); "
              f"a knight's shield saves {1 - shielded / M.duel_cost(m, ref, skill):.0%} of the duel cost at this depth.")


def net_dps(lo, hi, cycle, m):
    return M.net_hit(lo, hi, m.armor) / cycle


def evaluate_armor(args):
    d = P.GEAR["tier_intro"][args.tier - 1] + P.GEAR["tier_lag"]
    target = M.slot_armor(args.slot, args.weight, d, found=False)
    print(f"T{args.tier} {args.weight} {args.slot} on curve at depth {d}: target {target:.1f} armor; "
          f"this piece has {args.armor:g} ({args.armor / target:.0%} of target)")
    m, skill = M.monster("standard", d), P.SKILL[P.REFERENCE_SKILL]
    base = M.hero("reference", d)
    with_piece = M.hero("reference", d)
    with_piece.armor += args.armor - (target if args.slot != "shield" else 0)
    print(f"Reference duel cost vs standard: {M.duel_cost(m, base, skill):.1%} -> {M.duel_cost(m, with_piece, skill):.1%}")


def evaluate_monster(args):
    lo, hi = parse_range(args.damage)
    m = M.Monster(archetype="custom", depth=args.depth, hp=args.hp, armor=args.armor, hit=(lo + hi) / 2,
                  cycle=args.cycle, kind=args.kind, speed=args.speed, guard=args.guard, darts=args.darts,
                  boss=args.boss)
    level = M.threat_level(m)
    print(f"Threat level {level:.1f}; XP {M.xp_reward(m):.0f}")
    for d in sorted({max(1, round(level) - 3), args.depth, min(P.DEPTHS, round(level) + 3)}):
        print(f"  at depth {d}: costs {M.cost_units(m, d):.2f} standard monsters, "
              f"{M.duel_cost(m, M.hero('reference', d), P.SKILL[P.REFERENCE_SKILL]):.1%} of the reference hero's health")


# --- Game content ---------------------------------------------------------------------------------------

def report_audit(categories, match):
    """Every authored value the model sets that is off its target, with each category's summary."""
    off_total = 0
    for g in C.collect(categories, match):
        print(f"## {g.name}\n")
        if g.rows:
            table(g.headers, g.rows)
        off = [v for v in g.values if v.off > P.AUDIT_TOLERANCE]
        off_total += len(off)
        if off:
            table(["item", "value", "now", "target", "off"],
                  [[v.item, v.key, *v.shown, f"{v.off:.0%}"] for v in off])
        for note in g.notes:
            print(f"- {note}")
        if g.notes:
            print()
    print(f"{off_total} values off target by more than {P.AUDIT_TOLERANCE:.0%}. `derive` writes the targets.")


GODOT = R.ROOT / ".agents/skills/godot-mcp/scripts/godot.sh"


def report_supply(depths, seeds, samples):
    """Runs the game's loot supply probe headless and compares healing, food and gear per floor with SUPPLY."""
    import json
    import subprocess
    import tempfile
    out = Path(tempfile.mkdtemp()) / "supply.json"
    subprocess.run([str(GODOT), "--headless", "--path", str(R.ROOT), "res://scenes/debug/balance/loot_supply_probe.tscn",
                    "--", f"--supply-out={out}", f"--supply-depths={depths}", f"--supply-seeds={seeds}",
                    f"--supply-samples={samples}"], check=True, capture_output=True)
    report = json.loads(out.read_text())
    s = P.SUPPLY
    print(f"Items per floor from the real loot generator ({seeds} floors per depth), against SUPPLY\n")
    rows = []
    for depth, items in sorted(report.items(), key=lambda pair: int(pair[0])):
        potions = {k.split(":")[1]: v for k, v in items.items() if k.startswith("potion.healing:")}
        heal = sum(v * s["potion_sizes"][size] for size, v in potions.items())
        gear = ", ".join(f"{k.split(':')[1]} {v:.1f}" for k, v in sorted(items.items()) if k.startswith("gear:"))
        rows.append([depth, f"{items.get('monsters', 0):.1f}", f"{sum(potions.values()):.2f}", f"{s['healing_potions']:.2f}",
                     f"{heal:.0%}", f"{s['healing_potions'] * s['potion_heal']:.0%}", f"{items.get('food', 0):.2f}",
                     f"{s['food']:.2f}", gear])
    table(["depth", "monsters", "healing potions", "model", "healing", "model", "food", "model", "gear"], rows)


def read_trace(path):
    """Attacks of a combat-arena trace (`--combat-log`): who made each, its kind, and whether it landed."""
    import json
    attacks, parents, landed = {}, {}, set()
    for line in Path(path).read_text().splitlines():
        if not line.strip():
            continue
        record = json.loads(line.removeprefix("COMBAT "))
        event, data, actor = record["event"], record["data"], record["actor"]
        if event == "attack.started":
            key = (data["execution_source"], data["execution"])
            style = str(data.get("style", ""))
            # Casts fire homing missiles; a monster's "ranged" style shoots bolts; everything else is a melee swing.
            kind = "homing" if "cast" in style else "ranged" if style == "ranged" else "melee"
            attacks[key] = {"hero": "/Player/" in actor + "/", "kind": kind}
        elif event == "projectile.spawned" and "parent_source" in data:
            parent = (data["parent_source"], data["parent_execution"])
            parents[(data["execution_source"], data["execution"])] = parent
            if parent in attacks and attacks[parent]["kind"] == "melee":
                attacks[parent]["kind"] = "homing" if "Missile" in actor else "ranged"
        elif event == "damage.resolved" and data["outcome"] == "Damaged":
            key = (data["execution_source"], data["execution"])
            landed.add(parents.get(key, key))
    return [dict(a, landed=key in landed) for key, a in attacks.items()]


def report_calibration(paths):
    """Measured avoidance per attack kind and hero efficiency from traces, placed against the skill tiers."""
    attacks = [a for path in paths for a in read_trace(path)]

    def nearest(value, field, kind=None):
        tiers = {name: (s["avoid"][kind] if kind else s[field]) for name, s in P.SKILL.items()}
        return min(tiers, key=lambda name: abs(tiers[name] - value))

    rows = []
    for kind in ("melee", "ranged", "homing"):
        tried = [a for a in attacks if not a["hero"] and a["kind"] == kind]
        if tried:
            avoided = 1 - sum(a["landed"] for a in tried) / len(tried)
            rows.append([f"monster {kind} avoided", len(tried), f"{avoided:.0%}", nearest(avoided, "avoid", kind)])
    swings = [a for a in attacks if a["hero"]]
    if swings:
        efficiency = sum(a["landed"] for a in swings) / len(swings)
        rows.append(["hero attacks landed", len(swings), f"{efficiency:.0%}", nearest(efficiency, "efficiency")])
    print(f"Calibration from {len(paths)} trace(s); compare with SKILL in params.py\n")
    table(["measure", "attacks", "share", "closest skill tier"], rows)


def derive(categories, match):
    """Write every target into its .tres file; git shows what changed."""
    groups = C.collect(categories, match)
    written = 0
    for g in groups:
        for v in g.values:
            written += v.file.set(v.section, v.key, v.target)
        for note in g.notes:
            print(f"- {g.name}: {note}")
    changed = R.save_all()
    print(f"Wrote {written} values to {len(changed)} files:")
    for path in changed:
        print(f"  {R.to_res(path)}")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command")
    for name in ("progression", "monsters", "gear", "classes", "xp"):
        sub.add_parser(name)
    pa = sub.add_parser("parity")
    pa.add_argument("--floors", type=int, default=200)
    pa.add_argument("--seed", type=int, default=1)
    s = sub.add_parser("survival")
    s.add_argument("--runs", type=int, default=300)
    s.add_argument("--seed", type=int, default=1)
    s.add_argument("--classes", default=",".join(["reference"] + CLASS_NAMES))
    w = sub.add_parser("weapon")
    w.add_argument("--tier", type=int, required=True)
    w.add_argument("--damage", required=True)
    w.add_argument("--cycle", type=float, default=0.5)
    w.add_argument("--hands", type=int, default=1)
    a = sub.add_parser("armor")
    a.add_argument("--tier", type=int, required=True)
    a.add_argument("--slot", choices=["body", "helmet", "shield"], required=True)
    a.add_argument("--armor", type=float, required=True)
    a.add_argument("--weight", choices=list(P.GEAR["weight"]), default="medium")
    m = sub.add_parser("monster")
    m.add_argument("--depth", type=int, required=True)
    m.add_argument("--hp", type=float, required=True)
    m.add_argument("--armor", type=float, default=0)
    m.add_argument("--damage", required=True)
    m.add_argument("--cycle", type=float, default=2.2)
    m.add_argument("--kind", choices=["melee", "ranged", "homing"], default="melee")
    m.add_argument("--speed", type=float, default=2.8)
    m.add_argument("--guard", type=float, default=0.0)
    m.add_argument("--darts", type=int, default=1)
    m.add_argument("--boss", action="store_true")
    cal = sub.add_parser("calibrate")
    cal.add_argument("traces", nargs="+", help="combat-arena traces recorded with --combat-log")
    sp = sub.add_parser("supply")
    sp.add_argument("--depths", default="1-3")
    sp.add_argument("--seeds", type=int, default=12)
    sp.add_argument("--samples", type=int, default=40)
    for name in ("audit", "derive"):
        c = sub.add_parser(name)
        c.add_argument("--only", default=",".join(C.CATEGORIES), help="categories: " + ", ".join(C.CATEGORIES))
        c.add_argument("--match", action="append", help="only resources whose path contains this text (repeatable)")
    args = parser.parse_args()
    match args.command:
        case None | "progression":
            report_progression()
        case "monsters":
            report_monsters()
        case "gear":
            report_gear()
        case "classes":
            report_classes()
        case "xp":
            report_xp()
        case "parity":
            report_parity(args.floors, args.seed)
        case "survival":
            report_survival(args.runs, args.seed, args.classes.split(","))
        case "weapon":
            evaluate_weapon(args)
        case "armor":
            evaluate_armor(args)
        case "monster":
            evaluate_monster(args)
        case "calibrate":
            report_calibration(args.traces)
        case "supply":
            report_supply(args.depths, args.seeds, args.samples)
        case "audit" | "derive":
            categories = [c.strip() for c in args.only.split(",")]
            unknown = set(categories) - set(C.CATEGORIES)
            if unknown:
                parser.error(f"unknown categories: {', '.join(sorted(unknown))}")
            (report_audit if args.command == "audit" else derive)(categories, args.match)


if __name__ == "__main__":
    main()
