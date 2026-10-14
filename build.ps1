<#
.SYNOPSIS
  Builds FOCA3_Pro.sln and runs the unit tests.
.DESCRIPTION
  The WinForms projects use the legacy csproj format and need Visual Studio's MSBuild; the test projects are
  SDK-style. Some Build Tools installs ship without the .NET SDK resolver, so when it is missing this script
  points MSBuild at the newest installed .NET SDK.
#>
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Debug',
    [switch] $SkipTests
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild not found. Install Visual Studio or Build Tools with the .NET desktop workload.' }

$resolver = Join-Path (Split-Path $msbuild) 'SdkResolvers\Microsoft.DotNet.MSBuildSdkResolver'
if (-not (Test-Path $resolver)) {
    $sdk = & dotnet --list-sdks | ForEach-Object { ($_ -split ' ')[0] } | Sort-Object { [version]($_ -replace '-.*$', '') } | Select-Object -Last 1
    if (-not $sdk) { throw 'No .NET SDK found; install the .NET SDK to build the test projects.' }
    $env:MSBuildSDKsPath = Join-Path (Split-Path (& { (Get-Command dotnet).Source })) "sdk\$sdk\Sdks"
    $env:MSBuildEnableWorkloadResolver = 'false'
}

& $msbuild (Join-Path $root 'FOCA3_Pro.sln') -restore "-p:Configuration=$Configuration" '-p:Platform=Any CPU' -m -v:m -nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $SkipTests) {
    Get-ChildItem (Join-Path $root 'tests') -Recurse -Filter *.Tests.csproj | ForEach-Object {
        & dotnet test $_.FullName --no-build -c $Configuration --nologo
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}
