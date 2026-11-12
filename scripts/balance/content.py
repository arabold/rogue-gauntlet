"""
The game's authored content in balance-model terms.

For every value the model sets (monster stats and attacks, weapon damage and timing, armor, class sheets) this module
pairs the value in its `.tres` with the model's target. `simulate.py audit` reports the pairs that differ;
`simulate.py derive` writes the targets.

How content maps to the model:
- Monsters carry `Archetype` and `BalanceDepth` in their definition. The first damaging action in their combat profile
  takes the archetype's hit and rhythm; further damaging actions keep their damage ratio to it and their authored
  cooldowns, which set how often signature attacks come around.
- Weapons get their kind from their attack style (or `MagicStaff` for wands and staffs) and their target from `Tier`.
  A combo's average damage and duration scales count toward the hit and the seconds per attack.
- Armor gets its slot from `ValidSlots` and its target from `Tier`.
- Classes match the model's classes by `Id`.
"""
from __future__ import annotations

import re
from dataclasses import dataclass, field, replace

import model as M
import params as P
import resources as R

CATEGORIES = ("monsters", "weapons", "armor", "classes")


@dataclass
class Value:
    """One authored value the model sets."""
    item: str
    file: R.TextResource
    section: R.Section
    key: str
    current: float
    target: float

    @property
    def off(self) -> float:
        """Relative difference from the target; absolute when the target is zero."""
        if self.target == 0:
            return abs(self.current)
        return abs(self.current - self.target) / abs(self.target)

    @property
    def location(self):
        return self.file.path, self.section.start, self.key


@dataclass
class Group:
    """One category of content: the values the model sets, a summary table and notes that need a person."""
    name: str
    headers: list[str] = field(default_factory=list)
    rows: list[list] = field(default_factory=list)
    values: list[Value] = field(default_factory=list)
    notes: list[str] = field(default_factory=list)

    def add(self, item: str, file: R.TextResource, section: R.Section, key: str, target):
        current = file.get(section, key)
        self.values.append(Value(item, file, section, key, float(current or 0), target))

    def note(self, text: str):
        if text not in self.notes:
            self.notes.append(text)


def snap(value: float, unit: float, minimum: float = 0.0) -> float:
    return max(minimum, round(round(value / unit) * unit, 4))


def _selected(path, match) -> bool:
    return not match or any(m in str(path) for m in match)


def collect(categories=CATEGORIES, match=None) -> list[Group]:
    groups = [{"monsters": monsters, "weapons": weapons, "armor": armor, "classes": classes}[c](match)
              for c in categories]
    _merge_shared(groups)
    return groups


def _merge_shared(groups):
    """
    A resource shared by several items (a staff's missiles, a combat profile) appears once. When the items want
    different values for it, it keeps its value and gets a note instead.
    """
    targets, seen = {}, set()
    for g in groups:
        for v in g.values:
            targets.setdefault(v.location, set()).add(v.target)
    for g in groups:
        conflicts = {v.location for v in g.values if len(targets[v.location]) > 1}
        for path, _, key in sorted(conflicts):
            g.note(f"{R.to_res(path)} {key}: shared by items with different targets; left alone")
        unique = []
        for v in g.values:
            if v.location not in conflicts and v.location not in seen:
                seen.add(v.location)
                unique.append(v)
        g.values = unique


# --- Monsters -------------------------------------------------------------------------------------

ARCHETYPE_ENUM = R.ROOT / "scenes/enemies/MonsterArchetype.cs"


def archetype_names() -> list[str]:
    """The C# enum's members in declaration order, which is how Godot stores them, checked against the model."""
    body = re.search(r"enum MonsterArchetype\s*\{(.*?)\}", ARCHETYPE_ENUM.read_text(), re.S).group(1)
    names = [line.strip().rstrip(",") for line in body.splitlines()
             if line.strip() and not line.strip().startswith("//")]
    missing = [n for n in names[1:] if n.lower() not in P.ARCHETYPES]
    if missing:
        raise SystemExit(f"{ARCHETYPE_ENUM.name}: params.ARCHETYPES has no {', '.join(missing)}")
    return [n.lower() for n in names]


def _damaging_actions(file: R.TextResource, definition: R.Section):
    behavior = file.get(definition, "Behavior")
    if not isinstance(behavior, R.Ref):
        return []
    bf, b = file.follow(behavior)
    combat = bf.get(b, "Combat")
    if not isinstance(combat, R.Ref):
        return []
    cf, c = bf.follow(combat)
    actions = [cf.follow(ref) for ref in cf.get(c, "Actions", [])]
    return [(f, a) for f, a in actions if (f.get(a, "MaxDamage") or 0) > 0]


