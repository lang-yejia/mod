"""Make two disposable save copies for an A/B test; never edits the source.

Usage: python make_filth_pair.py input.rws baseline.rws no_filth.rws
The two output paths must not exist. Keep them outside any published repository.
"""

import shutil
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def main() -> None:
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    source, baseline, cleaned = map(Path, sys.argv[1:])
    if not source.is_file():
        raise SystemExit(f"Missing source: {source}")
    for output in (baseline, cleaned):
        if output.exists():
            raise SystemExit(f"Refusing to overwrite: {output}")
        if output.resolve() == source.resolve():
            raise SystemExit("Output cannot be source")

    tree = ET.parse(source)
    removed = 0
    for things in tree.findall("./game/maps/li/things"):
        for thing in list(things):
            if thing.get("Class") == "Filth":
                things.remove(thing)
                removed += 1
    if removed == 0:
        raise SystemExit("No map filth found; no files created")

    shutil.copy2(source, baseline)
    with cleaned.open("xb") as out:
        tree.write(out, encoding="utf-8", xml_declaration=True)
    print(f"Created {baseline.name} and {cleaned.name}; removed {removed} filth objects")


if __name__ == "__main__":
    main()
