"""Change only the serialized MTRendering flag; do not rewrite Unity assets."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent / 'work/python-deps'))
import UnityPy

SETTINGS = Path('assets/bin/Data/globalgamemanagers')

def patch(original):
    env=UnityPy.load(original)
    player=next(o for o in env.objects if o.type.name=='PlayerSettings')
    tree=player.read_typetree()
    assert tree['m_MTRendering'] is True and tree['mobileMTRenderingBaked'] is False
    before=player.get_raw_data();tree['m_MTRendering']=False
    after=player.save_typetree(tree)
    assert len(before)==len(after)
    differences=[i for i,(a,b) in enumerate(zip(before,after)) if a!=b]
    assert differences==[256] and before[256]==1 and after[256]==0
    offset=player.byte_start+256
    assert original[player.byte_start:player.byte_start+len(before)]==before
    result=bytearray(original);result[offset]=0
    changed=next(o for o in UnityPy.load(bytes(result)).objects if o.type.name=='PlayerSettings').read_typetree()
    assert changed==tree
    return bytes(result),offset

def apply(source,client):
    original=(source/SETTINGS).read_bytes();result,_=patch(original)
    (client/SETTINGS).write_bytes(result)

def verify(source,client):
    expected,offset=patch((source/SETTINGS).read_bytes())
    assert (client/SETTINGS).read_bytes()==expected
    return {'changed_bytes':1,'offset':offset,'m_MTRendering':False,'mobileMTRenderingBaked':False,
            'other_settings_shaders_assets_preserved':True}
