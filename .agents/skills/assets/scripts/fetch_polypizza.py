#!/usr/bin/env python3
"""
Poly Pizza fetcher: discovery + single-model download. Poly Pizza indexes Quaternius,
Kenney, and many others, so this one keyed API spans most of our non-KayKit sources.
Licences are a mix of CC0 and CC-BY (CC-BY requires attribution -- captured below).

Reads POLYPIZZA_KEY from the environment (source it from the repo .env). Stdlib only.

Usage:
    python3 fetch_polypizza.py search <query> [--limit N] [--cc0] [--max-tris N]
    python3 fetch_polypizza.py get <ID> <dest_dir>

`search` prints the facts that decide style fit: triangle count (low-poly?), licence,
and Animated. `get` downloads the GLB and prints a ready-to-paste ATTRIBUTIONS.json entry
(with the required CC-BY attribution string). Then run the Godot import. See assets/AGENTS.md.
"""
import json, os, re, sys, urllib.parse, urllib.request

BASE = "https://api.poly.pizza/v1.1"

def api(path):
    key = os.environ.get("POLYPIZZA_KEY")
    if not key:
        sys.exit("POLYPIZZA_KEY not set (source the repo .env)")
    req = urllib.request.Request(BASE + path, headers={"x-auth-token": key,
                                                        "User-Agent": "rogue-gauntlet-asset-fetch"})
    return json.load(urllib.request.urlopen(req, timeout=30))

def slug(s):
    return re.sub(r"[^a-z0-9]+", "-", s.lower()).strip("-") or "model"

def lic_fields(licence):
    if licence and licence.upper().startswith("CC0"):
        return ("CC0-1.0", False, "https://creativecommons.org/publicdomain/zero/1.0/")
    if licence and "BY" in licence.upper():
        return ("CC-BY-3.0", True, "https://creativecommons.org/licenses/by/3.0/")
    return (licence or "UNKNOWN", None, "")

def _opt(args, flag, default):
    return args[args.index(flag) + 1] if flag in args else default

def cmd_search(args):
    data = api(f"/search/{urllib.parse.quote(args[0])}?Limit={_opt(args, '--limit', '10')}")
    cc0, max_tris = "--cc0" in args, int(_opt(args, "--max-tris", "0"))
    print(f"{data.get('total','?')} total; showing up to {len(data['results'])}")
    print(f"{'ID':<14}{'Tris':>8}  {'Lic':<10}{'Anim':<5} Creator / Title")
    for m in data["results"]:
        if cc0 and not m.get("Licence", "").upper().startswith("CC0"):
            continue
        if max_tris and (m.get("Tri Count") or 0) > max_tris:
            continue
        print(f"{m['ID']:<14}{m.get('Tri Count', 0):>8}  {m.get('Licence', ''):<10}"
              f"{'yes' if m.get('Animated') else 'no':<5} {m['Creator']['Username']} / {m['Title']}")

def cmd_get(args):
    mid, dest = args[0], args[1]
    m = api(f"/model/{mid}")
    os.makedirs(dest, exist_ok=True)
    out = os.path.join(dest, slug(m["Title"]) + ".glb")
    with urllib.request.urlopen(urllib.request.Request(
            m["Download"], headers={"User-Agent": "rogue-gauntlet-asset-fetch"}), timeout=120) as r:
        open(out, "wb").write(r.read())
    spdx, attr_req, lic_url = lic_fields(m.get("Licence"))
    print(f"downloaded: {out} ({os.path.getsize(out)} bytes, {m.get('Tri Count')} tris, "
          f"animated={m.get('Animated')})")
    entry = {
        "id": slug(m["Title"]), "name": m["Title"], "dir": f"assets/{slug(m['Title'])}",
        "author": m["Creator"]["Username"], "source": f"https://poly.pizza/m/{mid}",
        "license": spdx, "license_url": lic_url, "attribution_required": attr_req,
        "attribution_text": m.get("Attribution") if attr_req else None,
        "types": ["3d"], "compatibility_group": "polypizza-unsorted",
        "notes": f"{m.get('Tri Count')} tris; animated={m.get('Animated')}. Verify style fit before use.",
    }
    print("\n--- suggested ATTRIBUTIONS.json entry ---")
    print(json.dumps(entry, indent=2))

if __name__ == "__main__":
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    {"search": cmd_search, "get": cmd_get}[sys.argv[1]](sys.argv[2:])
