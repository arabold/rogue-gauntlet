#!/usr/bin/env python3
"""
Fetch an open CC0 3D asset pack repository's glTF assets straight from GitHub.
No API key needed (public repos).

Repo layout: addons/<pack>/Assets/{gltf,fbx,obj}/... -- we take the gltf set
(.gltf + .bin + shared texture .png) plus LICENSE.txt, flattened into <dest>/ to match
the game's assets/ directories.

Stdlib only -- run with system python3.

Usage:
    python3 fetch_pack.py <owner/repo> <dest_dir> [--dry-run]

After fetching: add an ATTRIBUTIONS.json entry (CC0, source = the itch/GitHub URL)
and run the Godot import step. See assets/AGENTS.md.
"""
import json, os, sys, urllib.parse, urllib.request

API = "https://api.github.com"

def gh_json(url):
    req = urllib.request.Request(url, headers={"Accept": "application/vnd.github+json",
                                               "User-Agent": "rogue-gauntlet-asset-fetch"})
    tok = os.environ.get("GH_TOKEN")  # optional: raises the unauth rate limit
    if tok:
        req.add_header("Authorization", f"Bearer {tok}")
    return json.load(urllib.request.urlopen(req, timeout=30))

def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    dry = "--dry-run" in sys.argv
    if len(args) < 2:
        sys.exit("usage: fetch_pack.py <owner/repo> <dest_dir> [--dry-run]")
    repo, dest = args[0], args[1]
    branch = gh_json(f"{API}/repos/{repo}")["default_branch"]
    tree = gh_json(f"{API}/repos/{repo}/git/trees/{branch}?recursive=1")["tree"]

    want = [n["path"] for n in tree if n["type"] == "blob"
            and ("/Assets/gltf/" in n["path"] or n["path"] in ("LICENSE.txt", "README.md"))]
    print(f"{repo}@{branch}: {len(want)} files to fetch -> {dest}")
    if dry:
        for p in want[:12]:
            print("  ", p)
        print(f"  ... ({len(want)} total)")
        return

    os.makedirs(dest, exist_ok=True)
    for p in want:
        raw = f"https://raw.githubusercontent.com/{repo}/{branch}/{urllib.parse.quote(p)}"
        with urllib.request.urlopen(urllib.request.Request(
                raw, headers={"User-Agent": "rogue-gauntlet-asset-fetch"}), timeout=60) as r:
            data = r.read()
        with open(os.path.join(dest, os.path.basename(p)), "wb") as f:  # flatten
            f.write(data)
    print(f"downloaded {len(want)} files into {dest}")

if __name__ == "__main__":
    main()
