"""Build the Android floor shader in a disposable Unity project."""
import json
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parent
EDITOR = Path('C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe')
PROJECT = ROOT / 'work/shader-project'

def main():
    shader_dir = PROJECT / 'Assets/Resources/Shaders'
    editor_dir = PROJECT / 'Assets/Editor'
    for directory in [shader_dir, editor_dir, PROJECT / 'ProjectSettings', PROJECT / 'Packages']:
        directory.mkdir(parents=True, exist_ok=True)
    shutil.copy2(ROOT / 'shaders/Floor2AlphaUV.shader', shader_dir)
    shutil.copy2(ROOT / 'shaders/BuildCompatibilityShaders.cs', editor_dir)
    (PROJECT / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 6000.6.0f1\n', encoding='utf-8')
    (PROJECT / 'Packages/manifest.json').write_text('{"dependencies":{}}\n', encoding='utf-8')
    subprocess.run([str(EDITOR), '-batchmode', '-nographics', '-projectPath', str(PROJECT),
                    '-executeMethod', 'BuildCompatibilityShaders.Run', '-logFile',
                    str(ROOT / 'work/unity6/shader-build.log')], check=True)
    output = ROOT / 'branding/compatibility'
    output.mkdir(parents=True, exist_ok=True)
    shutil.copy2(ROOT / 'work/unity6/compat-shaders/durango-br-shaders.bundle', output)
    import hashlib
    bundle = output / 'durango-br-shaders.bundle'
    (output / 'manifest.json').write_text(json.dumps({
        'unity_version': '6000.6.0f1', 'platform': 'Android',
        'file': bundle.name, 'sha256': hashlib.sha256(bundle.read_bytes()).hexdigest(),
        'source_sha256': hashlib.sha256((ROOT / 'shaders/Floor2AlphaUV.shader').read_bytes()).hexdigest(),
        'shader': 'Durango/Building/Floor2AlphaUV'
    }, indent=2) + '\n', encoding='utf-8')

if __name__ == '__main__':
    main()
