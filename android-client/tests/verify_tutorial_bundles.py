"""Validate the embedded tutorial dependencies against both consumers and the server catalog."""
import hashlib,json,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'android-client/work/python-deps'))
import UnityPy
P=ROOT/'Durango-CustomServer/server/assetbundles/android'
manifest=json.loads((ROOT/'android-client/bundled/tutorial/manifest.json').read_text())['bundles']
files={f['Name']:f for f in json.loads((P/'Info.5.2.1.json').read_text())['FileList']}
expected={'models$npc$materials$npc_k_body.mat.bundle':('2ed55120e9f3eda87f716d4b4791deef',8637032030502495052),'models$npc$materials$npc_k_hair2.mat.bundle':('5f33da317a5771df4797b60613269732',2309374952275864763),'models$npc$materials$npc_k_skin2.mat.bundle':('69d0b028fcb85e237d08c3116cfb87ec',-3085039788844458069),'particle$fx_materials$durango_common$fx_common_glow_05.mat.bundle':('774a6a714c22fa4984d65d5eb14f3d20',-8111886238630369642)}
assert set(x['name'] for x in manifest)==set(expected)
refs=set();textures=0
for entry in manifest:
 path=ROOT/'android-client/bundled/tutorial'/entry['name'];data=path.read_bytes()
 assert len(data)==entry['bytes'] and hashlib.sha256(data).hexdigest()==entry['sha256']
 assert (P/entry['server_filename']).read_bytes()==data
 f=files[entry['name']];assert (f['Crc'],f['Hash'],f['Size'])==(entry['crc'],entry['cache_hash'],entry['bytes'])
 env=UnityPy.load(data);cab,pathid=expected[entry['name']];materials=[]
 for obj in env.objects:
  assert int(obj.assets_file.target_platform)==13
  assert obj.assets_file.unity_version in ('2017.4.7f1','2017.4.34f1')
  if obj.type.name=='Material':
   materials.append((obj.assets_file.name.lower(),obj.path_id));obj.read_typetree()
  if obj.type.name=='Texture2D':
   t=obj.read();assert t.image.size==(t.m_Width,t.m_Height);textures+=1
 assert ('cab-'+cab,pathid) in materials
 refs.add(('cab-'+cab,pathid))
for name in ['models$npc$npc_kbikeprefab.prefab.bundle','models$ancora$animals$dog.bundle']:
 f=files[name];env=UnityPy.load(str(P/(name[:-7]+'.'+f['Crc']+'.bundle')));seen=set()
 for o in env.objects:
  tree=o.read_typetree()
  def walk(v):
   if isinstance(v,dict):
    if 'm_FileID' in v and 'm_PathID' in v:
     idx=v['m_FileID'];pid=v['m_PathID']
     if idx>0 and idx<=len(o.assets_file.externals):
      seen.add((o.assets_file.externals[idx-1].path.rsplit('/',1)[-1].lower(),pid))
    else:
     for child in v.values():walk(child)
   elif isinstance(v,list):
    for child in v:walk(child)
  walk(tree)
 needed={expected[x] for x in f['Dependencies'] if x in expected}
 assert {('cab-'+cab,pid) for cab,pid in needed}<=seen
 assert all((P/(dep[:-7]+'.'+files[dep]['Crc']+'.bundle')).is_file() for dep in f['Dependencies'])
print('PASS: four Android dependencies, material CAB/PathIDs, consumer references, server hashes and',textures,'decoded textures')