def _volley(file: R.TextResource, action: R.Section) -> tuple[int, float]:
    """Projectiles per attack and the share of the action's damage each deals."""
    attack = file.get(action, "Attack")
    if not isinstance(attack, R.Ref):
        return 1, 1.0
    af, a = file.follow(attack)
    if not af.get(a, "IsRanged"):
        return 1, 1.0
    return int(af.get(a, "ProjectileCount", 1)), float(af.get(a, "ProjectileDamageScale", 1.0))


THREAT_PROFILE = "res://scenes/levels/encounters/threat_profile.tres"


def _threat_profile(g: Group, match):
    """The constants the game turns threat levels into XP and floor budgets with."""
    f = R.load(THREAT_PROFILE)
    if not _selected(f.path, match):
        return
    s = f.main
    first, last = P.FLOOR["budget"]
    for key, value in {"StandardXp": P.XP["standard_xp"], "XpGrowth": P.XP["growth"] or M.cost_growth(),
                       "GatekeeperXpMultiplier": P.XP["boss_multiplier"], "CostGrowth": round(M.cost_growth(), 4),
                       "FirstFloorBudget": first, "LastFloorBudget": last, "LastDepth": P.DEPTHS,
                       "GatekeeperFloorShare": P.FLOOR["boss_budget_share"]}.items():
        g.add("Threat profile", f, s, key, value)


def monsters(match=None) -> Group:
    g = Group("Monsters", headers=["monster", "archetype", "sized for depth", "threat stored", "threat now",
                                   "threat derived", "XP derived"])
    names = archetype_names()
    _threat_profile(g, match)
    for path in sorted(R.ROOT.glob("scenes/enemies/*/*_definition.tres")):
        if "templates" in path.parts or not _selected(path, match):
            continue
        d = R.load(path)
        s = d.main
        name = d.get(s, "DisplayName") or path.stem
        archetype = names[d.get(s, "Archetype", 0)]
        if archetype == "none":
            g.note(f"{name}: no archetype; choose one and a balance depth in its definition")
            continue
        depth = float(d.get(s, "BalanceDepth", 1.0))
        attacks = _damaging_actions(d, s)
        if not attacks:
            g.note(f"{name}: no damaging action in its combat profile")
            continue

        target = M.monster(archetype, depth)
        pf, primary = attacks[0]
        darts, dart_scale = _volley(pf, primary)
        lo, hi = M.spread_range(target.hit / darts / dart_scale, P.STANDARD["spread"])
        lo, hi = snap(lo, 0.5, 0.5), snap(hi, 0.5, 0.5)
        cooldown = snap(target.cycle, 0.05)
        duration = pf.get(primary, "Duration")
        derived = replace(target, hp=float(max(1, round(target.hp))), armor=snap(target.armor, 0.5),
                          hit=(lo + hi) / 2 * dart_scale * darts, cycle=max(cooldown, duration),
                          speed=round(target.speed, 1), darts=darts)
        old_lo, old_hi, old_cooldown = pf.get(primary, "MinDamage"), pf.get(primary, "MaxDamage"), pf.get(primary, "Cooldown")
        now = replace(target, hp=d.get(s, "MaxHealth"), armor=d.get(s, "Armor"),
                      hit=(old_lo + old_hi) / 2 * dart_scale * darts, cycle=max(old_cooldown, duration),
                      speed=d.get(s, "MoveSpeed"), darts=darts)
        threat = M.threat_level(derived)

        g.add(name, d, s, "MaxHealth", derived.hp)
        g.add(name, d, s, "Armor", derived.armor)
        g.add(name, d, s, "MoveSpeed", derived.speed)
        g.add(name, d, s, "ThreatLevel", round(threat, 2))
        damage_scale = (lo + hi) / (old_lo + old_hi)
        g.add(f"{name}: {pf.get(primary, 'resource_name') or primary.id}", pf, primary, "Cooldown", cooldown)
        for f, a in attacks:
            # Signature attacks keep their authored cooldowns, which set how often they come around.
            label = f"{name}: {f.get(a, 'resource_name') or a.id}"
            new_lo, new_hi = (lo, hi) if a is primary else (snap(f.get(a, "MinDamage") * damage_scale, 0.5),
                                                              snap(f.get(a, "MaxDamage") * damage_scale, 0.5, 0.5))
            g.add(label, f, a, "MinDamage", new_lo)
            g.add(label, f, a, "MaxDamage", new_hi)
        g.rows.append([name, archetype, f"{depth:g}", f"{d.get(s, 'ThreatLevel', 1.0):.1f}",
                       f"{M.threat_level(now):.1f}", f"{threat:.1f}",
                       f"{M.xp_reward(derived):.0f}"])
    return g


# --- Weapons --------------------------------------------------------------------------------------

STYLE_KINDS = {"sword.1h": "sword_1h", "sword.2h": "sword_2h", "axe.1h": "axe_1h", "axe.2h": "axe_2h",
               "blunt.1h": "blunt_1h", "blunt.2h": "blunt_2h", "spear.2h": "spear_2h", "dagger.1h": "dagger",
               "crossbow": "crossbow"}
