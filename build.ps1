<#
.SYNOPSIS
    One-click win-x64 build for VGN Orb.

.DESCRIPTION
    Builds and tests the solution, then publishes:
      - a self-contained single-file build (no .NET runtime required)
      - a framework-dependent single-file build (requires .NET 10 Desktop Runtime)
      - a Native AOT build (default)

.PARAMETER Version
    Artifact version, also passed to the assembly version.

.PARAMETER NoAot
    Skip the Native AOT build. AOT is built by default.

.PARAMETER SkipTests
    Skip dotnet test.

.EXAMPLE
    .\build.ps1

.EXAMPLE
    .\build.ps1 -Version 1.1.0

.EXAMPLE
    .\build.ps1 -NoAot
#>
[CmdletBinding()]
param(
    [string]$Version = '1.0.0',
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',
    [switch]$NoAot,
    [switch]$SkipTests
)

$buildAot = -not $NoAot

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

function Get-Sha256 {
    param([Parameter(Mandatory)][string]$Path)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $bytes = $sha.ComputeHash($stream)
        return ([System.BitConverter]::ToString($bytes)).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $stream.Dispose()
        $sha.Dispose()
    }
}

function New-ReleaseArchive {
    param(
        [Parameter(Mandatory)][string]$PublishDirectory,
        [Parameter(Mandatory)][string]$ArchivePath,
        [Parameter(Mandatory)][string]$Label
    )

    if (Test-Path $ArchivePath) {
        Remove-Item -LiteralPath $ArchivePath -Force
    }

    $stage = Join-Path $script:OutputRoot ("stage\" + $Label)
    if (Test-Path $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
    New-Item -ItemType Directory -Path $stage | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $stage 'docs') | Out-Null

    Get-ChildItem -LiteralPath $PublishDirectory -Recurse -File |
        Where-Object { $_.Extension -ne '.pdb' } |
        ForEach-Object {
            $relative = $_.FullName.Substring($PublishDirectory.Length).TrimStart('\')
            $destination = Join-Path $stage $relative
            $destinationDirectory = Split-Path -Parent $destination
            if (-not (Test-Path $destinationDirectory)) {
                New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
            }
            Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
        }

    Copy-Item -LiteralPath 'README.md','LICENSE','THIRD-PARTY-NOTICES.md' -Destination $stage -Force
    Copy-Item -LiteralPath 'docs/vgn-hid-protocol.md' -Destination (Join-Path $stage 'docs') -Force

    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $ArchivePath -CompressionLevel Optimal
    $hash = Get-Sha256 -Path $ArchivePath
    $hashFile = $ArchivePath -replace '\.zip$', '.sha256'
    "$hash  $([System.IO.Path]::GetFileName($ArchivePath))" |
        Set-Content -LiteralPath $hashFile -Encoding utf8

    Write-Host "  $([System.IO.Path]::GetFileName($ArchivePath))  $((Get-Item $ArchivePath).Length) bytes"
    Write-Host "  SHA256  $hash"
}

$dotnetVersion = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet SDK not found. Install the .NET 10 SDK first.'
}

$script:OutputRoot = Join-Path $repoRoot ('artifacts\build-' + $Version)
if (Test-Path $script:OutputRoot) {
    Remove-Item -LiteralPath $script:OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $script:OutputRoot | Out-Null

Write-Host "VGN Orb build $Version ($Runtime)"
Write-Host "dotnet $dotnetVersion"
Write-Host "output $script:OutputRoot"

Write-Host "==> restore and build"
Invoke-DotNet @('restore','VGNOrb.sln','--nologo')
Invoke-DotNet @('build','VGNOrb.sln','-c','Release','--no-restore','--nologo')

if (-not $SkipTests) {
    Write-Host "==> tests"
    Invoke-DotNet @('test','tests/VGNOrb.Core.Tests/VGNOrb.Core.Tests.csproj','-c','Release','--no-build','--nologo')
}

$appProject = 'src/VGNOrb.Avalonia/VGNOrb.Avalonia.csproj'
$commonPublishArgs = @(
    $appProject,
    '-c','Release',
    '-r',$Runtime,
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    ('-p:Version=' + $Version),
    '--nologo'
)

Write-Host "==> publish self-contained"
$selfContainedDir = Join-Path $script:OutputRoot 'self-contained'
Invoke-DotNet (@('publish') + $commonPublishArgs + @('-p:EnableCompressionInSingleFile=true','--self-contained','true','-o',$selfContainedDir))

Write-Host "==> publish framework-dependent"
$frameworkDependentDir = Join-Path $script:OutputRoot 'framework-dependent'
Invoke-DotNet (@('publish') + $commonPublishArgs + @('--self-contained','false','-o',$frameworkDependentDir))

if ($buildAot) {
    Write-Host "==> publish Native AOT"
    $aotDir = Join-Path $script:OutputRoot 'aot'
    Invoke-DotNet @(
        'publish',$appProject,
        '-c','Release',
        '-r',$Runtime,
        '-p:PublishAot=true',
        '-p:NuGetAudit=false',
        '-p:SuppressTrimAnalysisWarnings=true',
        '-p:StripSymbols=true',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        ('-p:Version=' + $Version),
        '-o',$aotDir,
        '--nologo'
    )
}

Write-Host "==> package"
$selfZip = Join-Path $script:OutputRoot ("VGNOrb-$Version-$Runtime-开箱即用.zip")
New-ReleaseArchive -PublishDirectory $selfContainedDir -ArchivePath $selfZip -Label 'self-contained'

$fddZip = Join-Path $script:OutputRoot ("VGNOrb-$Version-$Runtime-需装运行时.zip")
New-ReleaseArchive -PublishDirectory $frameworkDependentDir -ArchivePath $fddZip -Label 'framework-dependent'

if ($buildAot) {
    $aotZip = Join-Path $script:OutputRoot ("VGNOrb-$Version-$Runtime.zip")
    New-ReleaseArchive -PublishDirectory $aotDir -ArchivePath $aotZip -Label 'aot'
}

Write-Host ''
Write-Host "Build complete: $script:OutputRoot"
Get-ChildItem $script:OutputRoot -File | Select-Object Name,Length | Format-Table -AutoSize
