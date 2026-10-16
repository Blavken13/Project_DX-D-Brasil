"""Correct culture-dependent inline icon sizes in the installed Unity PC client."""
from pathlib import Path
import hashlib
import json
import os
import shutil
import subprocess

PROJECT = Path(__file__).resolve().parent.parent
MANAGED = PROJECT / "Lost-Horizon-PC/Lost Horizon Game_Data/Managed"
ASSEMBLY = MANAGED / "Assembly-CSharp.dll"
WORK = PROJECT / "pc-client/work/resource-icon-audit"
PATCHER = PROJECT / "pc-client/CommunityPatcher"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(*args):
    result = subprocess.run(
        [str(arg) for arg in args], cwd=PROJECT, check=True,
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
        text=True, encoding="utf-8", errors="replace",
    )
    return result.stdout


def main():
    if not ASSEMBLY.is_file():
        raise FileNotFoundError(ASSEMBLY)
    WORK.mkdir(parents=True, exist_ok=True)
    before = digest(ASSEMBLY)
    backup = WORK / ("Assembly-CSharp.before-resource-icons-" + before[:16] + ".dll")
    if backup.exists():
        if digest(backup) != before:
            raise RuntimeError("Existing backup does not match the installed client")
    else:
        shutil.copy2(ASSEMBLY, backup)
    run("dotnet", "build", PATCHER / "CommunityPatcher.csproj", "-c", "Release", "--no-restore", "--nologo", "-v", "quiet")
    patched = WORK / "Assembly-CSharp.resource-icons.dll"
    output = run("dotnet", PATCHER / "bin/Release/net9.0/CommunityPatcher.dll", "--resource-icon-scale", ASSEMBLY, patched)
    report = json.loads(output.strip().splitlines()[-1])
    # Keep the original DLL intact until the validated replacement is fully written.
    if digest(ASSEMBLY) != before:
        raise RuntimeError("Installed client changed while preparing the patch")
    temporary = MANAGED / "Assembly-CSharp.resource-icons.tmp"
    shutil.copy2(patched, temporary)
    try:
        os.replace(temporary, ASSEMBLY)
    except PermissionError as error:
        raise RuntimeError("Feche Lost Horizon Game e execute o patch novamente; o Windows está usando a DLL.") from error
    after = digest(ASSEMBLY)
    if after != digest(patched):
        raise RuntimeError("Installed DLL does not match the validated patch")
    report.update(before_sha256=before, after_sha256=after, backup=str(backup), installed=str(ASSEMBLY))
    (WORK / "resource-icons-validation.json").write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    print(json.dumps(report, indent=2, ensure_ascii=False))


if __name__ == "__main__":
    main()
