#!/usr/bin/env python3
"""Validate a combat JSONL capture and summarize outcomes without depending on console prose."""

import argparse
from collections import Counter
import json
from pathlib import Path


def analyze(path: Path, require_complete: bool = False) -> dict:
    events = Counter()
    outcomes = Counter()
    commands = Counter()
    damage = Counter()
    run = None
    sequence = 0
    frame = 0
    time = 0.0
    rejected = []
    interrupted = 0
    for number, line in enumerate(path.read_text().splitlines(), 1):
        if not line.strip():
            continue
        record = json.loads(line.removeprefix("COMBAT "))
        if record["schema"] != 1:
            raise ValueError(f"line {number}: unsupported trace schema")
        run = run or record["run"]
        if record["run"] != run or record["sequence"] != sequence + 1:
            raise ValueError(f"line {number}: mixed runs or missing/out-of-order records")
        if record["frame"] < frame or record["time"] < time:
            raise ValueError(f"line {number}: simulation clock moved backwards")
        sequence, frame, time = record["sequence"], record["frame"], record["time"]
        event, data = record["event"], record["data"]
        events[event] += 1
        if event == "damage.resolved":
            outcomes[data["outcome"]] += 1
            damage[record["actor"]] += data["damage"]
        elif event == "harness.command":
            commands[data["command"]] += 1
            if not data["accepted"]:
                rejected.append({"frame": frame, **data})
        elif event == "attack.ended" and data["interrupted"]:
            interrupted += 1
    if not events["run.started"]:
        raise ValueError("capture has no run.started record")
    if require_complete and events["run.ended"] != 1:
        raise ValueError("capture is incomplete (no unique run.ended record)")
    return {"run": run, "complete": events["run.ended"] == 1, "records": sequence,
            "frames": frame, "game_seconds": time, "events": dict(events),
            "commands": dict(commands), "rejected_commands": rejected,
            "outcomes": dict(outcomes), "damage_by_target": dict(damage),
            "interrupted_attacks": interrupted}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("trace", type=Path)
    parser.add_argument("--require-complete", action="store_true")
    args = parser.parse_args()
    try:
        print(json.dumps(analyze(args.trace, args.require_complete), indent=2, sort_keys=True))
    except (ValueError, KeyError, OSError) as error:
        parser.exit(1, f"Invalid combat trace: {error}\n")
