$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$assembly = Join-Path $project 'Lost-Horizon-PC/Lost Horizon Game_Data/Managed/Assembly-CSharp.dll'
$work = Join-Path $PSScriptRoot 'work/world-map-pin'
$patcher = Join-Path $project 'Durango-CustomServer/tools/WorldMapPin'

function Get-Digest([string] $path) {
    (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}
function Invoke-Patcher([string] $source, [string] $destination) {
    $result = & dotnet (Join-Path $patcher 'bin/Release/net9.0/WorldMapPin.dll') $source $destination (Split-Path -Parent $assembly)
    if ($LASTEXITCODE -ne 0) { throw "World map patch failed: $result" }
    ($result -join "`n") | ConvertFrom-Json
}

New-Item -ItemType Directory -Path $work -Force | Out-Null
$before = Get-Digest $assembly
$backup = Join-Path $work ('Assembly-CSharp.before-world-map-pin-' + $before.Substring(0, 16) + '.dll')
if (Test-Path -LiteralPath $backup) {
    if ((Get-Digest $backup) -ne $before) { throw 'Existing backup differs from installed client' }
} else {
    Copy-Item -LiteralPath $assembly -Destination $backup
}
& dotnet build (Join-Path $patcher 'WorldMapPin.csproj') -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'World map patcher build failed' }
$patched = Join-Path $work 'Assembly-CSharp.world-map-pin.dll'
$report = Invoke-Patcher $assembly $patched
$repeated = Join-Path $work 'Assembly-CSharp.world-map-pin.repeated.dll'
Invoke-Patcher $patched $repeated | Out-Null
if ((Get-Digest $patched) -ne (Get-Digest $repeated)) { throw 'Patch is not idempotent' }
if ((Get-Digest $assembly) -ne $before) { throw 'Client changed while preparing the patch' }
$temporary = Join-Path (Split-Path -Parent $assembly) 'Assembly-CSharp.world-map-pin.tmp'
Copy-Item -LiteralPath $patched -Destination $temporary -Force
try {
    [System.IO.File]::Replace($temporary, $assembly, [System.Management.Automation.Language.NullString]::Value)
} catch {
    throw "Client replacement failed (if the DLL is in use, close Lost Horizon Game): $_"
}
$after = Get-Digest $assembly
if ($after -ne (Get-Digest $patched)) { throw 'Installed DLL differs from validated patch' }
$report | Add-Member -NotePropertyMembers @{
    before_sha256 = $before
    after_sha256 = $after
    backup = $backup
    installed = $assembly
    idempotent = $true
    visual_check = 'not performed'
}
$json = $report | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText((Join-Path $work 'validation.json'), $json)
$json
