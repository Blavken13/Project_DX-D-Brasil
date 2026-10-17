param(
    [string]$ClientRoot = '',
    [switch]$Install
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $ClientRoot) { $ClientRoot = Join-Path $projectRoot 'Lost-Horizon-PC' }
$ClientRoot = (Resolve-Path -LiteralPath $ClientRoot).Path
$managed = Join-Path $ClientRoot 'Lost Horizon Game_Data/Managed'
$source = Join-Path $managed 'Assembly-CSharp.dll'
$sourceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash.ToLowerInvariant()
$work = Join-Path $PSScriptRoot ('work/estate-unity6/' + $sourceHash)
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $work,$dist -Force | Out-Null
$baseline = Join-Path $work 'Assembly-CSharp.before.dll'
if (Test-Path -LiteralPath $baseline) {
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $baseline).Hash.ToLowerInvariant() -ne $sourceHash) {
        throw 'The saved baseline differs from this client.'
    }
} else { Copy-Item -LiteralPath $source -Destination $baseline }
$toolProject = Join-Path $PSScriptRoot 'EstatePatcher/EstatePatcher.csproj'
dotnet build $toolProject -c Release --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Estate patcher build failed.' }
$tool = Join-Path $PSScriptRoot 'EstatePatcher/bin/Release/net9.0/EstatePatcher.dll'
$patched = Join-Path $work 'Assembly-CSharp.dll'
dotnet $tool $source $patched
if ($LASTEXITCODE -ne 0) { throw 'Client patch validation failed.' }
$validation = Get-Content -LiteralPath ($patched + '.validation.json') -Raw | ConvertFrom-Json
$readme = Join-Path $work 'LEIA-ME-dominio.txt'
@'
Correcao de dominio para o cliente PC Unity 6 atual.

Feche o jogo e extraia este ZIP na pasta Lost-Horizon-PC.
O arquivo substituido fica em Lost Horizon Game_Data/Managed/Assembly-CSharp.dll.

Esta versao envia o pedido de expansao pessoal diretamente ao servidor,
como a DLL antiga embutida no AplicarPatch.exe. O servidor continua verificando
proprietario, adjacencia e limite de oito lotes. Os outros metodos foram preservados.

O AplicarPatch.exe antigo procura DurangoV2_Data e contem a DLL de outra build.
Use este pacote para a pasta atual Lost Horizon Game_Data.
'@ | Set-Content -LiteralPath $readme -Encoding UTF8
$archivePath = Join-Path $dist 'LostHorizon-PC-dominio-unity6.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$stream = [IO.File]::Open($archivePath,[IO.FileMode]::Create,[IO.FileAccess]::Write)
$zip = New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create)
try {
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$patched,'Lost Horizon Game_Data/Managed/Assembly-CSharp.dll') | Out-Null
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$readme,'LEIA-ME-dominio.txt') | Out-Null
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,($patched + '.validation.json'),'dominio-validation.json') | Out-Null
} finally { $zip.Dispose(); $stream.Dispose() }
$backup = Join-Path $managed ('Assembly-CSharp.dll.dominio-backup-' + $sourceHash.Substring(0,12))
if ($Install -and -not $validation.already_patched) {
    if (Get-Process -Name 'Lost Horizon Game' -ErrorAction SilentlyContinue) {
        throw "The package is ready at $archivePath. Close the game before installing it."
    }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash.ToLowerInvariant() -ne $sourceHash) {
        throw 'The installed client changed during patch generation.'
    }
    if (Test-Path -LiteralPath $backup) {
        if ((Get-FileHash -Algorithm SHA256 -LiteralPath $backup).Hash.ToLowerInvariant() -ne $sourceHash) {
            throw 'The existing backup does not match this client.'
        }
    } else { Copy-Item -LiteralPath $source -Destination $backup }
    Copy-Item -LiteralPath $patched -Destination $source
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash.ToLowerInvariant() -ne $validation.output_sha256) {
        throw 'Installed client checksum differs from the validated patch.'
    }
}
$archiveHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash.ToLowerInvariant()
($archiveHash + '  ' + [IO.Path]::GetFileName($archivePath)) | Set-Content -LiteralPath ($archivePath + '.sha256') -Encoding ASCII
[pscustomobject]@{
    archive = $archivePath
    sha256 = $archiveHash
    installed = [bool]$Install
    backup = $backup
    changed_methods = $validation.changed_methods
    preserved_methods = $validation.preserved_methods
} | ConvertTo-Json -Depth 5
