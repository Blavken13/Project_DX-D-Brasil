# Source launcher for environments that allow PowerShell but block unsigned EXEs.
# Uses the same C# implementation; does not change Windows application-control policy.
param([switch]$Preview)
$ErrorActionPreference = 'Stop'
try {
    $taskClient = Split-Path $PSScriptRoot
    $taskFramework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
    $taskRefs = @('System.dll', 'System.Core.dll', 'System.Security.dll', 'System.Web.Extensions.dll', 'System.Xaml.dll', 'System.Xml.dll') |
        ForEach-Object { Join-Path $taskFramework $_ }
    $taskRefs += @('WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll') |
        ForEach-Object { Join-Path (Join-Path $taskFramework 'WPF') $_ }
    Add-Type -AssemblyName PresentationFramework
    Add-Type -Path @((Join-Path $taskClient 'DurangoLauncher.cs'), (Join-Path $PSScriptRoot 'LauncherAuth.cs')) `
        -ReferencedAssemblies $taskRefs
    $taskFile = [IO.File]::OpenRead((Join-Path $PSScriptRoot 'login.xaml'))
    try { $taskWindow = [Windows.Markup.XamlReader]::Load($taskFile) } finally { $taskFile.Dispose() }
    [Program]::Run($taskWindow, [string]$taskClient, [bool]$Preview)
} catch {
    $taskErrorLog = Join-Path $PSScriptRoot 'launcher-error.log'
    $_.Exception.ToString() | Set-Content -LiteralPath $taskErrorLog -Encoding UTF8
    Add-Type -AssemblyName PresentationFramework
    [Windows.MessageBox]::Show('Não foi possível abrir Lost Horizon. Verifique a pasta do cliente e o .NET Framework.' +
        [Environment]::NewLine + $_.Exception.Message, 'Lost Horizon') | Out-Null
    exit 1
}
