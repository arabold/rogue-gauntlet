"""
Read and edit Godot text resources (`.tres`) in place, for the balance tools.

Covers what `content.py` needs: sections, scalar and reference properties, defaults read from the C# script that
declares a property, and line edits that keep everything else in the file untouched.
"""
from __future__ import annotations

import re
from dataclasses import dataclass, field
from functools import cache
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

_HEADER = re.compile(r"^\[(\w+)(.*)\]\s*$")
_ATTR = re.compile(r'(\w+)=("[^"]*"|\S+)')
_PROPERTY = re.compile(r"^([A-Za-z_][\w/]*) = (.*)$")
_REF = re.compile(r'(ExtResource|SubResource)\("([^"]+)"\)')


@dataclass(frozen=True)
class Ref:
    """A reference to an external or sub-resource by its id in the file."""
    kind: str  # "ExtResource" or "SubResource"
    id: str


@dataclass
class Section:
    kind: str                       # gd_resource, ext_resource, sub_resource or resource
    attrs: dict
    start: int                      # line index of the header
    props: dict = field(default_factory=dict)  # key -> (first line, last line, raw value)

    @property
    def id(self) -> str | None:
        return self.attrs.get("id")


def res_path(res: str) -> Path:
    return ROOT / res.removeprefix("res://")


def to_res(path: Path) -> str:
    return "res://" + path.resolve().relative_to(ROOT).as_posix()


def parse_value(raw: str):
    """A property value as Python: numbers, strings, booleans, references, or a list of references."""
    raw = raw.strip()
    if raw in ("true", "false"):
        return raw == "true"
    if raw.startswith('&"') or raw.startswith('"'):
        return raw[raw.index('"') + 1:-1]
    try:
        return int(raw)
    except ValueError:
        pass
    try:
        return float(raw)
    except ValueError:
        pass
    if raw.startswith("Packed") and "Array(" in raw:  # PackedInt32Array(1, 2) and the like
        inner = raw[raw.index("(") + 1:raw.rindex(")")].strip()
        return [parse_value(x) for x in inner.split(",")] if inner else []
    if raw.startswith(("ExtResource", "SubResource")):
        kind, id_ = _REF.match(raw).groups()
        return Ref(kind, id_)
    if raw.startswith("Array["):  # typed array: skip the element type, keep the elements
        raw = raw[raw.index("](") + 1:]
    if raw.startswith(("[", "(")):
        return [Ref(kind, id_) for kind, id_ in _REF.findall(raw)]
    return raw


def format_value(value) -> str:
    """Godot's text form of a number (floats keep a decimal point, without float noise) or of a list of integers."""
    if isinstance(value, list):
        return f"PackedInt32Array({', '.join(str(int(v)) for v in value)})"
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, int):
        return str(value)
    text = f"{value:.4f}".rstrip("0")
    return text + "0" if text.endswith(".") else text


