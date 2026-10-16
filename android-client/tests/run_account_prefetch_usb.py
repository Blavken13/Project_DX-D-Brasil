"""Read-only validation on the authorized USB device; no account registration."""
from pathlib import Path
import subprocess, sys, zipfile
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import build_apk as shared
WORK = ROOT / 'work/account-prefetch-tests'; WORK.mkdir(exist_ok=True)
BUILD = ROOT / 'work/account-prefetch-50224'
ADB = shared.SDK / 'platform-tools/adb.exe'
SERIAL = sys.argv[1] if len(sys.argv)>1 else '725b9a01'
def adb(*args):
    return subprocess.check_output([str(ADB),'-s',SERIAL,*map(str,args)],text=True,encoding='utf-8',errors='replace').strip()
classes=WORK/'classes'; classes.mkdir(exist_ok=True)
shared.run(shared.JAVA_BIN/'javac.exe','--release','8','-encoding','UTF-8','-cp',
           str(shared.SDK/'platforms/android-36/android.jar')+';'+str(BUILD/'classes'),'-d',classes,
           ROOT/'tests/AccountPrefetchValidationTest.java')
dex=WORK/'dex'; dex.mkdir(exist_ok=True)
shared.run(shared.JAVA_BIN/'java.exe','-cp',shared.TOOLS/'lib/d8.jar','com.android.tools.r8.D8',
           '--min-api','21','--lib',shared.SDK/'platforms/android-36/android.jar',
           '--classpath',BUILD/'stubs.jar','--output',dex,*classes.rglob('*.class'))
archive=WORK/'account-tests.jar'
with zipfile.ZipFile(archive,'w') as output: output.write(dex/'classes.dex','classes.dex')
print(adb('push',archive,'/data/local/tmp/lh-account-tests.jar'))
package=adb('shell','pm','path','com.nexon.durango.global').splitlines()[0].removeprefix('package:')
print(adb('shell',f'CLASSPATH=/data/local/tmp/lh-account-tests.jar:{package}',
          'app_process','/system/bin','com.newdawn.launcher.AccountPrefetchValidationTest'))
ndk=shared.SDK/'ndk/28.2.13676358/toolchains/llvm/prebuilt/windows-x86_64/bin'
binary=WORK/'handoff-test'
shared.run(ndk/'clang.exe','--target=aarch64-linux-android21','-fPIE','-pie','-O2','-Wall','-Wextra','-Werror',
           '-o',binary,ROOT/'tests/account_handoff_test.c','-ldl','-llog')
print(adb('push',binary,'/data/local/tmp/lh-handoff-test'))
print(adb('shell','chmod','700','/data/local/tmp/lh-handoff-test'))
print(adb('shell','/data/local/tmp/lh-handoff-test'))
