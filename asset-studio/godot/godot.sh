#!/usr/bin/env bash
# Studio-owned launcher; the existing game project can host optional GPU reviews.
set -euo pipefail
if [[ -n "${GODOT_BIN:-}" ]]; then exec "$GODOT_BIN" "$@"; fi
for binary in godot godot4 /Applications/Godot_mono.app/Contents/MacOS/Godot /Applications/Godot.app/Contents/MacOS/Godot; do
 if command -v "$binary" >/dev/null 2>&1; then exec "$binary" "$@"; fi
done
echo "Set GODOT_BIN to an installed Godot executable." >&2
exit 1
