"""Compile the pre-game login and patch only the managed launcher integration."""
import os
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parent
CLIENT = PROJECT / 'Durango-OffServer'
MANAGED = CLIENT / 'DurangoV2_Data/Managed'
FRAMEWORK = Path(os.environ.get('WINDIR', 'C:/Windows')) / 'Microsoft.NET/Framework64/v4.0.30319'
WORK = ROOT / 'work'


def run(*args):
    subprocess.run([str(a) for a in args], cwd=PROJECT, check=True)


def wsl(path):
    return '/mnt/' + path.drive[0].lower() + '/' + path.relative_to(path.anchor).as_posix()


def build_launcher():
    import shutil
    shutil.copy2(ROOT / 'LauncherAuth.cs', CLIENT / 'Launcher/LauncherAuth.cs')
    references = [FRAMEWORK / name for name in ['System.dll', 'System.Core.dll', 'System.Security.dll',
        'System.Web.Extensions.dll', 'System.Xaml.dll', 'System.Xml.dll']]
    references += [FRAMEWORK / 'WPF' / name for name in ['WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll']]
    run(FRAMEWORK / 'csc.exe', '/nologo', '/platform:x64', '/target:winexe',
        '/out:' + str(CLIENT / 'DurangoBrasil.exe'), '/win32icon:' + str(CLIENT / 'Launcher/icon.ico'),
        *['/reference:' + str(path) for path in references],
        CLIENT / 'DurangoLauncher.cs', ROOT / 'LauncherAuth.cs')


def main():
    import sys
    if '--launcher-only' in sys.argv:
        build_launcher()
        print('PASS: PC launcher compiled; Unity files unchanged')
        return
    import apply_branding
    apply_branding.patch()
    import shutil
    build_launcher()
    bridge = MANAGED / 'LostHorizon.PC.dll'
    run(FRAMEWORK / 'csc.exe', '/nologo', '/target:library', '/out:' + str(bridge),
        '/reference:' + str(MANAGED / 'Assembly-CSharp.dll'), ROOT / 'LauncherSessionBridge.cs')
    run('dotnet', 'build', ROOT / 'Patcher/Patcher.csproj', '-c', 'Release', '--nologo')
    # Windows SAC can refuse unsigned dotnet tools. Execute the verifier with the
    # existing isolated Linux runtime, without changing Windows security policy.
    runtime = PROJECT / 'Durango-CustomServer/server/bin/test-runtime-linux/dotnet'
    patched = WORK / 'Assembly-CSharp.patched.dll'
    run('wsl.exe', '--exec', wsl(runtime), wsl(ROOT / 'Patcher/bin/Release/net9.0/Patcher.dll'),
        wsl(WORK / 'Assembly-CSharp.dll'), wsl(bridge), wsl(patched))
    shutil.copy2(patched, MANAGED / 'Assembly-CSharp.dll')
    print('PC client ready: DurangoBrasil.exe -> login -> original splash -> authenticated character selection')


if __name__ == '__main__':
    main()
