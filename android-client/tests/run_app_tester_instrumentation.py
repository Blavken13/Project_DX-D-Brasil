"""Build/run a separate signed Android instrumentation package for the tester."""
import argparse,importlib.util,json,subprocess,sys,zipfile
from pathlib import Path
sys.stdout.reconfigure(encoding='utf-8')
ROOT=Path(__file__).resolve().parents[1];WORK=ROOT/'work/app-tester-001/instrumentation'
spec=importlib.util.spec_from_file_location('builder',ROOT/'build_app_tester.py');b=importlib.util.module_from_spec(spec);spec.loader.exec_module(b)
ADB=b.SDK/'platform-tools/adb.exe';SERIAL='725b9a01'
def adb(*args):b.run(ADB,'-s',SERIAL,*args)
def build():
    WORK.mkdir(exist_ok=True);manifest=WORK/'AndroidManifest.xml'
    manifest.write_text('''<manifest xmlns:android="http://schemas.android.com/apk/res/android" package="com.losthorizon.tester.tests" android:versionCode="1" android:versionName="1"><uses-sdk android:minSdkVersion="26" android:targetSdkVersion="28"/><application android:label="Aplicação Tester — testes"/><instrumentation android:name="com.losthorizon.tester.tests.AppTesterInstrumentation" android:targetPackage="com.losthorizon.tester" android:functionalTest="true"/></manifest>''','utf-8')
    unsigned=WORK/'test-res.apk';b.run(b.BT/'aapt2.exe','link','--manifest',manifest,'-I',b.ANDROID,'-o',unsigned)
    classes=WORK/'classes';classes.mkdir(exist_ok=True)
    b.run(b.JAVA/'javac.exe','-encoding','UTF-8','-source','8','-target','8','-classpath',b.ANDROID,'-d',classes,ROOT/'tests/AppTesterInstrumentation.java')
    jar=WORK/'classes.jar';b.run(b.JAVA/'jar.exe','cf',jar,'-C',classes,'.')
    dex=WORK/'dex';dex.mkdir(exist_ok=True);b.run(b.JAVA/'java.exe','-cp',b.BT/'lib/d8.jar','com.android.tools.r8.D8','--min-api','26','--lib',b.ANDROID,'--output',dex,jar)
    with zipfile.ZipFile(unsigned,'a') as z:z.write(dex/'classes.dex','classes.dex')
    aligned=WORK/'aligned.apk';b.run(b.BT/'zipalign.exe','-f','4',unsigned,aligned)
    output=WORK/'app-tester-tests.apk';b.run(b.JAVA/'java.exe','-jar',ROOT/'work/apksigner.jar','sign','--ks',ROOT/'durango-br-test.jks','--ks-pass','file:'+str(ROOT/'durango-br-test.pass'),'--v4-signing-enabled','false','--out',output,aligned)
    adb('install','-r',output)
def main():
    p=argparse.ArgumentParser();p.add_argument('mode',choices=('build','ui','invalid','inspect','tap','screen','stop'));p.add_argument('args',nargs='*');a=p.parse_args()
    if a.mode=='build':build();return
    if a.mode in ('tap','stop'):
        action={'action':a.mode}
        if a.mode=='tap':action.update(x=float(a.args[0]),y=float(a.args[1]),name=a.args[2])
        path=WORK/'command.json';path.write_text(json.dumps(action),'utf-8');adb('push',path,'/sdcard/Android/data/com.losthorizon.tester/files/app-tester-command.json');return
    if a.mode=='screen':
        label=a.args[0];adb('pull','/sdcard/Android/data/com.losthorizon.tester/files/app-tester-'+label+'.png',WORK/(label+'.png'));return
    extra=['-e','servers','true'] if 'servers' in a.args else []
    r=subprocess.run([str(ADB),'-s',SERIAL,'shell','am','instrument','-w','-r','-e','mode',a.mode,*extra,'com.losthorizon.tester.tests/com.losthorizon.tester.tests.AppTesterInstrumentation'],capture_output=True,text=True,encoding='utf-8')
    (WORK/(a.mode+'-result.txt')).write_text(r.stdout+r.stderr,'utf-8');print(r.stdout+r.stderr);assert r.returncode==0 and 'PASS:' in r.stdout
if __name__=='__main__':main()
