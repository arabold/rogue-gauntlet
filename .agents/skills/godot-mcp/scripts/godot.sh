#!/usr/bin/env bash
# Locate the working Godot 4 (mono/C#) binary on this machine and run it with the
# given arguments. Avoids hardcoding the binary path in every command and handles
# the fact that this machine has Godot_mono.app, not the default Godot.app.
#
# Usage:
#   .agents/skills/godot-mcp/scripts/godot.sh --headless --path "$PWD" --import
#   .agents/skills/godot-mcp/scripts/godot.sh --headless --path "$PWD" res://scenes/main/main.tscn --quit-after 150
set -euo pipefail

candidates=(
	/Applications/Godot_mono.app/Contents/MacOS/Godot
	/Applications/Godot.app/Contents/MacOS/Godot
)

for bin in "${candidates[@]}"; do
	if [[ -x "$bin" ]]; then
		exec "$bin" "$@"
	fi
done

echo "godot.sh: no Godot binary found in: ${candidates[*]}" >&2
exit 1
