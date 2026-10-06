"""Build a scoped PC update from the current assembly, retaining all existing fixes."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parent
MANAGED = PROJECT / 'Durango-OffServer/DurangoV2_Data/Managed'
WORK = ROOT / 'work/enclave1'
DIST = ROOT / 'dist'


def linux(path):
    return '/mnt/' + path.drive[0].lower() + '/' + path.relative_to(path.anchor).as_posix()


def main():
    WORK.mkdir(parents=True, exist_ok=True)
    DIST.mkdir(exist_ok=True)
    source = MANAGED / 'Assembly-CSharp.dll'
    original = WORK / 'Assembly-CSharp.before.dll'
    if original.exists():
        assert original.read_bytes() == source.read_bytes(), 'Input differs from the saved baseline'
    else:
        shutil.copy2(source, original)
    patched = WORK / 'Assembly-CSharp.dll'
    subprocess.run(['dotnet', 'build', str(ROOT / 'Patcher/Patcher.csproj'), '-c', 'Release',
                    '--no-restore', '--nologo'], check=True, cwd=PROJECT)
    runtime = PROJECT / 'Durango-CustomServer/server/bin/test-runtime-linux/dotnet'
    subprocess.run(['wsl', '--distribution', 'Ubuntu-24.04', '--exec', linux(runtime),
                    linux(ROOT / 'Patcher/bin/Release/net9.0/Patcher.dll'), linux(original),
                    linux(MANAGED / 'LostHorizon.PC.dll'), linux(patched), '--estate-only'], check=True, cwd=PROJECT)
    output = DIST / 'LostHorizon-PC-enclave1.zip'
    with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED) as archive:
        archive.write(patched, 'DurangoV2_Data/Managed/Assembly-CSharp.dll')
        archive.writestr('LEIA-ME.txt', 'Feche o jogo. Extraia sobre a pasta Durango-OffServer.\n'
            'Corrige expansao gratuita de territorios. As demais alteracoes do cliente foram preservadas.\n')
    digest = hashlib.sha256(output.read_bytes()).hexdigest()
    output.with_suffix('.zip.sha256').write_text(digest + '  ' + output.name + '\n', encoding='ascii')
    print(json.dumps({'update': str(output), 'sha256': digest, 'installed_client_modified': False}))


if __name__ == '__main__':
    main()
