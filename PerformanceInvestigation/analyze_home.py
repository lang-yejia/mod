"""Count map objects inside the RimWorld home area (single-map saves).

Usage: python analyze_home.py input.rws
"""

import base64
import json
import re
import sys
import xml.etree.ElementTree as ET
import zlib
from collections import Counter
from pathlib import Path


def main(path: Path) -> None:
    root = ET.parse(path).getroot()
    map_node = root.find("./game/maps/li")
    if map_node is None:
        raise SystemExit("No map found")
    areas = map_node.findall("./areaManager/areas/li")
    home = next((a for a in areas if a.get("Class") == "Area_Home"), None)
    if home is None:
        raise SystemExit("No home area found")
    grid = home.find("innerGrid")
    width = int(grid.findtext("mapSizeX"))
    height = int(grid.findtext("mapSizeZ"))
    bits = zlib.decompress(base64.b64decode(grid.findtext("arrDeflate").strip()), -15)
    declared = int(grid.findtext("trueCount"))
    actual = sum(byte.bit_count() for byte in bits)
    if actual != declared:
        raise SystemExit(f"Home grid mismatch: {actual} != {declared}")

    inside, outside = Counter(), Counter()
    inside_filth, outside_filth = Counter(), Counter()
    positions = re.compile(r"\((\d+),\s*\d+,\s*(\d+)\)")
    for thing in map_node.find("things"):
        position = positions.fullmatch(thing.findtext("pos", ""))
        if not position:
            continue
        x, z = map(int, position.groups())
        if x >= width or z >= height:
            continue
        index = x + z * width
        is_home = bool(bits[index // 8] & (1 << (index % 8)))
        kind = thing.get("Class", "Thing")
        (inside if is_home else outside)[kind] += 1
        if kind == "Filth":
            (inside_filth if is_home else outside_filth)[thing.findtext("def", "unknown")] += 1

    print(json.dumps({
        "file": path.name,
        "home_cells": actual,
        "inside": dict(inside.most_common()),
        "outside": dict(outside.most_common()),
        "inside_filth": dict(inside_filth.most_common()),
        "outside_filth": dict(outside_filth.most_common()),
    }, ensure_ascii=False))


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    main(Path(sys.argv[1]))
