#requires -Version 5.1
<#
.SYNOPSIS
  Builds the native engine via CMake (repo root is the parent of DataGateWin.UI)
  and copies engine.exe plus vcpkg runtime DLLs next to the WinUI app output.
  For Release (or Both), publishes DataGateWin.Installer into the WinUI publish Installer folder.

.EXAMPLE
  pwsh -File .\Build-Engine.ps1
  pwsh -File .\Build-Engine.ps1 -Configuration Release
  pwsh -File .\Build-Engine.ps1 -SkipConfigure
  pwsh -File .\Build-Engine.ps1 -SkipInstaller
#>
param(
    [ValidateSet('Debug', 'Release', 'Both')]
    [string] $Configuration = 'Both',

    [switch] $SkipConfigure,

    # Skip dotnet publish of the Windows installer into WinUI output Installer\.
    [switch] $SkipInstaller,

    # CMake generator (default: VS 2022 x64).
    [string] $Generator = 'Visual Studio 17 2022',

    # Explicit CMake build directory (default: <repo>\build).
    [string] $BuildDirectory = '',

    # Target framework folder name (must match DataGateWin.WinUI .csproj).
    [string] $TargetFramework = 'net10.0-windows10.0.26100.0'
)

$ErrorActionPreference = 'Stop'

$ScriptDir = $PSScriptRoot
$RepoRoot = Split-Path -Parent $ScriptDir
$UiDir = Join-Path $RepoRoot 'DataGateWin.WinUI'
$BuildDir = if ($BuildDirectory) { $BuildDirectory } else { Join-Path $RepoRoot 'build' }

function Find-BuiltEngineExe {
    param([string] $Root, [string] $Config)

    $primary = Join-Path $Root (Join-Path 'engine' (Join-Path $Config 'engine.exe'))
    if (Test-Path -LiteralPath $primary) {
        return (Get-Item -LiteralPath $primary)
    }

    $needle = [IO.Path]::DirectorySeparatorChar + $Config + [IO.Path]::DirectorySeparatorChar
    Get-ChildItem -Path $Root -Recurse -File -Filter 'engine.exe' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName.Contains($needle) } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
}

function Resolve-VcpkgBinDirectory {
    param(
        [ValidateSet('Debug', 'Release')]
        [string] $CMakeConfig
    )
    # vcpkg MSVC: release DLLs in installed\x64-windows\bin; debug (lz4d.dll, etc.) in installed\x64-windows\debug\bin.
    $rel = if ($CMakeConfig -eq 'Debug') { 'installed\x64-windows\debug\bin' } else { 'installed\x64-windows\bin' }

    if ($env:VCPKG_ROOT) {
        $p = Join-Path $env:VCPKG_ROOT $rel
        if (Test-Path -LiteralPath $p) { return $p }
    }
    $cache = Join-Path $BuildDir 'CMakeCache.txt'
    if (Test-Path -LiteralPath $cache) {
        $line = Get-Content -LiteralPath $cache -ErrorAction SilentlyContinue |
            Where-Object { $_ -match '^VCPKG_INSTALLED_DIR:STATIC=(.+)$' } |
            Select-Object -First 1
        if ($line -match '^VCPKG_INSTALLED_DIR:STATIC=(.+)$') {
            $installedRoot = $Matches[1].Trim()
            $b = Join-Path $installedRoot $(if ($CMakeConfig -eq 'Debug') { 'x64-windows\debug\bin' } else { 'x64-windows\bin' })
            if (Test-Path -LiteralPath $b) { return $b }
        }
    }
    return $null
}

function Copy-VcpkgRuntimeDlls {
    param(
        [string] $DestEngineDir,
        [ValidateSet('Debug', 'Release')]
        [string] $CMakeConfig
    )

    $vcpkgBin = Resolve-VcpkgBinDirectory -CMakeConfig $CMakeConfig
    if (-not $vcpkgBin) {
        Write-Warning "vcpkg bin not found (VCPKG_ROOT or VCPKG_INSTALLED_DIR in build\CMakeCache.txt). OpenSSL DLLs were not copied — engine may fail with 0xC0000135 if run without them."
        return
    }

    # OpenSSL: same runtime DLL names for Debug/Release in typical vcpkg x64-windows installs.
    foreach ($n in @('libcrypto-3-x64.dll', 'libssl-3-x64.dll')) {
        $src = Join-Path $vcpkgBin $n
        if (Test-Path -LiteralPath $src) {
            Copy-Item -LiteralPath $src -Destination (Join-Path $DestEngineDir $n) -Force
            Write-Host "Copied DLL: $n -> $DestEngineDir"
        }
    }

    # MSVC debug shared libs often use a 'd' suffix (e.g. lz4d.dll). Prefer debug names; fall back if absent.
    if ($CMakeConfig -eq 'Debug') {
        $candidates = @(
            @( 'jsoncppd.dll', 'jsoncpp.dll' ),
            @( 'zlibd1.dll', 'zlib1.dll' ),
            @( 'lz4d.dll', 'lz4.dll' )
        )
        foreach ($pair in $candidates) {
            $copied = $false
            foreach ($n in $pair) {
                $src = Join-Path $vcpkgBin $n
                if (Test-Path -LiteralPath $src) {
                    Copy-Item -LiteralPath $src -Destination (Join-Path $DestEngineDir $n) -Force
                    Write-Host "Copied DLL: $n -> $DestEngineDir"
                    $copied = $true
                    break
                }
            }
            if (-not $copied) {
                Write-Warning "None of [$($pair -join ', ')] found under vcpkg bin; Debug engine may fail to start."
            }
        }
    }
    else {
        foreach ($n in @('jsoncpp.dll', 'zlib1.dll', 'lz4.dll')) {
            $src = Join-Path $vcpkgBin $n
            if (Test-Path -LiteralPath $src) {
                Copy-Item -LiteralPath $src -Destination (Join-Path $DestEngineDir $n) -Force
                Write-Host "Copied DLL: $n -> $DestEngineDir"
            }
        }
    }
}

