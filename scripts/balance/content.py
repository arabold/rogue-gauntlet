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

CATEGORIES = ("monsters", "weapons", "armor", "classes", "supplies", "loot")


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
        """Relative difference from the target (absolute when the target is zero); the largest one for a list."""
        if isinstance(self.target, R.Raw):
            return 0.0 if self.current == self.target else 1.0
        if isinstance(self.target, list):
            current = self.current if isinstance(self.current, list) else []
            if len(current) != len(self.target):
                return 1.0
            return max((_relative(c, t) for c, t in zip(current, self.target)), default=0.0)
        return _relative(self.current, self.target)

    @property
    def shown(self) -> tuple[str, str]:
        """Current and target value as the audit prints them."""
        def text(value):
            if isinstance(value, R.Raw):
                return value.text if len(value.text) <= 40 else value.text[:37] + "..."
            return f"{len(value)} entries" if isinstance(value, list) else f"{value:g}"
        return text(self.current), text(self.target)

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
        if isinstance(target, R.Raw):
            self.values.append(Value(item, file, section, key, R.Raw(file.raw(section, key) or ""), target))
            return
        current = file.get(section, key)
        current = current if isinstance(current, list) else float(current or 0)
        self.values.append(Value(item, file, section, key, current, target))

    def note(self, text: str):
        if text not in self.notes:
            self.notes.append(text)


def _relative(current: float, target: float) -> float:
    return abs(current) if target == 0 else abs(current - target) / abs(target)


def snap(value: float, unit: float, minimum: float = 0.0) -> float:
    return max(minimum, round(round(value / unit) * unit, 4))


def _selected(path, match) -> bool:
    return not match or any(m in str(path) for m in match)


