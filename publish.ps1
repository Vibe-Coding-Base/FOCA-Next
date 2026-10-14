<#
.SYNOPSIS
  Publishes FOCA (desktop app + foca CLI) as self-contained packages: users do not need .NET installed.
.EXAMPLE
  ./publish.ps1 -Runtime win-x64,linux-x64,osx-arm64 -Version 4.0.0
.NOTES
  Output: artifacts/FOCA-<version>-<runtime>.zip (Windows) or .tar.gz (Linux, macOS; keeps the executable bit).
  macOS builds are not signed or notarized here; Gatekeeper requires that for distribution (see release workflow).
#>
param(
    [string[]] $Runtime = @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64'),
    [string] $Version = '4.0.0-dev'
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Force $artifacts | Out-Null

foreach ($rid in $Runtime) {
    $out = Join-Path $artifacts "FOCA-$Version-$rid"
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }

    foreach ($project in 'Foca.App/Foca.App.csproj', 'Foca.Cli/Foca.Cli.csproj') {
        & dotnet publish (Join-Path $root $project) -c Release -r $rid --self-contained true `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none `
            -p:Version=$Version -o $out --nologo -v:m
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    Copy-Item (Join-Path $root 'LICENSE.txt') $out
    Copy-Item (Join-Path $root 'README.md') $out -ErrorAction SilentlyContinue

    if ($rid.StartsWith('win')) {
        $archive = "$out.zip"
        if (Test-Path $archive) { Remove-Item $archive }
        Compress-Archive -Path "$out/*" -DestinationPath $archive
    }
    else {
        $archive = "$out.tar.gz"
        & tar -czf $archive -C $artifacts (Split-Path $out -Leaf)
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    $hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path $archive -Leaf)" | Add-Content (Join-Path $artifacts 'SHA256SUMS')
    Write-Host "Packaged $archive"
}
