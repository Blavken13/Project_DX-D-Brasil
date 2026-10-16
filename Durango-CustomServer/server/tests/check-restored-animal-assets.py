"""Verify that restored encounters have original Android model bundles available."""
import json
from pathlib import Path

SERVER = Path(__file__).resolve().parents[1]
DATA = SERVER / "data"


def main():
    animals = json.loads((DATA / "assets/entity_types/animal.json").read_text("utf-8-sig"))
    encounters = json.loads((DATA / "restored-animal-encounters.json").read_text("utf-8"))
    catalog = json.loads((SERVER / "assetbundles/android/Info.5.2.1.json").read_text("utf-8-sig"))
    active = {entry["Name"].lower(): entry for entry in catalog["FileList"]}
    types = {str(entry["entity_type"]) for entries in encounters.values() for entry in entries} | {"2004"}
    for entity_type in sorted(types):
        animal = animals[entity_type]
        prefix = "models$animals$" + animal["model_path"].lower().replace("/", "$") + ".prefab."
        record = active.get(prefix + "bundle")
        if record is None:
            raise RuntimeError(f"{entity_type}: model missing from Android catalog: {prefix}")
        path = SERVER / "assetbundles/android" / (prefix + record["Hash"] + ".bundle")
        if not path.is_file() or path.stat().st_size != record["Size"]:
            raise RuntimeError(f"{entity_type}: active Android model missing or wrong size: {path}")
        with path.open("rb") as stream:
            if not stream.read(8).startswith(b"UnityFS"):
                raise RuntimeError(f"{entity_type}: invalid UnityFS header in {path}")
        print("OK", entity_type, animal["__name__"], path.name)
    print("PASS", len(types), "native Android models")


if __name__ == "__main__":
    main()
