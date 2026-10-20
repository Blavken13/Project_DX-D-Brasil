"""Synchronize the generated Spino footprint with the server's client-facing data."""
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
source = ROOT / 'dino/Spinosaurus/Converted/ActorXRepair/combat-shape.json'
shape = json.loads(source.read_text('utf-8-sig'))
if len(shape['circles']) != 24 or shape['selection_colliders'] != 24:
    raise RuntimeError('Rebuild the interactable Spino bundle first')
assets = ROOT / 'Durango-CustomServer/server/data/assets'
definitions = assets / 'entity_types/animal.json'
text = definitions.read_text('utf-8-sig')
entry = re.search(r'(?ms)^  "2214": \{.*?(?=^  "[0-9]+":|\Z)', text)
if entry is None:
    raise RuntimeError('Spinosaurus 2214 is missing')
radius = shape['client_bound_radius']
if not 40 < radius < 60:
    raise RuntimeError('Unexpected Spino body radius')
updated, count = re.subn(r'("bound_radius":\s*)[0-9.]+', lambda m: m[1] + str(radius), entry[0])
if count != 1:
    raise RuntimeError('Unexpected bound_radius schema')
definitions.write_text(text[:entry.start()] + updated + text[entry.end():], encoding='utf-8', newline='\n')
footprints = assets / 'animal_body_shapes.json'
shapes = json.loads(footprints.read_text('utf-8-sig')) if footprints.exists() else {}
shapes['2214'] = {k: shape[k] for k in ('attack_padding', 'circles')}
footprints.write_text(json.dumps(shapes, indent=2) + '\n', encoding='utf-8')
print(f'Spino client bound_radius={radius:.6f}; runtime radius={radius * 4:.3f}; '
      f'{len(shape["circles"])} body sections; attack padding={shape["attack_padding"]}')
