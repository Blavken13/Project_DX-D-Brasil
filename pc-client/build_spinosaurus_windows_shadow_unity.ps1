$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$sourceEditor = 'C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor'
$shadowEditor = Join-Path $root 'pc-client\work\unity-shadow-6000.6.0f1\Editor'
$sourceData = Join-Path $sourceEditor 'Data'
$shadowData = Join-Path $shadowEditor 'Data'

function MirrorFiles($source, $target) {
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Extension -ne '.pdb' } | ForEach-Object {
        $destination = Join-Path $target $_.Name
        if (-not (Test-Path -LiteralPath $destination)) {
            Copy-Item -LiteralPath $_.FullName -Destination $destination
        }
    }
}
function Junction($source, $target) {
    if (-not (Test-Path -LiteralPath $target)) {
        New-Item -ItemType Junction -Path $target -Target $source | Out-Null
    }
}

MirrorFiles $sourceEditor $shadowEditor
foreach ($directory in Get-ChildItem -LiteralPath $sourceEditor -Directory | Where-Object Name -ne 'Data') {
    Junction $directory.FullName (Join-Path $shadowEditor $directory.Name)
}
MirrorFiles $sourceData $shadowData
foreach ($directory in Get-ChildItem -LiteralPath $sourceData -Directory | Where-Object Name -ne 'PlaybackEngines') {
    Junction $directory.FullName (Join-Path $shadowData $directory.Name)
}
$sourcePlayback = Join-Path $sourceData 'PlaybackEngines'
$shadowPlayback = Join-Path $shadowData 'PlaybackEngines'
New-Item -ItemType Directory -Path $shadowPlayback -Force | Out-Null
foreach ($directory in Get-ChildItem -LiteralPath $sourcePlayback -Directory | Where-Object Name -ne 'WindowsStandaloneSupport') {
    Junction $directory.FullName (Join-Path $shadowPlayback $directory.Name)
}
$sourceWindows = Join-Path $sourcePlayback 'WindowsStandaloneSupport'
$shadowWindows = Join-Path $shadowPlayback 'WindowsStandaloneSupport'
MirrorFiles $sourceWindows $shadowWindows
foreach ($directory in Get-ChildItem -LiteralPath $sourceWindows -Directory | Where-Object Name -ne 'Variations') {
    Junction $directory.FullName (Join-Path $shadowWindows $directory.Name)
}
$sourceVariations = Join-Path $sourceWindows 'Variations'
$shadowVariations = Join-Path $shadowWindows 'Variations'
MirrorFiles $sourceVariations $shadowVariations
$sourceMono = Join-Path $sourceVariations 'mono'
$shadowMono = Join-Path $shadowVariations 'mono'
MirrorFiles $sourceMono $shadowMono
$sourceManaged = Join-Path $sourceMono 'Managed'
$shadowManaged = Join-Path $shadowMono 'Managed'
MirrorFiles $sourceManaged $shadowManaged
$valid = Join-Path $sourcePlayback 'AndroidPlayer\Variations\mono\Managed\UnityEngine.CoreModule.dll'
Copy-Item -LiteralPath $valid -Destination (Join-Path $shadowManaged 'UnityEngine.CoreModule.dll') -Force

$project = Join-Path $root 'dino\Spinosaurus\UnityProject'
$log = Join-Path $root 'dino\Spinosaurus\unity-rebuild-client-fix.log'
$arguments = '-batchmode -nographics -quit -projectPath "' + $project + '" -executeMethod BuildSpinosaurus.Run -logFile "' + $log + '"'
$process = Start-Process -FilePath (Join-Path $shadowEditor 'Unity.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0) { throw "Shadow Unity exited with code $($process.ExitCode). See $log" }
$report = Get-Content -Raw -LiteralPath (Join-Path $root 'dino\Spinosaurus\Unity_conversion_report.json') | ConvertFrom-Json
if ($report.windowsBundle -ne 'built and verified') { throw "Windows bundle check failed: $($report.windowsBundle)" }
Write-Output 'Windows Spinosaurus bundle rebuilt with repaired shadow Unity.'
