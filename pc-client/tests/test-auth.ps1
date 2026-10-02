param([string]$Gateway, [string]$TestFolder)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot
Add-Type -Path @((Join-Path $taskRoot 'LauncherAuth.cs'), (Join-Path $PSScriptRoot 'AuthTests.cs')) `
    -ReferencedAssemblies System.dll,System.Core.dll,System.Security.dll,System.Web.Extensions.dll
[LostHorizon.Tests.AuthTests]::Run($Gateway, $TestFolder)
