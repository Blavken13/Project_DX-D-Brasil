"""Bind the animated Spinosaurus body to the Windows client's animal shader.

Run with Blender's bundled Python after building the Unity Windows bundles and
before install_spinosaurus_2214.ps1. Only the local Spinosaurus prefab bundle is
rewritten. The grayscale diffuse receives a natural color tint; the shader and
matcap remain in the PC client's existing preload.
"""

import copy
import gc
import hashlib
import json
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "pc-client/work/spino-python-deps"))
import UnityPy


CLIENT = ROOT / "Lost-Horizon-PC/Lost Horizon Game_Data/StreamingAssets/AssetBundles"
SOURCE = ROOT / "dino/Spinosaurus/AssetBundles/Windows"
PREFAB = SOURCE / "models$animals$spinosaurus$spinosaurusprefab.prefab.bundle"
BACKUP = ROOT / "dino/Spinosaurus/AssetBundles/Windows/spinosaurusprefab.before-native.bundle"
REPORT = ROOT / "dino/Spinosaurus/AssetBundles/Windows/native-material-validation.json"
EXPECTED = {"SpinosaurusBody": "Spino_colorized_D"}


def indexed_bundle(index, name):
    entry = next(item for item in index["FileList"] if item["Name"] == name)
    return CLIENT / (name[:-7] + "." + entry["Crc"] + ".bundle")