WEAPON_CLASSES = ("Weapon", "RangedWeapon", "MagicStaff")


def weapon_kind(file: R.TextResource, section: R.Section) -> str | None:
    if file.script_class == "MagicStaff":
        return "staff" if file.get(section, "IsTwoHanded") else "wand"
    style = file.get(section, "AttackStyle") or ""
    return next((kind for prefix, kind in STYLE_KINDS.items() if style.startswith(prefix)), None)


def tier_depth(tier: int) -> int:
    """The depth a tier is on curve: its introduction plus the lag before the expected hero wears it."""
    return P.GEAR["tier_intro"][tier - 1] + P.GEAR["tier_lag"]


def _combo_scales(file: R.TextResource, section: R.Section) -> tuple[float, float]:
    """Average damage and duration scale over a combo's steps; (1, 1) without a combo."""
    combo = file.get(section, "PrimaryCombo")
    if not isinstance(combo, R.Ref):
        return 1.0, 1.0
    cf, c = file.follow(combo)
    steps = [cf.follow(ref) for ref in cf.get(c, "Steps", [])]
    if not steps:
        return 1.0, 1.0
    return (sum(f.get(s, "DamageScale", 1.0) for f, s in steps) / len(steps),
            sum(f.get(s, "DurationScale", 1.0) for f, s in steps) / len(steps))


def _missiles(file: R.TextResource, section: R.Section):
    """The attack definitions of a staff's missile set."""
    missiles = file.get(section, "Missiles")
    if not isinstance(missiles, R.Ref):
        return []
    mf, m = file.follow(missiles)
    out = []
    for key in m.props:
        ref = mf.get(m, key)
        if isinstance(ref, R.Ref) and ref.kind == "ExtResource" and mf.ext_path(ref.id).endswith(".tres"):
            af, a = mf.follow(ref)
            if af.script_class == "AttackDefinition":
                out.append((af, a))
    return out


def weapons(match=None) -> Group:
    g = Group("Weapons", headers=["weapon", "kind", "tier", "damage", "derived", "seconds", "derived"])
    for path in sorted(R.ROOT.glob("scenes/items/weapons/**/*.tres")):
        if not _selected(path, match):
            continue
        f = R.load(path)
        s = f.main
        if f.script_class not in WEAPON_CLASSES:
            continue
        name = f.get(s, "Name") or path.stem
        kind = weapon_kind(f, s)
        if kind is None:
            g.note(f"{name}: attack style {f.get(s, 'AttackStyle')!r} matches no weapon kind in params.WEAPONS")
            continue
        tier, w = int(f.get(s, "Tier", 1)), P.WEAPONS[kind]
        damage_scale, duration_scale = _combo_scales(f, s)
        mean = M.reference_weapon_hit(tier_depth(tier)) * w["hit"] / damage_scale
        lo, hi = (snap(x, 0.5, 0.5) for x in M.spread_range(mean, P.GEAR["weapon_spread"]))
        fixed = f.get(s, "Delay", 0.0) + f.get(s, "CooldownDuration", 0.0)
        perform = round((w["cycle"] - fixed) / duration_scale, 2)
        label = f"{name} (T{tier})"
        g.add(label, f, s, "DamageMin", lo)
        g.add(label, f, s, "DamageMax", hi)
        g.add(label, f, s, "PerformDuration", perform)
        for af, a in _missiles(f, s):
            g.add(f"{label}: {af.path.stem}", af, a, "ProjectileDamageScale", w["dart_scale"])
            if af.get(a, "ProjectileCount", 1) != w["darts"]:
                g.note(f"{af.path.stem}: {af.get(a, 'ProjectileCount', 1)} missiles, the model assumes {w['darts']}")
        if (w["hands"] == 2) != bool(f.get(s, "IsTwoHanded")):
            g.note(f"{name}: IsTwoHanded disagrees with the model's {kind} ({w['hands']} hands)")
        g.rows.append([name, kind, tier, f"{f.get(s, 'DamageMin'):g}-{f.get(s, 'DamageMax'):g}", f"{lo:g}-{hi:g}",
                       f"{fixed + f.get(s, 'PerformDuration') * duration_scale:.2f}", f"{w['cycle']:.2f}"])
    return g


# --- Armor ----------------------------------------------------------------------------------------

SLOT_FLAGS = {1 << 0: "helmet", 1 << 1: "body", 1 << 9: "shield"}  # EquipmentSlot in Inventory.cs


