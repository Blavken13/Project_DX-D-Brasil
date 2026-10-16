"""Physical tester-002 selection regression using the existing signed harness."""
import argparse,importlib.util,json,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];WORK=ROOT/'work/app-tester-002/device'
sys.stdout.reconfigure(encoding='utf-8')
spec=importlib.util.spec_from_file_location('harness',ROOT/'tests/run_app_tester_instrumentation.py');h=importlib.util.module_from_spec(spec);spec.loader.exec_module(h)
def adb(*args):
    r=subprocess.run([str(h.ADB),'-s',h.SERIAL,*map(str,args)],capture_output=True)
    if r.returncode:raise RuntimeError((r.stdout+r.stderr).decode(errors='replace'))
    return r.stdout
def main():
    p=argparse.ArgumentParser();p.add_argument('mode',choices=('build','selection','launch','capture','log'));p.add_argument('slot',nargs='?',default='1');a=p.parse_args()
    WORK.mkdir(parents=True,exist_ok=True)
    if a.mode=='build':h.build();return
    if a.mode=='selection':
        adb('shell','am','force-stop','com.losthorizon.tester')
        before=adb('logcat','-d','-s','AppTester').decode(errors='replace')
        r=adb('shell','am','instrument','-w','-r','-e','mode','selection','-e','slot',a.slot,'com.losthorizon.tester.tests/com.losthorizon.tester.tests.AppTesterInstrumentation').decode(errors='replace')
        (WORK/f'slot-{a.slot}-result.txt').write_text(r,'utf-8');print(r,flush=True)
        events=adb('logcat','-d','-s','AppTester').decode(errors='replace')
        events=events[len(before):] if events.startswith(before) else events
        (WORK/f'slot-{a.slot}-events.log').write_text(events,'utf-8')
        for prefix in ('characters','chosen','confirmed','world'):
            name=f'{prefix}-slot{a.slot}';adb('pull',f'/sdcard/Android/data/com.losthorizon.tester/files/app-tester-{name}.png',WORK/(name+'.png'))
        assert 'PASS:' in r,r
        assert f'character_choice_saved slot={a.slot}' in events,events
        assert f'character_choice_restored slot={a.slot}' in events,events
        print('Selection preserved across account refresh; screenshots collected.',flush=True)
    if a.mode=='launch':print(adb('shell','am','start','-W','-n','com.losthorizon.tester/com.durango.offserver.DurangoActivity').decode(errors='replace'))
    if a.mode=='capture':(WORK/('screen-'+a.slot+'.png')).write_bytes(adb('exec-out','screencap','-p'))
    if a.mode=='log':print(adb('logcat','-d','-s','AppTester','AndroidRuntime','libc').decode(errors='replace')[-9000:])
if __name__=='__main__':main()
