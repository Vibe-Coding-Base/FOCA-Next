<#
.SYNOPSIS
  Builds and tests FOCA Next (and, on Windows, FOCA Classic).
.DESCRIPTION
  1. Foca.slnx with the .NET SDK: every project under src/, tests/ and samples/. Runs on Windows,
     Linux and macOS.
  2. Windows only, unless -SkipLegacy: FOCA Classic (legacy/, WinForms on .NET Framework 4.8) and its
     tests, with Visual Studio's MSBuild. It needs Visual Studio or Build Tools with the .NET desktop workload.
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

# 1. FOCA Next
Invoke-Checked { dotnet build (Join-Path $root 'Foca.slnx') -c $Configuration --nologo -v:m }
if (-not $SkipTests) {
    Invoke-Checked { dotnet test (Join-Path $root 'Foca.slnx') -c $Configuration --no-build --nologo }
}

# 2. FOCA Classic
if ($SkipLegacy -or -not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core') { return }

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = if (Test-Path $vswhere) {
    & $vswhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
}
if (-not $msbuild) {
    Write-Warning 'Visual Studio MSBuild not found: skipping FOCA Classic. Use -SkipLegacy to silence this.'
    return
}

# Some Build Tools installs ship without the .NET SDK resolver needed by the SDK-style references
$resolver = Join-Path (Split-Path $msbuild) 'SdkResolvers\Microsoft.DotNet.MSBuildSdkResolver'
if (-not (Test-Path $resolver)) {
    $sdk = & dotnet --list-sdks | ForEach-Object { ($_ -split ' ')[0] } | Sort-Object { [version]($_ -replace '-.*$', '') } | Select-Object -Last 1
    $env:MSBuildSDKsPath = Join-Path (Split-Path (Get-Command dotnet).Source) "sdk\$sdk\Sdks"
    $env:MSBuildEnableWorkloadResolver = 'false'
}

$classicTests = Join-Path $root 'legacy\tests\FOCA.Tests\FOCA.Tests.csproj'
# Restore evaluates every target framework of the shared libraries (net10.0 included), which needs the
# .NET SDK's own MSBuild; Visual Studio's MSBuild then only builds the net48 graph.
Invoke-Checked { dotnet restore $classicTests --nologo -v:q }
Invoke-Checked { & $msbuild $classicTests "-p:Configuration=$Configuration" -m -v:m -nologo }
if (-not $SkipTests) {
    Invoke-Checked { dotnet test $classicTests --no-build -c $Configuration --nologo }
}
