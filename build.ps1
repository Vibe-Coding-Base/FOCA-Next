<#
.SYNOPSIS
  Builds and tests FOCA.
.DESCRIPTION
  Two build paths:
  1. Foca.sln with the .NET SDK: every SDK-style project (shared libraries, CLI, Avalonia app, tests) for
     net10.0, plus net48 on Windows. This is the cross-platform path and the only one on Linux/macOS.
  2. Windows only: the legacy WinForms application (FOCA\FOCA.csproj) and its tests with Visual Studio's
     MSBuild. Shared libraries are pulled in through project references, which select their net48 build.
.PARAMETER SkipLegacy
  Skip the WinForms application even on Windows.
#>
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Debug',
    [switch] $SkipTests,
    [switch] $SkipLegacy
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

function Invoke-Checked([scriptblock] $command) {
    & $command
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# 1. Cross-platform build
Invoke-Checked { dotnet build (Join-Path $root 'Foca.sln') -c $Configuration --nologo -v:m }
if (-not $SkipTests) {
    Invoke-Checked { dotnet test (Join-Path $root 'Foca.sln') -c $Configuration --no-build --nologo }
}

# 2. Legacy WinForms application
if ($SkipLegacy -or -not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core') { return }

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild not found. Install Visual Studio or Build Tools with the .NET desktop workload.' }

# Some Build Tools installs ship without the .NET SDK resolver needed by the SDK-style references
$resolver = Join-Path (Split-Path $msbuild) 'SdkResolvers\Microsoft.DotNet.MSBuildSdkResolver'
if (-not (Test-Path $resolver)) {
    $sdk = & dotnet --list-sdks | ForEach-Object { ($_ -split ' ')[0] } | Sort-Object { [version]($_ -replace '-.*$', '') } | Select-Object -Last 1
    if (-not $sdk) { throw 'No .NET SDK found.' }
    $env:MSBuildSDKsPath = Join-Path (Split-Path (Get-Command dotnet).Source) "sdk\$sdk\Sdks"
    $env:MSBuildEnableWorkloadResolver = 'false'
}

$legacyTests = Join-Path $root 'tests\FOCA.Tests\FOCA.Tests.csproj'
# Restore evaluates every target framework of the shared libraries (net10.0 included), which needs the
# .NET SDK's own MSBuild; Visual Studio's MSBuild then only builds the net48 graph.
Invoke-Checked { dotnet restore $legacyTests --nologo -v:q }
Invoke-Checked { & $msbuild $legacyTests "-p:Configuration=$Configuration" -m -v:m -nologo }
if (-not $SkipTests) {
    Invoke-Checked { dotnet test $legacyTests --no-build -c $Configuration --nologo }
}
