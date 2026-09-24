from pathlib import Path
import UnityPy
import json

FILE = Path(
    r".\DurangoV2_Data\globalgamemanagers"
)

env = UnityPy.load(str(FILE))

print("=== PLAYER SETTINGS / SPLASH ===")
print()

for obj in env.objects:

    if obj.type.name != "PlayerSettings":
        continue

    print(
        "PlayerSettings PathID:",
        obj.path_id
    )

    try:
        tree = obj.read_typetree()
    except Exception as e:
        print("Erro:", e)
        continue

    def walk(value, path="root"):

        if isinstance(value, dict):

            for k, v in value.items():

                low = str(k).lower()

                if (
                    "splash" in low
                    or "logo" in low
                ):
                    print(
                        path + "." + str(k),
                        "=",
                        v
                    )

                walk(
                    v,
                    path + "." + str(k)
                )

        elif isinstance(value, list):

            for i, v in enumerate(value):
                walk(
                    v,
                    f"{path}[{i}]"
                )

    walk(tree)
