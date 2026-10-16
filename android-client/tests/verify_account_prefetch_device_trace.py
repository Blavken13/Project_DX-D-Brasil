"""Prove ordering and byte-identical installed APK from an actual device trace."""
import argparse, hashlib, json
from pathlib import Path

def verify(folder, apk):
    session=json.loads((folder/'session.json').read_text('utf-8'))
    events=[json.loads(line) for line in (folder/'java-events.jsonl').read_text('utf-8').splitlines() if line.strip()]
    digest=hashlib.sha256(apk.read_bytes()).hexdigest()
    if session['version_code']!=50224 or session['installed_apk_sha256']!=digest:
        raise AssertionError('Trace does not describe the final APK')
    expected=['ACCOUNT_PREFETCH_START','ACCOUNT_PREFETCH_HTTP_200', 'ACCOUNT_PREFETCH_VALIDATED_',
              'LOGIN_VIDEO_RELEASING','GAME_PREPARING','ACCOUNT_CACHE_BRIDGE_READY','UNITY_STARTING','ACCOUNT_PREFETCH_CONSUMED']
    timeline=[]
    for name in expected:
        found=[e for e in events if e['event'].startswith(name)]
        if len(found)!=1: raise AssertionError('Expected exactly one stage: '+name)
        event=found[0]
        timeline.append(dict(event=event['event'],utc_ms=event['utc_ms'],mono_ns=event['mono_ns'],seq=event['seq']))
    if any(a['seq']>=b['seq'] or a['mono_ns']>b['mono_ns'] for a,b in zip(timeline,timeline[1:])):
        raise AssertionError('Account was not validated before Unity startup')
    failures=[e for e in events if 'PREFETCH_FAILED' in e['event'] or 'BRIDGE_FAILED' in e['event'] or e['event']=='JAVA_FAILURE']
    if failures: raise AssertionError('Account flow recorded a failure')
    native=[json.loads(line) for line in (folder/'native-events.jsonl').read_text('utf-8').splitlines() if line.strip()]
    return dict(version_code=session['version_code'],apk_sha256=digest,device_model=session['model'],
                android_api=session['android_api'],page_size=session['page_size'],pid=session['pid'],
                session_id=session['session_id'],characters_validated=int(timeline[2]['event'].rsplit('_',1)[1]),
                ordering_verified=True,prefetch_consumed=True,
                prefetch_duration_ms=round((timeline[2]['mono_ns']-timeline[0]['mono_ns'])/1e6,3),
                unity_to_cache_consumed_ms=round((timeline[-1]['mono_ns']-timeline[-2]['mono_ns'])/1e6,3),
                java_event_count=len(events),native_event_count=len(native),timeline=timeline,
                limitations=['USB device is not the Poco F7; this does not establish the Poco crash cause.',
                             'Integration used Continue with an existing session; fresh password login was not separately exercised.'])

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('folder',type=Path);parser.add_argument('apk',type=Path)
    parser.add_argument('--output',type=Path,required=True);args=parser.parse_args()
    result=verify(args.folder,args.apk)
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(result,indent=2)+'\n','utf-8')
    print(json.dumps(result,indent=2))
