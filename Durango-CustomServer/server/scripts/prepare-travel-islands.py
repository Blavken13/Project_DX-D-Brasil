"""Create missing alpha sailing terrains from installed maps of the same biome."""
import hashlib
import json
from pathlib import Path
import zipfile

root = Path(__file__).resolve().parents[3]
data = root / 'Durango-CustomServer/server/data'
templates = json.loads((data / 'assets/region_templates.json').read_text('utf-8'))
variants = [
    ('ri20te_alpha', 'ri20te190710', 'ri35te'),
    ('ri40tu_alpha', 'ri40tu171228', 'ri30td01'),
    ('ri45sw_alpha', 'ri45sw171228', 'ra60sw'),
    ('ri55sw_alpha', 'ri55sw171228', 'ra60sw'),
    ('ua60tu_alpha', 'ua60tu02Main01', 'ri55tu'),
    ('ri50de_alpha', 'ri50de171228', 'ri35de'),
    ('ri55tr_alpha', 'ri55tb171228', 'ri40tr'),
    ('ua60tr_alpha', 'ua60tr02Main01', 'ri40tr'),
    ('ua60sn_alpha', 'ua60sn02Main04_elite', 'ri50sn'),
    ('ua60de_alpha', 'ua60de02Main02_elite', 'ri35de'),
    ('op60tr_alpha', 'op60tr180220', 'ri40tr'),
    ('op60te_alpha', 'op60te180220', 'ri35te'),
]
records = []
for name, template, source in variants:
    target = data / 'terrains' / (name + '.zip')
    with zipfile.ZipFile(data / 'terrains' / (source + '.zip')) as original:
        info = json.loads(original.read('info.yml'))
        src_template = templates[info['region_template']]
        assert list(src_template['biome_effects'])[0] == list(templates[template]['biome_effects'])[0]
        info['region_template'] = template
        with zipfile.ZipFile(target, 'w', compression=zipfile.ZIP_DEFLATED) as output:
            for entry in original.infolist():
                payload = json.dumps(info, ensure_ascii=False, indent=2).encode('utf-8') if entry.filename == 'info.yml' else original.read(entry)
                output.writestr(entry, payload)
    records.append({'terrain': name, 'template': template, 'level': templates[template]['level'],
                    'base_terrain': source, 'sha256': hashlib.sha256(target.read_bytes()).hexdigest()})
(data / 'terrains/alpha-variants.json').write_text(json.dumps(records, indent=2) + '\n', encoding='utf-8')
print(json.dumps(records, indent=2))
