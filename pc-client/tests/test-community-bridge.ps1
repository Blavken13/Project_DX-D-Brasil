param([string]$Scenario)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot
Add-Type -Path @((Join-Path $taskRoot 'CommunitySessionBridge.cs'),(Join-Path $PSScriptRoot 'CommunityBridgeTests.cs')) -ReferencedAssemblies System.dll,System.Core.dll
[LostHorizon.Tests.CommunityBridgeTests]::Run($Scenario)