class TextResource:
    """One `.tres` file. Edits change single property lines; `save` writes only when something changed."""

    def __init__(self, path: Path | str):
        self.path = res_path(path) if str(path).startswith("res://") else Path(path)
        self.lines = self.path.read_text().split("\n")
        self.sections: list[Section] = []
        self.dirty = False
        self._parse()

    def _parse(self):
        current, i = None, 0
        while i < len(self.lines):
            line = self.lines[i]
            header = _HEADER.match(line)
            if header:
                current = Section(header.group(1), dict(_ATTR.findall(header.group(2))), i)
                current.attrs = {k: v.strip('"') for k, v in current.attrs.items()}
                self.sections.append(current)
            elif current and (prop := _PROPERTY.match(line)):
                start, raw = i, prop.group(2)
                while _unbalanced(raw) and i + 1 < len(self.lines):
                    i += 1
                    raw += "\n" + self.lines[i]
                current.props[prop.group(1)] = (start, i, raw)
            i += 1

    # --- Reading ---------------------------------------------------------------------------------

    @property
    def script_class(self) -> str | None:
        return self.sections[0].attrs.get("script_class")

    @property
    def main(self) -> Section:
        return next(s for s in self.sections if s.kind == "resource")

    def sub(self, id_: str) -> Section:
        return next(s for s in self.sections if s.kind == "sub_resource" and s.id == id_)

    def ext_path(self, id_: str) -> str:
        return next(s for s in self.sections if s.kind == "ext_resource" and s.id == id_).attrs["path"]

    def has(self, section: Section, key: str) -> bool:
        return key in section.props

    def get(self, section: Section, key: str, default=None):
        """A property's value, else the default its C# script declares, else `default`."""
        if key in section.props:
            return parse_value(section.props[key][2])
        script = self.script_of(section)
        declared = script_default(script, key) if script else None
        return default if declared is None else declared

    def script_of(self, section: Section) -> str | None:
        ref = parse_value(section.props["script"][2]) if "script" in section.props else None
        return self.ext_path(ref.id) if isinstance(ref, Ref) else None

    def follow(self, ref: Ref) -> tuple[TextResource, Section]:
        """The resource a reference points at: a sub-resource here, or another file's main resource."""
        if ref.kind == "SubResource":
            return self, self.sub(ref.id)
        other = load(self.ext_path(ref.id))
        return other, other.main

    # --- Editing ---------------------------------------------------------------------------------

    def set(self, section: Section, key: str, value) -> bool:
        """Write `value`; returns whether the file changed. Omitted properties stay omitted at their default."""
        section = self._current(section)
        text = format_value(value)
        current = self.get(section, key)
        if isinstance(value, list) and current == value:
            return False
        if isinstance(current, (int, float)) and not isinstance(current, bool) and abs(current - value) < 1e-9:
            return False
        if key in section.props:
            start, end, raw = section.props[key]
            if end != start:
                raise ValueError(f"{self.path}: cannot rewrite multi-line property {key}")
            self.lines[start] = f"{key} = {text}"
        else:
            self._insert(section, f"{key} = {text}")
        self.dirty = True
        self._reparse()
        return True

    def _current(self, section: Section) -> Section:
        """The live section matching one parsed before earlier edits moved its lines."""
        return next(s for s in self.sections if s.kind == section.kind and s.id == section.id)

    def _insert(self, section: Section, line: str):
        props = sorted(section.props.values())
        metadata = [p for k, p in section.props.items() if k.startswith("metadata/")]
        if metadata:
            at = min(p[0] for p in metadata)
        elif props:
            at = max(p[1] for p in props) + 1
        else:
            at = section.start + 1
        self.lines.insert(at, line)

    def _reparse(self):
        self.sections = []
        self._parse()

    def save(self):
        if self.dirty:
            self.path.write_text("\n".join(self.lines))
            self.dirty = False


def _unbalanced(text: str) -> bool:
    depth = 0
    in_string = False
    for ch in text:
        if ch == '"':
            in_string = not in_string
        elif not in_string and ch in "[{(":
            depth += 1
        elif not in_string and ch in "]})":
            depth -= 1
    return depth > 0


_LOADED: dict[Path, TextResource] = {}


def load(path: Path | str) -> TextResource:
    """Shared instance per file, so every edit to one file lands in one buffer."""
    resource = TextResource(path)
    return _LOADED.setdefault(resource.path, resource)


def save_all() -> list[Path]:
    changed = [r.path for r in _LOADED.values() if r.dirty]
    for r in _LOADED.values():
        r.save()
    return changed


# --- C# declared defaults -------------------------------------------------------------------------------

_CLASS = re.compile(r"partial class (\w+)\s*:\s*(\w+)")


@cache
def _class_files() -> dict[str, Path]:
    files = {}
    for path in ROOT.glob("scenes/**/*.cs"):
        for name, _ in _CLASS.findall(path.read_text()):
            files.setdefault(name, path)
    return files


_ZERO = {"float": 0.0, "double": 0.0, "int": 0, "uint": 0, "bool": False}


@cache
def script_default(script_res: str, key: str):
    """
    The value Godot gives `key` when a file omits it: the property's initializer in the script or one of its base
    classes, or the type's zero. None for anything else.
    """
    path = res_path(script_res)
    seen = set()
    while path and path not in seen:
        seen.add(path)
        source = path.read_text()
        match = re.search(rf"public\s+([\w.<>\[\]?]+)\s+{re.escape(key)}\s*\{{[^}}]*\}}\s*(?:=\s*([^;]+))?", source)
        if match:
            type_, raw = match.group(1), (match.group(2) or "").strip()
            if not raw:
                return _ZERO.get(type_)
            return parse_value(raw[:-1] if re.fullmatch(r"-?[\d.]+[fF]", raw) else raw)
        base = _CLASS.search(source)
        path = _class_files().get(base.group(2)) if base else None
    return None
