"""Verify that restored encounters have original Android model bundles available."""
import json
from pathlib import Path

SERVER = Path(__file__).resolve().parents[1]
DATA = SERVER / "data"


def main():
    animals = json.loads((DATA / "assets/entity_types/animal.json").read_text("utf-8-sig"))
    encounters = json.loads((DATA / "restored-animal-encounters.json").read_text("utf-8"))
    bundles = list((SERVER / "assetbundles/android").glob("*.bundle"))
    types = {str(entry["entity_type"]) for entries in encounters.values() for entry in entries} | {"2004"}
    for entity_type in sorted(types):
        animal = animals[entity_type]
        prefix = "models$animals$" + animal["model_path"].lower().replace("/", "$") + ".prefab."
        matches = [path for path in bundles if path.name.lower().startswith(prefix)]
        if len(matches) != 1:
            raise RuntimeError(f"{entity_type}: expected one Android model for {prefix}, found {matches}")
        with matches[0].open("rb") as stream:
            if not stream.read(8).startswith(b"UnityFS"):
                raise RuntimeError(f"{entity_type}: invalid UnityFS header in {matches[0]}")
        print("OK", entity_type, animal["__name__"], matches[0].name)
    print("PASS", len(types), "native Android models")


if __name__ == "__main__":
    main()
