param(
    [Parameter(Mandatory = $true)][string]$ClientAssembly,
    [string]$PatchedAssembly = (Join-Path $PSScriptRoot 'artifacts\Assembly-CSharp.dll')
)
$ErrorActionPreference = 'Stop'
$target = (Resolve-Path -LiteralPath $ClientAssembly).Path
$patched = (Resolve-Path -LiteralPath $PatchedAssembly).Path
$manifest = Get-Content -LiteralPath "$patched.manifest.json" -Raw | ConvertFrom-Json
$targetHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $target).Hash
if ($targetHash -eq $manifest.sha256) { Write-Output 'World map cursor patch is already installed.'; exit 0 }
if ($targetHash -ne $manifest.inputSha256) { throw 'Client changed since patch generation. Rebuild the patch from the current client DLL.' }
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $patched).Hash -ne $manifest.sha256) { throw 'Patched DLL does not match its validation manifest.' }
if (Get-Process -Name 'Lost Horizon Game' -ErrorAction SilentlyContinue) { throw 'Close the game before installing the patch.' }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = "$target.world-map-pin-$stamp.bak"
$staged = "$target.world-map-pin-$stamp.tmp"
Copy-Item -LiteralPath $patched -Destination $staged
[System.IO.File]::Replace($staged, $target, $backup)
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $target).Hash -ne $manifest.sha256) {
    Copy-Item -LiteralPath $backup -Destination $target -Force
    throw 'Installed DLL failed hash verification; original restored.'
}
Write-Output "Installed: $target"
Write-Output "Backup: $backup"
Write-Output "SHA256: $($manifest.sha256)"