def collect(categories=CATEGORIES, match=None) -> list[Group]:
    groups = [{"monsters": monsters, "weapons": weapons, "armor": armor, "classes": classes, "supplies": supplies, "loot": loot}[c](match)
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
            targets.setdefault(v.location, set()).add(tuple(v.target) if isinstance(v.target, list) else v.target)  # Raw is hashable
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


WEIGHTS = ("light", "medium", "heavy")  # ArmorWeight in Armor.cs, in declaration order


def _weight(file: R.TextResource, section: R.Section) -> str:
    value = file.get(section, "Weight")
    return WEIGHTS[value] if isinstance(value, int) else str(value).rsplit(".", 1)[-1].lower()


def armor(match=None) -> Group:
    """Body armor and helmets by slot, weight and tier; shields by tier. Body armor also gets its weight's speed."""
    g = Group("Armor", headers=["armor", "slot", "weight", "tier", "armor", "derived"])
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
        weight = "medium" if slot == "shield" else _weight(f, s)
        target = max(1, round(M.slot_armor(slot, weight, tier_depth(tier), found=False)))
        g.add(f"{name} (T{tier})", f, s, "ArmorPoints", target)
        if slot == "body":
            g.add(f"{name} (T{tier})", f, s, "SpeedModifier", P.GEAR["weight"][weight][2])
        g.rows.append([name, slot, "" if slot == "shield" else weight, tier, f.get(s, "ArmorPoints"), target])
    return g


# --- Classes --------------------------------------------------------------------------------------

ATTRIBUTE_KEYS = {"STR": "BaseStrength", "DEX": "BaseDexterity", "VIT": "BaseVitality", "INT": "BaseIntelligence"}
HERO_SHEET = "res://scenes/player/resources/player_stats.tres"


def _profile_values(cls: dict) -> dict:
    """A class's stat profile: how its attributes pay off and how much each grows per level."""
    growth = cls["growth"]
    return {"HealthPercentPerVitality": M._profile(cls, "hp_per_vit"),
            "PhysicalDamagePercentPerStrength": M._profile(cls, "damage_per_str"),
            "MagicDamagePercentPerIntelligence": M._profile(cls, "spell_per_int"),
            "ManaPerIntelligence": M._profile(cls, "mana_per_int"),
            "CritChancePerDexterity": M._profile(cls, "crit_per_dex"),
            "StrengthPerLevel": growth["STR"], "DexterityPerLevel": growth["DEX"],
            "VitalityPerLevel": growth["VIT"], "IntelligencePerLevel": growth["INT"]}


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


LEVELING = "res://scenes/player/resources/leveling_profile.tres"


def classes(match=None) -> Group:
    g = Group("Classes", headers=["class", "STR", "DEX", "VIT", "INT", "health", "derived", "starting weapons"])
    a = P.ATTRIBUTES
    if any("hp_per_vit" in c.get("profile", {}) for c in P.CLASSES.values()):
        raise SystemExit("params.CLASSES: per-class hp_per_vit needs a per-class BaseMaxHealth the game lacks")
    sheet = R.load(HERO_SHEET)
    hs = sheet.main
    if _selected(sheet.path, match):
        g.add("Hero sheet", sheet, hs, "BaseMaxHealth", a["hp_base"])
        g.add("Hero sheet", sheet, hs, "BaseMaxMana", a["mana_base"])
        g.add("Hero sheet", sheet, hs, "ManaRegenRate", a["mana_regen"])
        g.add("Hero sheet", sheet, hs, "BaseCritChance", 0.0)
        pf, p = sheet.follow(sheet.get(hs, "Profile"))
        for key, value in _profile_values(P.CLASSES["reference"]).items():
            g.add("Default profile", pf, p, key, value)
    leveling = R.load(LEVELING)
    if _selected(leveling.path, match):
        need, _ = M.xp_ladder()
        g.add("XP ladder", leveling, leveling.main, "XpToNextLevel", [int(n) for n in need])
        g.add("XP ladder", leveling, leveling.main, "HealthPerLevel", a["hp_per_level"])
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
        vitality = f.get(s, "BaseVitality") - 10
        health = base_health * max(0.0, 1 + pf.get(p, "HealthPercentPerVitality") * vitality)
        g.rows.append([name] + [f"{f.get(s, k):g}" for k in ATTRIBUTE_KEYS.values()]
                      + [f"{health:.0f}", f"{M.hero_hp(cls, 1):.0f}", ", ".join(sorted(kinds))])
    return g


# --- Supplies -------------------------------------------------------------------------------------

def _healing_buffs(file: R.TextResource, section: R.Section, seen=None):
    """Every `HealingBuff` a consumable reaches through its references (its own buff or its spell's effect)."""
    seen = seen if seen is not None else set()
    if (file.path, section.start) in seen:
        return []
    seen.add((file.path, section.start))
    script = file.script_of(section) or ""
    found = [(file, section)] if script.endswith("/HealingBuff.cs") else []
    for key in section.props:
        if key == "script" or key.startswith("metadata/"):
            continue
        value = file.get(section, key)
        for ref in value if isinstance(value, list) else [value]:
            if not isinstance(ref, R.Ref) or (ref.kind == "ExtResource" and not file.ext_path(ref.id).endswith(".tres")):
                continue
            found += _healing_buffs(*file.follow(ref), seen)
    return found


def _ticks(file: R.TextResource, buff: R.Section) -> int:
    return max(1, round(file.get(buff, "Duration", 1.0) * file.get(buff, "TicksPerSecond", 1.0)))


def supplies(match=None) -> Group:
    """Healing potions and food restore their model share of max health, spread over their buff's ticks."""
    g = Group("Supplies", headers=["item", "heals", "derived"])
    s = P.SUPPLY
    items = [(path, s["potion_sizes"][path.stem.rsplit("_", 1)[1]])
             for path in sorted(R.ROOT.glob("scenes/items/potions/potion_healing_*.tres"))]
    items += [(path, s["food_heal"]) for path in sorted(R.ROOT.glob("scenes/items/food/**/*.tres"))]
    for path, share in items:
        if not _selected(path, match):
            continue
        f = R.load(path)
        name = f.get(f.main, "Name") or path.stem
        for bf, buff in _healing_buffs(f, f.main):
            ticks = _ticks(bf, buff)
            g.add(name, bf, buff, "MaxHealthFractionPerTick", round(share / ticks, 4))
            g.rows.append([name, f"{bf.get(buff, 'MaxHealthFractionPerTick', 0.0) * ticks:.0%}", f"{share:.0%}"])
    return g


# --- Loot tiers -----------------------------------------------------------------------------------

TIER_BANDS = "res://scenes/items/quality/tier_depth_bands.tres"
TIERED_POOLS = ("res://scenes/items/loot/pools/weapons.tres", "res://scenes/items/loot/pools/armor.tres")


def tier_bands() -> list[tuple[int, int]]:
    """Depths each tier drops at: from its introduction for `tier_band` depths; the last tier never stops (0)."""
    intros = P.GEAR["tier_intro"]
    bands = []
    for i, intro in enumerate(intros):
        last = intro + P.GEAR["tier_band"] - 1
        bands.append((intro, 0 if i == len(intros) - 1 or last >= P.DEPTHS else last))
    return bands


def loot(match=None) -> Group:
    """The tier depth bands and every tiered loot-pool entry's depth window, from the model's tier introductions."""
    g = Group("Loot", headers=["tier", "drops at depths", "derived"])
    bands = tier_bands()
    f = R.load(TIER_BANDS)
    if _selected(f.path, match):
        current = f.raw(f.main, "Bands") or ""
        g.add("Tier bands", f, f.main, "Bands",
              R.Raw("Array[Vector2i]([" + ", ".join(f"Vector2i({a}, {b})" for a, b in bands) + "])"))
        old = [tuple(int(x) for x in pair.split(",")) for pair in re.findall(r"Vector2i\(([^)]*)\)", current)]
        for tier, band in enumerate(bands, 1):
            was = old[tier - 1] if tier <= len(old) else None
            g.rows.append([f"T{tier}", f"{was[0]}-{was[1] or ''}" if was else "", f"{band[0]}-{band[1] or ''}"])
    for pool_path in TIERED_POOLS:
        pool = R.load(pool_path)
        if not _selected(pool.path, match):
            continue
        for ref in pool.get(pool.main, "Items", []):
            ef, entry = pool.follow(ref)
            item = ef.get(entry, "Item")
            if not isinstance(item, R.Ref):
                continue
            itf, it = ef.follow(item)
            if itf.script_class not in WEAPON_CLASSES + ("Armor",):
                continue
            tier = int(itf.get(it, "Tier", 1))
            label = f"{itf.get(it, 'Name') or itf.path.stem} (T{tier})"
            low, high = bands[tier - 1]
            g.add(label, ef, entry, "MinDepth", low)
            g.add(label, ef, entry, "MaxDepth", high)
    return g
