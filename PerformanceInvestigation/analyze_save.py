"""Summarize spawned map objects in RimWorld XML saves without changing them.

Usage: python analyze_save.py save1.rws [save2.rws ...]
"""

import json
import sys
from collections import Counter
from pathlib import Path
from xml.etree.ElementTree import iterparse


def analyze(path: Path) -> dict:
    classes = Counter()
    filth = Counter()
    pawns = Counter()
    mods = 0
    version = None
    maps = 0
    stack = []

    for event, elem in iterparse(path, events=("start", "end")):
        if event == "start":
            stack.append(elem.tag)
            if stack[-3:] == ["game", "maps", "li"]:
                maps += 1
            continue

        if stack == ["savegame", "meta", "gameVersion"]:
            version = elem.text
        elif stack[-3:] == ["meta", "modIds", "li"]:
            mods += 1
        elif stack[-4:-1] == ["maps", "li", "things"] and elem.tag == "thing":
            kind = elem.get("Class", "Thing")
            classes[kind] += 1
            definition = elem.findtext("def", "unknown")
            if kind == "Filth":
                filth[definition] += 1
            elif kind == "Pawn":
                pawns[definition] += 1
            elem.clear()
        stack.pop()

    return {
        "file": path.name,
        "bytes": path.stat().st_size,
        "version": version,
        "mod_count": mods,
        "maps": maps,
        "spawned_total": sum(classes.values()),
        "classes": dict(classes.most_common()),
        "filth": dict(filth.most_common()),
        "pawns": dict(pawns.most_common()),
    }


if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    for file in sys.argv[1:]:
        print(json.dumps(analyze(Path(file)), ensure_ascii=False))
