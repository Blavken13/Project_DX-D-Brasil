"""Finalize Giga bundles with each client's own preloaded LitSphere shader/matcap.

Uses existing compiled shaders, so packaging does not require an editor license.
Run after the Unity authoring build and before install-assets.py.
"""
from pathlib import Path
import copy, hashlib, json, re, sys

PACKAGE = Path(__file__).resolve().parent
ROOT = PACKAGE.parents[1]
sys.path.insert(0, str(ROOT/'tools/giga-video-analysis/python-deps'))
import UnityPy

def read_json(path): return json.loads(path.read_text(encoding='utf-8-sig'))
def write_json(path, data): path.write_text(json.dumps(data, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
def file_for(folder, entry): return folder/(entry['Name'][:-7]+'.'+entry['Crc']+'.bundle')

def update_motion_fields(path, updates, source=None):
    """Preserve the hand-maintained formatting and all unrelated animal fields."""
    raw=source if source is not None else path.read_bytes().decode('utf-8-sig')
    newline='\r\n' if '\r\n' in raw else '\n'
    def patch(match):
        fields=updates.get(match[2])
        if not fields: return match[0]
        body=match[3]
        for key,value in fields.items():
            pattern=r'(^    "'+re.escape(key)+r'": )[^,\r\n]+'
            if re.search(pattern,body,re.M): body=re.sub(pattern,lambda m:m[1]+json.dumps(value),body,flags=re.M)
            else: body=body.rstrip('\r\n')+','+newline+'    "'+key+'": '+json.dumps(value)+newline
        return match[1]+body+match[4]
    raw=re.sub(r'(^  "(\d+)": \{\r?\n)(.*?)(^  \})',patch,raw,flags=re.M|re.S)
    path.write_bytes(raw.encode('utf-8'))

def finalize():
    report=[]
    for platform, client in (
        ('Windows',ROOT/'Lost-Horizon-PC/Lost Horizon Game_Data/StreamingAssets/AssetBundles'),
        ('Android',ROOT/'Durango-CustomServer/server/assetbundles/android')):
        index=read_json(client/'Info.5.2.1.json')
        reference=next(e for e in index['FileList'] if e['Name']=='models$animals$trex$trexprefab.prefab.bundle')
        native_env=UnityPy.load(str(file_for(client,reference)))
        native_obj=next(o for o in native_env.objects if o.type.name=='Material' and o.peek_name()=='TRex-texture1')
        native=native_obj.read_typetree()
        preload_path=client/('preload.'+index['PreloadCrc']+'.bundle')
        preload=UnityPy.load(str(preload_path))
        matcap=next(o for o in preload.objects if o.type.name=='Texture2D' and o.peek_name()=='litsphere_dino_giant')
        if platform=='Windows':
            textures=PACKAGE/'UnityProject/Assets/Models/Animals/Giganotosaurus/Textures'
            matcap.read().image.save(textures/'LitSphere_Dino_Giant.png')
            meta=(textures/'Giganotosaurus_Albedo.png.meta').read_text()
            meta=meta.replace('8aea43ae6fb60ee4faefc92c8dfca226','9cfbda3f71dd4a6cbfb7ff212213bac1')
            (textures/'LitSphere_Dino_Giant.png.meta').write_text(meta)
        shader=next(o for o in preload.objects if o.type.name=='Shader' and o.read_typetree().get('m_ParsedForm',{}).get('m_Name')=='LitSphere/DualMode/Animal')
        assert native['m_Shader']['m_PathID']==shader.path_id

        built=PACKAGE/'Build'/platform
        prefab=built/'models$animals$giganotosaurus$giganotosaurusprefab.prefab.bundle'
        env=UnityPy.load(str(prefab)); bundle=env.file
        obj=next(o for o in env.objects if o.type.name=='Material' and o.peek_name()=='Giganotosaurus_Material')
        tree=obj.read_typetree(); before=copy.deepcopy(tree)
        untouched={o.path_id:o.get_raw_data() for o in env.objects if o.path_id!=obj.path_id}
        external_changed=False
        assert int(shader.assets_file.target_platform)==int(obj.assets_file.target_platform)
        # Reuse the previous native shader's external slot when the APK preload
        # changes, avoiding an unused reference to an obsolete preload CAB.
        previous_shader=tree['m_Shader']
        if previous_shader['m_FileID']>0 and previous_shader['m_PathID']==native['m_Shader']['m_PathID']:
            replacement=native_obj.assets_file.externals[native['m_Shader']['m_FileID']-1]
            external_changed=obj.assets_file.externals[previous_shader['m_FileID']-1].path!=replacement.path
            obj.assets_file.externals[previous_shader['m_FileID']-1]=copy.copy(replacement)
        albedo=copy.deepcopy(dict(tree['m_SavedProperties']['m_TexEnvs'])['_MainTex'])
        assert albedo['m_Texture']['m_FileID']==0

        def remap(pointer):
            pointer=copy.deepcopy(pointer)
            if not pointer['m_PathID']: return pointer
            if pointer['m_FileID']==0:
                return {'m_FileID':0,'m_PathID':0}
            external=native_obj.assets_file.externals[pointer['m_FileID']-1]
            matches=[i for i,e in enumerate(obj.assets_file.externals) if e.path==external.path]
            if not matches:
                obj.assets_file.externals.append(copy.copy(external))
                matches=[len(obj.assets_file.externals)-1]
            pointer['m_FileID']=matches[0]+1
            return pointer

        tree['m_Shader']=remap(native['m_Shader'])
        tree['m_SavedProperties']=copy.deepcopy(native['m_SavedProperties'])
        tree['m_SavedProperties'].setdefault('m_Ints', [])
        for name,tex in tree['m_SavedProperties']['m_TexEnvs']:
            tex['m_Texture']=remap(tex['m_Texture'])
            if name=='_MainTex': tex.update(albedo)
        floats=dict(tree['m_SavedProperties']['m_Floats'])
        floats.update(_DontUseSubTex=1.,_NoColorMap=0.,_AnimalAging=0.,_AgingRatio=0.,_Metalic=0.,_NormalDepth=0.)
        tree['m_SavedProperties']['m_Floats']=list(floats.items())
        colors=dict(tree['m_SavedProperties']['m_Colors'])
        colors['_Color']={'r':1.,'g':1.,'b':1.,'a':1.}
        colors['_SubColor']={'r':.5,'g':.5,'b':.5,'a':.5}
        tree['m_SavedProperties']['m_Colors']=list(colors.items())
        tree['m_ValidKeywords']=[]
        tree['m_InvalidKeywords']=[]
        tex=dict(tree['m_SavedProperties']['m_TexEnvs'])
        assert tex['_LitSphereTex']['m_Texture']['m_PathID']==matcap.path_id
        if tree!=before or external_changed:
            backup=PACKAGE/'Work/pre-native-materials'/platform
            backup.mkdir(parents=True,exist_ok=True)
            if not (backup/prefab.name).exists(): (backup/prefab.name).write_bytes(prefab.read_bytes())
            obj.save_typetree(tree)
            prefab.write_bytes(bundle.save(packer='lz4'))

        # Giga's measured planted-foot velocity is 3.20 m/s at entity scale 1.
        fwpath=built/'models$animals$giganotosaurus$giganotosaurus_framework.asset.bundle'
        fwenv=UnityPy.load(str(fwpath))
        fwobj=next(o for o in fwenv.objects if o.type.name=='MonoBehaviour' and o.peek_name()=='Giganotosaurus_framework')
        fw=fwobj.read_typetree(); fwbefore=copy.deepcopy(fw)
        for group in fw['move_sets']:
            for move_set in group['elems']:
                for motion in move_set['motions']:
                    if motion['motion']=='Giganotosaurus-Move-Fwd': motion['base_move_speed']=320.
        if fw!=fwbefore:
            backup=PACKAGE/'Work/pre-native-materials'/platform
            backup.mkdir(parents=True,exist_ok=True)
            if not (backup/fwpath.name).exists(): (backup/fwpath.name).write_bytes(fwpath.read_bytes())
            fwobj.save_typetree(fw)
            fwpath.write_bytes(fwenv.file.save(packer='lz4'))

        manifest=read_json(built/'build-manifest.json')
        for entry in manifest['bundles']:
            entry['hash']=hashlib.md5((built/entry['name']).read_bytes()).hexdigest()
        write_json(built/'build-manifest.json',manifest)
        # Resolve the saved material against the real preload, not just names.
        checked=UnityPy.load(str(prefab))
        assert {o.path_id:o.get_raw_data() for o in checked.objects if o.path_id!=obj.path_id}==untouched
        checked.load_file(str(preload_path))
        material=next(o.read() for o in checked.objects if o.type.name=='Material' and o.peek_name()=='Giganotosaurus_Material')
        actual_shader=material.m_Shader.read().m_ParsedForm.m_Name
        actual_tex=dict(material.m_SavedProperties.m_TexEnvs)['_LitSphereTex'].m_Texture.read().m_Name
        assert actual_shader=='LitSphere/DualMode/Animal' and actual_tex=='litsphere_dino_giant'
        report.append(dict(platform=platform,shader=actual_shader,matcap=actual_tex,albedo='giga_d_colorized.png',
                           prefab_md5=hashlib.md5(prefab.read_bytes()).hexdigest(),native_preload=index['PreloadCrc']))
        print(platform,actual_shader,actual_tex,flush=True)

    source=PACKAGE/'UnityProject/Assets/Editor/framework-data.json'
    fw=read_json(source)
    for group in fw['move_sets']:
        for move_set in group['elems']:
            for motion in move_set['motions']:
                if motion['motion']=='Giganotosaurus-Move-Fwd': motion['base_move_speed']=320.
    write_json(source,fw)
    motions_path=ROOT/'Durango-CustomServer/server/data/assets/derived/animal_motions.json'
    motions=read_json(motions_path)
    motions['2213'].update(move_speed=320.,move_playback_rate=1.,attack_duration=1.25,attack_impact=.65)
    update_motion_fields(motions_path,{'2213':{k:motions['2213'][k] for k in ('move_speed','move_playback_rate','attack_duration','attack_impact')}})
    write_json(PACKAGE/'Server/derived/animal_motions.2213.json',{'2213':motions['2213']})
    animals=read_json(ROOT/'Durango-CustomServer/server/data/assets/entity_types/animal.json')
    write_json(PACKAGE/'Server/entity_types/animal.2213.json',{'2213':animals['2213']})
    write_json(PACKAGE/'Metadata/native-materials.json',report)
    return report

if __name__=='__main__': finalize()
