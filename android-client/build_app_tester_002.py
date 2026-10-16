"""Build tester 002 as an isolated update of the verified tester 001 APK."""
import hashlib,json,shutil
import build_app_tester as b

PREVIOUS=b.WORK
SOURCE=b.OUTPUT
SOURCE_SHA256='d3c77f8b53edd270a39c5742b016f631090c10ba0d9411f152d7636d19c0b2d8'
b.WORK=b.ROOT/'work/app-tester-002';b.MAIN=b.WORK/'main'
b.OUTPUT=b.ROOT/'dist/losthorizon-build-tester-002.apk'
b.APP_NAME='Lost Horizon - Build tester';b.ICON=b.PROJECT/'icon2.png'
b.VERSION_CODE=2;b.VERSION_NAME='tester-002'

def main():
    assert hashlib.sha256(SOURCE.read_bytes()).hexdigest()==SOURCE_SHA256
    b.WORK.mkdir(exist_ok=True)
    for folder in ('main','embedded-ui-decoded'):
        target=b.WORK/folder
        if not target.exists():shutil.copytree(PREVIOUS/folder,target)
    native=b.WORK/'base/lib/arm64-v8a';native.mkdir(parents=True,exist_ok=True)
    shutil.copy2(PREVIOUS/'base/lib/arm64-v8a/libdbr.so',native/'libdbr.so')
    b.prepare_main();b.patch_bridge()
    b.audit.update(version_code=2,version_name=b.VERSION_NAME,app_name=b.APP_NAME,
                   source_sha256=SOURCE_SHA256,icon_source='icon2.png',
                   icon_sha256=hashlib.sha256(b.ICON.read_bytes()).hexdigest(),
                   selection_fix='Preserve explicit character/slot across account refresh; validate character membership')
    b.BASE=SOURCE
    b.package()

if __name__=='__main__':main()
