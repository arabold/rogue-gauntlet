#!/usr/bin/env bash
# Record a scene with Godot's Movie Maker (windowed: headless renders blank) and stitch the frames into a looping GIF.
# Movie Maker forces a fixed frame rate, so tweens and animations play at their true timing however slow the render.
#
# Usage (from the project/worktree root):
#   .agents/skills/godot-mcp/scripts/record_gif.sh <res://scene.tscn> <out.gif> [seconds=2] [fps=30] [resolution=960x640] [-- frames_to_gif.py args]
# Example:
#   .agents/skills/godot-mcp/scripts/record_gif.sh res://tmp_preview.tscn "$SCRATCH/preview.gif" 3 30 960x640 -- --skip 5 --width 480
set -euo pipefail

scene=$1
out=$2
seconds=${3:-2}
fps=${4:-30}
resolution=${5:-960x640}
shift $(( $# < 5 ? $# : 5 ))
[[ ${1:-} == "--" ]] && shift

here=$(cd "$(dirname "$0")" && pwd)
frames_dir=$(mktemp -d "${TMPDIR:-/tmp}/godot_gif.XXXXXX")
trap 'rm -rf "$frames_dir"' EXIT

frame_count=$(awk "BEGIN { print int($seconds * $fps) }")
"$here/godot.sh" --path "$PWD" "$scene" --resolution "$resolution" --fixed-fps "$fps" \
	--write-movie "$frames_dir/frame.png" --quit-after "$frame_count"
python3 -I "$here/frames_to_gif.py" "$frames_dir" "$out" --fps "$fps" "$@"
