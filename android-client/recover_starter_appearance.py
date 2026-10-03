"""Recover the original Android starter outfit and its exact catalog dependencies."""
import hashlib, json, shutil, sys, urllib.request
from pathlib import Path
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT/'work/python-deps'))
import UnityPy

def main():
    server=ROOT.parent/'Durango-CustomServer/server/assetbundles/android'
    index=json.loads((server/'Info.5.2.1.json').read_text('utf-8-sig'))
    catalog={x['Name']:x for x in index['FileList']}
    roots=['models$pc$male$body$m_body_beginner_leaf.fbx.bundle']
    closure=set();pending=list(roots)
    while pending:
        name=pending.pop()
        if name in closure:continue
        closure.add(name);pending.extend(catalog[name]['Dependencies'])
    recovery=ROOT/'recovered/upstream-android';inventory=json.loads((recovery/'inventory.json').read_text('utf-8'))
    known={x['server_filename']:x for x in inventory['bundles']};added=[]
    for name in sorted(closure):
        entry=catalog[name];filename=name[:-7]+'.'+entry['Crc']+'.bundle';path=server/filename
        if path.is_file():continue
        url='http://api.durangonewdawn.com:8190/assetbundles/android/'+filename
        with urllib.request.urlopen(url,timeout=30) as response:data=response.read(32*1024*1024+1)
        assert len(data)<=32*1024*1024 and data.startswith(b'UnityFS\0')
        env=UnityPy.load(data)
        assert len(list(env.objects))>0
        for obj in env.objects:
            assert obj.assets_file.unity_version=='2017.4.34f1' and int(obj.assets_file.target_platform)==13
            if obj.type.name=='Texture2D':
                texture=obj.read();assert texture.image.size==(texture.m_Width,texture.m_Height)
        record={'server_filename':filename,'catalog_entry':entry,'bytes':len(data),'sha256':hashlib.sha256(data).hexdigest(),
                'download_url':url,'unity_version':'2017.4.34f1','validation':'Android starter appearance; exact original catalog and dependencies; validated Unity 2017 Android objects'}
        assert filename not in known or known[filename]==record
        (recovery/'bundles-android').mkdir(exist_ok=True)
        (recovery/'bundles-android'/filename).write_bytes(data);path.write_bytes(data)
        if filename not in known:inventory['bundles'].append(record)
        added.append(name);print('RECOVERED',filename,len(data))
    inventory['starter_male_dependencies']={'roots':roots,'bundles':sorted(closure),'recovered_names':added or inventory.get('starter_male_dependencies',{}).get('recovered_names',[])}
    (recovery/'inventory.json').write_text(json.dumps(inventory,indent=2,ensure_ascii=False)+'\n','utf-8')
    print('STARTER_DEPENDENCIES',len(closure))

if __name__=='__main__':main()