def armor(match=None) -> Group:
    g = Group("Armor", headers=["armor", "slot", "tier", "armor", "derived"])
    for path in sorted(R.ROOT.glob("scenes/items/armor/**/*.tres")):
        if not _selected(path, match):
            continue
        f = R.load(path)
        s = f.main
        if f.script_class != "Armor":
            continue
        name = f.get(s, "Name") or path.stem
        slot = SLOT_FLAGS.get(f.get(s, "ValidSlots", 0))
        if slot is None:
            g.note(f"{name}: slot {f.get(s, 'ValidSlots')} has no armor target")
            continue
        tier = int(f.get(s, "Tier", 1))
        target = max(1, round(M.slot_armor(slot, "medium", tier_depth(tier), found=False)))
        g.add(f"{name} (T{tier})", f, s, "ArmorPoints", target)
        g.rows.append([name, slot, tier, f.get(s, "ArmorPoints"), target])
    return g


# --- Classes --------------------------------------------------------------------------------------

ATTRIBUTE_KEYS = {"STR": "BaseStrength", "DEX": "BaseDexterity", "VIT": "BaseVitality", "INT": "BaseIntelligence"}
HERO_SHEET = "res://scenes/player/resources/player_stats.tres"


def _profile_values(cls: dict) -> dict:
    """Stat profile coefficients that reproduce the model's level-1 hero with the game's flat formulas."""
    return {"HealthPerVitality": P.ATTRIBUTES["hp_base"] * M._profile(cls, "hp_per_vit"),
            "ManaPerIntelligence": M._profile(cls, "mana_per_int"),
            "CritChancePerDexterity": M._profile(cls, "crit_per_dex")}


def _kit_kinds(file: R.TextResource, section: R.Section) -> set[str]:
    kinds = set()
    for ref in file.get(section, "StartingItems", []):
        sf, slot = file.follow(ref)
        item = sf.get(slot, "Item")
        if isinstance(item, R.Ref):
            itf, it = sf.follow(item)
            if itf.script_class in WEAPON_CLASSES and (kind := weapon_kind(itf, it)):
                kinds.add(kind)
    return kinds


def classes(match=None) -> Group:
    g = Group("Classes", headers=["class", "STR", "DEX", "VIT", "INT", "health", "derived", "starting weapons"])
    a = P.ATTRIBUTES
    if any("hp_per_vit" in c.get("profile", {}) for c in P.CLASSES.values()):
        raise SystemExit("params.CLASSES: per-class hp_per_vit needs a per-class BaseMaxHealth the game lacks")
    sheet = R.load(HERO_SHEET)
    hs = sheet.main
    if _selected(sheet.path, match):
        # At level 1, base x (1 + k (VIT - 10)) equals the game's flat BaseMaxHealth + VIT x HealthPerVitality.
        g.add("Hero sheet", sheet, hs, "BaseMaxHealth", a["hp_base"] * (1 - 10 * a["hp_per_vit"]))
        g.add("Hero sheet", sheet, hs, "BaseMaxMana", a["mana_base"])
        g.add("Hero sheet", sheet, hs, "ManaRegenRate", a["mana_regen"])
        g.add("Hero sheet", sheet, hs, "BaseCritChance", 0.0)
        pf, p = sheet.follow(sheet.get(hs, "Profile"))
        for key, value in _profile_values(P.CLASSES["reference"]).items():
            g.add("Default profile", pf, p, key, value)
    base_health = sheet.get(hs, "BaseMaxHealth")
    for path in sorted(R.ROOT.glob("scenes/player/classes/class_*.tres")):
        if not _selected(path, match):
            continue
        f = R.load(path)
        s = f.main
        name, cls = f.get(s, "DisplayName") or path.stem, P.CLASSES.get(f.get(s, "Id"))
        if cls is None:
            g.note(f"{name}: no class {f.get(s, 'Id')!r} in params.CLASSES")
            continue
        for attr, key in ATTRIBUTE_KEYS.items():
            g.add(name, f, s, key, float(cls["attrs"][attr]))
        pf, p = f.follow(f.get(s, "StatProfile"))
        for key, value in _profile_values(cls).items():
            g.add(f"{name} profile", pf, p, key, value)
        kinds = _kit_kinds(f, s)
        expected = {cls["weapon"]} | ({cls["opener"]} if "opener" in cls else set())
        if not expected <= kinds:
            g.note(f"{name}: starts with {', '.join(sorted(kinds)) or 'no weapon'}; "
                           f"the model expects {', '.join(sorted(expected))}")
        health = base_health + f.get(s, "BaseVitality") * pf.get(p, "HealthPerVitality")
        g.rows.append([name] + [f"{f.get(s, k):g}" for k in ATTRIBUTE_KEYS.values()]
                      + [f"{health:.0f}", f"{M.hero_hp(cls, 1):.0f}", ", ".join(sorted(kinds))])
    g.note("Attribute damage (DamagePerStrength, DamagePerIntelligence) and the hero's base damage stay as "
                   "they are until attributes become percentages (the leveling step of the plan).")
    return g
