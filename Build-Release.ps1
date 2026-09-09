# Build-Release.ps1 — Release WinUI app + engine + installer + GitHub ZIP (DataGateWin.vX.Y.Z.zip)
param(
    [string]$Configuration = "Release",
    [string]$Version = "1.0.22",
    [string]$VcpkgRoot = "F:\C++\vcpkg",
    [switch]$SkipConfigure,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

$Root = Split-Path $PSScriptRoot -Parent

$UiDir = Join-Path $Root "DataGateWin.WinUI"
$EngineDir = Join-Path $Root "engine"
$InstallerDir = Join-Path $Root "DataGateWin.Installer"
$WintunDll = Join-Path $Root "drivers\wintun\wintun.dll"
$VcpkgBin = Join-Path $VcpkgRoot "installed\x64-windows\bin"
$Tfm = "net10.0-windows10.0.26100.0"
# dotnet publish -r win-x64 writes here; bin\x64\... may hold a stale tiny PRI from smoke tests.
$OutDir = Join-Path $UiDir "bin\$Configuration\$Tfm\win-x64\publish"
if (-not (Test-Path (Join-Path $OutDir "DataGateWin.exe"))) {
    $OutDir = Join-Path $UiDir "bin\x64\$Configuration\$Tfm\win-x64\publish"
}
$EngineOut = Join-Path $OutDir "engine"
$InstallerOut = Join-Path $OutDir "Installer"
$ZipPath = Join-Path $OutDir "DataGateWin.v$Version.zip"

function Require-Path([string]$Path, [string]$Label) {
    if (-not (Test-Path $Path)) {
        throw "$Label not found: $Path"
    }
}

Require-Path $VcpkgBin "vcpkg bin"
Require-Path $WintunDll "wintun.dll"

$fetchLibxray = Join-Path $Root "scripts\libxray\fetch-windows.ps1"
Write-Host "=== Ensure libXray.dll ===" -ForegroundColor Cyan
& $fetchLibxray
Require-Path (Join-Path $Root "engine\third_party\libxray\libXray.dll") "libXray.dll"

Write-Host "=== Configure engine (openvpn3) ===" -ForegroundColor Cyan
$BuildDir = Join-Path $EngineDir "build"
if (-not $SkipConfigure) {
    cmake -S $EngineDir -B $BuildDir `
        -DCMAKE_TOOLCHAIN_FILE="$VcpkgRoot\scripts\buildsystems\vcpkg.cmake" `
        -A x64
}

Write-Host "=== Build engine ($Configuration) ===" -ForegroundColor Cyan
cmake --build $BuildDir --config $Configuration --target engine -- /m

$EngineExe = Join-Path $BuildDir "$Configuration\engine.exe"
Require-Path $EngineExe "engine.exe"

Write-Host "=== Publish WinUI ($Configuration, unpackaged self-contained) ===" -ForegroundColor Cyan
$UiProj = Join-Path $UiDir "DataGateWin.csproj"
dotnet publish $UiProj `
    -c $Configuration `
    -r win-x64 `
    -p:Platform=x64 `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:SelfContained=true `
    -p:PublishTrimmed=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish WinUI failed with exit $LASTEXITCODE"
}

Require-Path $OutDir "WinUI publish dir"
Require-Path (Join-Path $OutDir "DataGateWin.exe") "DataGateWin.exe"
$PriPath = Join-Path $OutDir "DataGateWin.pri"
Require-Path $PriPath "DataGateWin.pri (required for XAML)"
$priLen = (Get-Item $PriPath).Length
if ($priLen -lt 100000) {
    throw "DataGateWin.pri too small ($priLen bytes) — need SDK EmbeddedData PRI with WinUI themes"
}
Write-Host "DataGateWin.pri size=$priLen" -ForegroundColor Green

Write-Host "=== Stage engine + runtime DLLs ===" -ForegroundColor Cyan
# Replace the folder so leftover build junk (e.g. .lib, nested dirs) never ships.
if (Test-Path $EngineOut) {
    Remove-Item -Recurse -Force $EngineOut
}
New-Item -ItemType Directory -Force -Path $EngineOut | Out-Null
Copy-Item -Force $EngineExe (Join-Path $EngineOut "engine.exe")
foreach ($dll in @(
        "libcrypto-3-x64.dll",
        "libssl-3-x64.dll",
        "lz4.dll",
        "jsoncpp.dll")) {
    Copy-Item -Force (Join-Path $VcpkgBin $dll) (Join-Path $EngineOut $dll)
}
Copy-Item -Force $WintunDll (Join-Path $EngineOut "wintun.dll")
Copy-Item -Force (Join-Path $Root "engine\third_party\libxray\libXray.dll") (Join-Path $EngineOut "libXray.dll")

if (-not $SkipInstaller) {
    Write-Host "=== Publish installer (single-file) ===" -ForegroundColor Cyan
    dotnet publish (Join-Path $InstallerDir "DataGateWin.Installer.csproj") `
        -c $Configuration -r win-x64 `
        -p:PublishSingleFile=true -p:SelfContained=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None -p:DebugSymbols=false

    $PublishedInstaller = Join-Path $InstallerDir "bin\$Configuration\net10.0-windows\win-x64\publish\DataGateWin.Installer.exe"
    Require-Path $PublishedInstaller "published installer"
    New-Item -ItemType Directory -Force -Path $InstallerOut | Out-Null
    Copy-Item -Force $PublishedInstaller (Join-Path $InstallerOut "DataGateWin.Installer.exe")

    # Offline package beside the installer tree: parent of Installer\ is searched too.
    # Kept OUT of the GitHub ZIP (not listed in $zipNames) so release assets stay single-copy.
    $localPkgName = "DataGateWinBuild.v$Version"
    $localPkg = Join-Path $OutDir $localPkgName
    Write-Host "=== Stage local build package ($localPkgName) ===" -ForegroundColor Cyan
    if (Test-Path $localPkg) {
        Remove-Item -Recurse -Force $localPkg
    }
    New-Item -ItemType Directory -Force -Path $localPkg | Out-Null
    & robocopy.exe $OutDir $localPkg /E /NFL /NDL /NJH /NJS /R:1 /W:1 `
        /XD "Installer" $localPkgName `
        /XF "DataGateWin.v*.zip" "*.pdb" | Out-Null
    $rc = $LASTEXITCODE
    if ($rc -ge 8) {
        throw "robocopy local build package failed with exit $rc"
    }
    Require-Path (Join-Path $localPkg "DataGateWin.exe") "local build DataGateWin.exe"
    Write-Host "  Local offline install: run Installer\DataGateWin.Installer.exe (uses $localPkgName, no GitHub)." -ForegroundColor Green
}

Write-Host "=== Create release ZIP ===" -ForegroundColor Cyan
if (Test-Path $ZipPath) { Remove-Item -Force $ZipPath }

# Unpackaged WinUI: pack the FULL publish tree. Locale folders (en-us\*.mui, ru-RU\*.mui, …)
# are required — omitting them causes install-from-GitHub FailFast 0x80073B01 / 0xC000027B
# after ShowMain while the same bits still run from the publish folder.
$zipExcludeNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
[void]$zipExcludeNames.Add((Split-Path $ZipPath -Leaf))
[void]$zipExcludeNames.Add("_pri_work")
Get-ChildItem -Path $OutDir -Directory -Filter "DataGateWinBuild*" -EA SilentlyContinue |
    ForEach-Object { [void]$zipExcludeNames.Add($_.Name) }

$zipPaths = @(
    Get-ChildItem -Path $OutDir -Force |
        Where-Object {
            -not $zipExcludeNames.Contains($_.Name) -and
            $_.Name -notlike "DataGateWin.v*.zip" -and
            $_.Extension -ne ".pdb"
        } |
        Select-Object -ExpandProperty FullName
)

if ($zipPaths.Count -eq 0) {
    throw "Nothing to pack into release ZIP."
}

$muiDirCount = @(
    Get-ChildItem -Path $OutDir -Directory -EA SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName "Microsoft.ui.xaml.dll.mui") }
).Count
if ($muiDirCount -lt 1) {
    throw "Publish folder has no WinUI MUI locale dirs (en-us\Microsoft.ui.xaml.dll.mui missing) — refuse to ship a broken ZIP."
}
Write-Host "  Packing $($zipPaths.Count) roots ($muiDirCount WinUI MUI locale dirs)." -ForegroundColor Green

Push-Location $OutDir
try {
    $relative = $zipPaths | ForEach-Object {
        $_.Substring($OutDir.Length).TrimStart('\', '/')
    }
    Compress-Archive -Path $relative -DestinationPath $ZipPath -CompressionLevel Optimal
}
finally {
    Pop-Location
}

# Guard: ZIP must contain at least one MUI satellite (prevents silent packaging regressions).
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipCheck = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
try {
    $muiInZip = @($zipCheck.Entries | Where-Object { $_.FullName -like "*.mui" }).Count
    if ($muiInZip -lt 1) {
        throw "Release ZIP missing *.mui satellites ($muiInZip) — WinUI will FailFast after install."
    }
    Write-Host "  ZIP OK: $muiInZip .mui entries, $([math]::Round((Get-Item $ZipPath).Length/1MB,1)) MB" -ForegroundColor Green
}
finally {
    $zipCheck.Dispose()
}

Write-Host "Done." -ForegroundColor Green
Write-Host "  App:       $OutDir\DataGateWin.exe"
Write-Host "  Engine:    $EngineOut\engine.exe"
if (-not $SkipInstaller) {
    Write-Host "  Installer: $InstallerOut\DataGateWin.Installer.exe"
}
Write-Host "  ZIP:       $ZipPath"
Write-Host "  Layout:    docs\WINUI3_PUBLISH_LAYOUT.md"