function Copy-EngineToUiBin {
    param(
        [System.IO.FileInfo] $EngineExe,
        [string] $UiBinConfigFolder,
        [ValidateSet('Debug', 'Release')]
        [string] $CMakeConfig
    )

    $destDir = Join-Path $UiDir $UiBinConfigFolder
    if (-not (Test-Path -LiteralPath $destDir)) {
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    }

    Copy-Item -LiteralPath $EngineExe.FullName -Destination (Join-Path $destDir 'engine.exe') -Force
    Write-Host "Copied: $($EngineExe.FullName) -> $(Join-Path $destDir 'engine.exe')"
    Copy-VcpkgRuntimeDlls -DestEngineDir $destDir -CMakeConfig $CMakeConfig

    $wintunSrc = Join-Path $RepoRoot 'drivers\wintun\wintun.dll'
    if (Test-Path -LiteralPath $wintunSrc) {
        Copy-Item -LiteralPath $wintunSrc -Destination (Join-Path $destDir 'wintun.dll') -Force
        Write-Host "Copied: wintun.dll -> $destDir"
    }
    else {
        Write-Warning "wintun.dll not found at $wintunSrc — engine cannot load Wintun (see drivers/wintun/README.md)."
    }
}

function Invoke-EngineBuild {
    param([string] $Config)

    cmake --build $BuildDir --config $Config --target engine
    $built = Find-BuiltEngineExe -Root $BuildDir -Config $Config
    if (-not $built) {
        throw "engine.exe not found under $BuildDir for configuration '$Config'."
    }

    $rel = "bin\x64\$Config\$TargetFramework\win-x64\engine"
    Copy-EngineToUiBin -EngineExe $built -UiBinConfigFolder $rel -CMakeConfig $Config
}

function Publish-InstallerToUiRelease {
    $installerProj = Join-Path $RepoRoot 'DataGateWin.Installer\DataGateWin.Installer.csproj'
    if (-not (Test-Path -LiteralPath $installerProj)) {
        Write-Warning "Installer project not found: $installerProj (skipped)."
        return
    }

    $outDir = Join-Path $UiDir "bin\Release\$TargetFramework\win-x64\publish\Installer"
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "dotnet not found in PATH (needed to publish the installer)."
    }

    Write-Host "dotnet publish installer -> $outDir"
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    dotnet publish $installerProj -c Release -o $outDir `
        -p:DebugType=none `
        -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish installer failed with exit code $LASTEXITCODE."
    }

    Get-ChildItem -LiteralPath $outDir -Filter '*.pdb' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
}

if (-not (Get-Command cmake -ErrorAction SilentlyContinue)) {
    throw "cmake not found in PATH. Install CMake and reopen the shell."
}

if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot 'CMakeLists.txt'))) {
    throw "Repository root not found (no CMakeLists.txt): $RepoRoot"
}

if (-not $SkipConfigure -or -not (Test-Path -LiteralPath (Join-Path $BuildDir 'CMakeCache.txt'))) {
    Write-Host "CMake configure: $RepoRoot -> $BuildDir ($Generator)"
    cmake -S $RepoRoot -B $BuildDir -G $Generator -A x64
}

if ($Configuration -eq 'Both') {
    Invoke-EngineBuild -Config 'Debug'
    Invoke-EngineBuild -Config 'Release'
}
else {
    Invoke-EngineBuild -Config $Configuration
}

if (-not $SkipInstaller -and ($Configuration -eq 'Release' -or $Configuration -eq 'Both')) {
    Publish-InstallerToUiRelease
}

Write-Host 'Done.'