def main():
    index = json.loads((CLIENT / "Info.5.2.1.json").read_text(encoding="utf-8-sig"))
    native_path = indexed_bundle(index, "models$animals$trex$trexprefab.prefab.bundle")
    preload_path = CLIENT / ("preload." + index["PreloadCrc"] + ".bundle")
    native_env = UnityPy.load(str(native_path))
    native_obj = next(obj for obj in native_env.objects
                      if obj.type.name == "Material" and obj.peek_name() == "TRex-texture1")
    native = native_obj.read_typetree()
    preload = UnityPy.load(str(preload_path))
    shader = next(obj for obj in preload.objects if obj.type.name == "Shader"
                  and obj.read_typetree().get("m_ParsedForm", {}).get("m_Name")
                  == "LitSphere/DualMode/Animal")
    matcap = next(obj for obj in preload.objects if obj.type.name == "Texture2D"
                  and obj.peek_name() == "litsphere_dino_giant")
    assert native["m_Shader"]["m_PathID"] == shader.path_id

    if not BACKUP.exists():
        BACKUP.write_bytes(PREFAB.read_bytes())
    # Rebuilds replace PREFAB with the original Standard materials. Refuse to
    # silently layer another edit over an already finalized bundle.
    env = UnityPy.load(str(PREFAB))
    materials = {obj.peek_name(): obj for obj in env.objects if obj.type.name == "Material"
                 and obj.peek_name() in EXPECTED}
    assert set(materials) == set(EXPECTED), sorted(materials)
    assert all(int(native_obj.assets_file.target_platform) == int(obj.assets_file.target_platform)
               for obj in materials.values())
    original = {obj.path_id: obj.get_raw_data() for obj in env.objects
                if obj.path_id not in {mat.path_id for mat in materials.values()}}

    for name, obj in materials.items():
        tree = obj.read_typetree()
        if tree["m_Shader"]["m_FileID"] != 0 or not tree["m_Shader"]["m_PathID"]:
            raise RuntimeError(name + " has unexpected shader pointer " + str(tree["m_Shader"]))
        albedo = copy.deepcopy(dict(tree["m_SavedProperties"]["m_TexEnvs"])["_MainTex"])
        assert albedo["m_Texture"]["m_FileID"] == 0
        assert albedo["m_Texture"]["m_PathID"] != 0

        def remap(pointer):
            pointer = copy.deepcopy(pointer)
            if not pointer["m_PathID"]:
                return pointer
            if pointer["m_FileID"] == 0:
                return {"m_FileID": 0, "m_PathID": 0}
            external = native_obj.assets_file.externals[pointer["m_FileID"] - 1]
            matching = [i for i, ref in enumerate(obj.assets_file.externals)
                        if ref.path == external.path]
            if not matching:
                obj.assets_file.externals.append(copy.copy(external))
                matching = [len(obj.assets_file.externals) - 1]
            pointer["m_FileID"] = matching[0] + 1
            return pointer

        tree["m_Shader"] = remap(native["m_Shader"])
        tree["m_SavedProperties"] = copy.deepcopy(native["m_SavedProperties"])
        tree["m_SavedProperties"].setdefault("m_Ints", [])
        for key, value in tree["m_SavedProperties"]["m_TexEnvs"]:
            value["m_Texture"] = remap(value["m_Texture"])
            if key == "_MainTex":
                value.update(albedo)
        floats = dict(tree["m_SavedProperties"]["m_Floats"])
        floats.update(_DontUseSubTex=1.0, _NoColorMap=1.0, _AnimalAging=0.0,
                      _AgingRatio=0.0, _Metalic=0.0, _NormalDepth=0.0)
        tree["m_SavedProperties"]["m_Floats"] = list(floats.items())
        colors = dict(tree["m_SavedProperties"]["m_Colors"])
        colors["_Color"] = {"r": 0.72, "g": 0.60, "b": 0.38, "a": 1.0}
        colors["_SubColor"] = {"r": 0.45, "g": 0.38, "b": 0.25, "a": 1.0}
        tree["m_SavedProperties"]["m_Colors"] = list(colors.items())
        tree["m_ValidKeywords"] = []
        tree["m_InvalidKeywords"] = []
        assert dict(tree["m_SavedProperties"]["m_TexEnvs"])["_LitSphereTex"]["m_Texture"]["m_PathID"] == matcap.path_id
        obj.save_typetree(tree)

    candidate = PREFAB.with_suffix(".native.tmp")
    candidate.write_bytes(env.file.save(packer="lz4"))
    checked = UnityPy.load(str(candidate))
    assert {obj.path_id: obj.get_raw_data() for obj in checked.objects
            if obj.path_id not in {mat.path_id for mat in materials.values()}} == original
    checked.load_file(str(preload_path))
    results = {}
    for name, expected_albedo in EXPECTED.items():
        material = next(obj.read() for obj in checked.objects
                        if obj.type.name == "Material" and obj.peek_name() == name)
        actual_shader = material.m_Shader.read().m_ParsedForm.m_Name
        textures = dict(material.m_SavedProperties.m_TexEnvs)
        actual_matcap = textures["_LitSphereTex"].m_Texture.read().m_Name
        actual_albedo = textures["_MainTex"].m_Texture.read().m_Name
        assert (actual_shader, actual_matcap, actual_albedo) == (
            "LitSphere/DualMode/Animal", "litsphere_dino_giant", expected_albedo)
        properties = material.m_SavedProperties
        color = dict(properties.m_Colors)["_Color"]
        assert tuple(round(getattr(color, channel), 2) for channel in ("r", "g", "b")) == (0.72, 0.60, 0.38)
        floats = dict(properties.m_Floats)
        assert floats["_NoColorMap"] == 1.0 and floats["_DontUseSubTex"] == 1.0
        results[name] = dict(shader=actual_shader, matcap=actual_matcap,
                             albedo=actual_albedo,
                             tint=dict(r=0.72, g=0.60, b=0.38),
                             bypass_color_mask=True)
    # UnityPy keeps the candidate bundle open while its object readers exist.
    # Release that handle before atomically replacing the built prefab bundle.
    del material, checked, env, preload, native_env
    gc.collect()
    candidate.replace(PREFAB)
    results["prefab_md5"] = hashlib.md5(PREFAB.read_bytes()).hexdigest()
    REPORT.write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(results, indent=2))


if __name__ == "__main__":
    main()
