"""Focused USB validation; preserves installed applications and their data."""
import argparse,json,os,subprocess,time,zipfile,sys
sys.stdout.reconfigure(encoding='utf-8')
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
WORK=ROOT/'android-client/work/app-tester-001'
ADB='C:/Users/Ander/AppData/Local/Android/Sdk/platform-tools/adb.exe'
SERIAL='725b9a01'
def adb(*args,check=True):
    r=subprocess.run([ADB,'-s',SERIAL,*map(str,args)],capture_output=True)
    if check and r.returncode:raise RuntimeError(r.stderr.decode(errors='replace')+r.stdout.decode(errors='replace'))
    return r.stdout
def main():
    p=argparse.ArgumentParser();p.add_argument('mode',choices=['baseline','install','launch','capture','tap','text','key','log','stop','crash','exit','events','files','bridge-log','photo']);p.add_argument('args',nargs='*');a=p.parse_args()
    package='com.durango.offserver' if a.mode=='baseline' or 'baseline' in a.args else 'com.losthorizon.tester'
    if a.mode in ('baseline','install'):
        if a.mode=='baseline':
            path=WORK/'community-reference.apk'
            if not path.exists():os.link(ROOT/'Durango-Brasil-v2.2-Lite.zip',path)
            assert not adb('shell','pm','list','packages',package).strip(), 'Baseline package already exists; will not replace it.'
        else:path=ROOT/'android-client/dist/losthorizon-app-tester-001.apk'
        print(adb('install','-r',path).decode(),flush=True)
    if a.mode in ('baseline','launch'):
        print(adb('shell','am','start','-W','-n',package+'/com.durango.offserver.DurangoActivity').decode(),flush=True)
    if a.mode=='capture':
        label=next((x for x in a.args if x!='baseline'),'screen')
        target=WORK/(label+'.png');target.write_bytes(adb('exec-out','screencap','-p'));print(target)
        (WORK/(label+'.log')).write_bytes(adb('logcat','-d','-t','2000'))
        print(adb('shell','pidof',package,check=False).decode())
        print(adb('shell','dumpsys','activity','activities').decode(errors='replace')[-1800:])
    if a.mode=='tap':print(adb('shell','input','tap',*a.args).decode())
    if a.mode=='text':adb('shell','input','text',a.args[0])
    if a.mode=='key':adb('shell','input','keyevent',*a.args)
    if a.mode=='log':
        data=adb('logcat','-d','-s','AppTester','FanNotice','AndroidRuntime','libc','Unity').decode(errors='replace')
        print('\n'.join(x for x in data.splitlines() if any(t in x for t in ('Durango','Tester','FATAL EXCEPTION','Fatal signal','Unity','dbr','fanmod')))[-19000:])
    if a.mode=='stop':adb('shell','am','force-stop',package)
    if a.mode=='crash':
        data=adb('logcat','-d','-b','crash').decode(errors='replace');(WORK/'tester-crash.log').write_text(data,'utf-8');print(data[-22000:])
        print(adb('shell','dumpsys','activity','exit-info',package).decode(errors='replace')[-5000:])
    if a.mode=='exit':print(adb('shell','dumpsys','activity','exit-info',package).decode(errors='replace')[-8000:])
    if a.mode=='events':
        data=adb('logcat','-d','-s','AppTester').decode(errors='replace');(WORK/'tester-events.log').write_text(data,'utf-8');print(data[-8000:])
    if a.mode=='files':print(adb('shell','find','/sdcard/Android/data/'+package+'/files','-maxdepth','3','-type','f',check=False).decode(errors='replace')[-7000:])
    if a.mode=='photo':
        name=a.args[0];assert name.startswith('Durango_') and name.endswith('.png') and '/' not in name and '\\' not in name
        print(adb('pull','/sdcard/Pictures/Durango/'+name,WORK/'tester-photo.png').decode(errors='replace'))
    if a.mode=='bridge-log':
        path=a.args[0];assert path.startswith('/sdcard/Android/data/com.losthorizon.tester/files/') and (path.endswith('.txt') or '/tombstone_' in path)
        data=adb('shell','cat',path);(WORK/'bridge-device.txt').write_bytes(data);print(data.decode(errors='replace')[-11000:])
if __name__=='__main__':main()
