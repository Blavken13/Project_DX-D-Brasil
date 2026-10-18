param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$source = Join-Path $Root 'dino\Spinosaurus\AssetBundles\Windows'
$target = Join-Path $Root 'Lost-Horizon-PC\Lost Horizon Game_Data\StreamingAssets\AssetBundles'
$catalogPath = Join-Path $target 'Info.5.2.1.json'
$embedPath = Join-Path $target 'OffServerEmbed.json'
$framework = 'models$animals$spinosaurus$spinosaurus_framework.asset.bundle'
$prefab = 'models$animals$spinosaurus$spinosaurusprefab.prefab.bundle'
$materialReportPath = Join-Path $source 'native-material-validation.json'
if (-not (Test-Path -LiteralPath $materialReportPath)) {
    throw 'Finalize and validate the native Spinosaurus materials before installation.'
}
$materialReport = Get-Content -Raw -LiteralPath $materialReportPath | ConvertFrom-Json
$prefabHash = (Get-FileHash -LiteralPath (Join-Path $source $prefab) -Algorithm MD5).Hash.ToLowerInvariant()
if ($materialReport.prefab_md5 -ne $prefabHash -or
    $materialReport.SpinosaurusBody.shader -ne 'LitSphere/DualMode/Animal' -or
    $materialReport.SpinosaurusBody.albedo -ne 'Spino_colorized_D') {
    throw 'Native material validation does not match the current Spinosaurus bundle.'
}
$manifest = Get-Content -Raw -LiteralPath (Join-Path $source ($prefab + '.manifest'))
if (-not $manifest.Contains($framework)) {
    throw 'The Spinosaurus prefab bundle does not declare its framework dependency.'
}

$catalog = Get-Content -Raw -LiteralPath $catalogPath | ConvertFrom-Json
$embed = Get-Content -Raw -LiteralPath $embedPath | ConvertFrom-Json
$newEntries = @()
foreach ($name in @($framework, $prefab)) {
    $sourceFile = Join-Path $source $name
    if (-not (Test-Path -LiteralPath $sourceFile)) { throw "Missing built bundle: $sourceFile" }
    $hash = (Get-FileHash -LiteralPath $sourceFile -Algorithm MD5).Hash.ToLowerInvariant()
    $installedName = $name.Substring(0, $name.Length - '.bundle'.Length) + ".${hash}.bundle"
    [string[]]$dependencies = @()
    if ($name -eq $prefab) { $dependencies = [string[]]@($framework) }
    $newEntries += [pscustomobject]@{
        Name = $name
        Hash = $hash
        Crc = $hash
        Size = (Get-Item -LiteralPath $sourceFile).Length
        Dependencies = $dependencies
        Priority = 750
    }
    Copy-Item -LiteralPath $sourceFile -Destination (Join-Path $target $installedName) -Force
}

$activePrefab = @($newEntries | Where-Object Name -eq $prefab)[0]
$activePrefabFile = $prefab.Substring(0, $prefab.Length - '.bundle'.Length) + "." + $activePrefab.Crc + '.bundle'
$archive = Join-Path $Root 'pc-client\work\spino-old-body-bundles'
$resolvedRoot = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\')
$resolvedTarget = (Resolve-Path -LiteralPath $target).Path
if (-not $resolvedTarget.StartsWith($resolvedRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Bundle target is outside the workspace root.'
}
New-Item -ItemType Directory -Path $archive -Force | Out-Null
$resolvedArchive = (Resolve-Path -LiteralPath $archive).Path
if (-not $resolvedArchive.StartsWith($resolvedRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Bundle archive is outside the workspace root.'
}
foreach ($old in Get-ChildItem -LiteralPath $target -File | Where-Object {
    $_.Name -match '^models\$animals\$spinosaurus\$spinosaurusprefab\.prefab\.[0-9a-f]{32}\.bundle$' -and
    $_.Name -ne $activePrefabFile
}) {
    $archived = Join-Path $archive $old.Name
    if (Test-Path -LiteralPath $archived) { throw "Bundle archive collision: $archived" }
    Move-Item -LiteralPath $old.FullName -Destination $archived
}

foreach ($file in @(@($catalog.FileList) | Where-Object { $_.Name -like '*spinosaurus*' })) {
    if ($file.Name -notin @($framework, $prefab)) { throw "Unexpected Spinosaurus entry: $($file.Name)" }
}
foreach ($entry in $newEntries) {
    $catalog.FileList = @($catalog.FileList | Where-Object { $_.Name -ne $entry.Name }) + @($entry)
    $catalog.ItemList = @($catalog.ItemList | Where-Object { $_.Name -ne $entry.Name }) + @([pscustomobject]@{
        Name = $entry.Name
        SavePerDirectory = $false
    })
}

foreach ($path in @($catalogPath, $embedPath)) {
    $backup = $path + '.before-spino-framework.bak'
    if (-not (Test-Path -LiteralPath $backup)) { Copy-Item -LiteralPath $path -Destination $backup }
}
$utf8 = New-Object System.Text.UTF8Encoding($false)
$catalogText = $catalog | ConvertTo-Json -Depth 100
[System.IO.File]::WriteAllText($catalogPath, $catalogText + "`n", $utf8)
$embed.info_md5 = (Get-FileHash -LiteralPath $catalogPath -Algorithm MD5).Hash.ToLowerInvariant()
$active = @('preload.' + $catalog.PreloadCrc + '.bundle') + @($catalog.FileList | ForEach-Object {
    $_.Name.Substring(0, $_.Name.Length - '.bundle'.Length) + '.' + $_.Crc + '.bundle'
})
$embed.files = $active.Count
$embed.bytes = [long]0
foreach ($name in $active) {
    $bundlePath = Join-Path $target $name
    if (-not (Test-Path -LiteralPath $bundlePath)) { throw "Catalog bundle missing: $name" }
    $embed.bytes += (Get-Item -LiteralPath $bundlePath).Length
}
$embed.patched = 'spinosaurus-2214-framework-pc-local'
[System.IO.File]::WriteAllText($embedPath, (($embed | ConvertTo-Json -Compress) + "`n"), $utf8)
Write-Output "Installed Spinosaurus 2214: $($embed.files) bundles, $($embed.bytes) bytes, catalog MD5 $($embed.info_md5)"
$newEntries | Format-Table Name,Hash,Size,Dependencies -AutoSize
